using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace SqlMcpServer.Data;

/// <summary>
/// Lesender Datenbankzugriff für den Agenten. Sicherheit in mehreren Schichten:
/// 1. <see cref="SqlGuard"/> lehnt alles außer einzelnen SELECTs ab,
/// 2. die Verbindung ist auf Datenbankebene schreibgeschützt (Mode=ReadOnly + query_only),
/// 3. Tabellennamen werden gegen das Schema geprüft, nie ungeprüft in SQL konkateniert,
/// 4. Zeilenzahl und Laufzeit sind begrenzt.
/// </summary>
public sealed class DatabaseService(IOptions<DatabaseOptions> options)
{
    private readonly DatabaseOptions _options = options.Value;

    public async Task<IReadOnlyList<TableInfo>> ListTablesAsync(CancellationToken ct)
    {
        await using var connection = await OpenReadOnlyAsync(ct);
        var names = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\' ORDER BY name";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                names.Add(reader.GetString(0));
        }

        var tables = new List<TableInfo>();
        foreach (var name in names)
        {
            await using var count = connection.CreateCommand();
            count.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(name)}";
            var rows = Convert.ToInt64(await count.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            tables.Add(new TableInfo(name, rows));
        }
        return tables;
    }

    public async Task<IReadOnlyList<ColumnInfo>> DescribeTableAsync(string table, CancellationToken ct)
    {
        await using var connection = await OpenReadOnlyAsync(ct);

        // Der Name wird nur verwendet, wenn er exakt einer existierenden Tabelle entspricht.
        string canonicalName;
        await using (var lookup = connection.CreateCommand())
        {
            lookup.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name AND name NOT LIKE 'sqlite\\_%' ESCAPE '\\'";
            lookup.Parameters.AddWithValue("$name", table ?? string.Empty);
            canonicalName = await lookup.ExecuteScalarAsync(ct) as string
                ?? throw new QueryRejectedException($"Die Tabelle '{table}' existiert nicht. Rufe zuerst list_tables auf.");
        }

        await using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({QuoteIdentifier(canonicalName)})";
        await using var reader = await command.ExecuteReaderAsync(ct);

        var columns = new List<ColumnInfo>();
        while (await reader.ReadAsync(ct))
        {
            columns.Add(new ColumnInfo(
                Name: reader.GetString(reader.GetOrdinal("name")),
                Type: reader.GetString(reader.GetOrdinal("type")),
                NotNull: reader.GetInt32(reader.GetOrdinal("notnull")) != 0,
                PrimaryKey: reader.GetInt32(reader.GetOrdinal("pk")) != 0));
        }
        return columns;
    }

    public async Task<QueryResult> QueryAsync(string sql, CancellationToken ct)
    {
        SqlGuard.EnsureReadOnly(sql);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.QueryTimeoutSeconds));

        try
        {
            await using var connection = await OpenReadOnlyAsync(timeout.Token);
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = _options.QueryTimeoutSeconds;
            await using var reader = await command.ExecuteReaderAsync(timeout.Token);

            var columns = Enumerable.Range(0, reader.FieldCount).Select(reader.GetName).ToList();
            var rows = new List<IReadOnlyList<object?>>();
            var truncated = false;

            while (await reader.ReadAsync(timeout.Token))
            {
                if (rows.Count >= _options.MaxRows)
                {
                    truncated = true;
                    break;
                }

                var row = new object?[reader.FieldCount];
                for (var i = 0; i < row.Length; i++)
                    row[i] = ToJsonFriendly(reader.GetValue(i));
                rows.Add(row);
            }

            return new QueryResult(columns, rows, truncated);
        }
        catch (SqliteException ex)
        {
            // Fehlertext geht an den Agenten, damit er die Abfrage korrigieren kann
            throw new QueryRejectedException($"SQL-Fehler: {ex.Message}");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new QueryRejectedException($"Die Abfrage hat das Zeitlimit von {_options.QueryTimeoutSeconds} s überschritten.");
        }
    }

    /// <summary>Öffnet die Datenbank ausschließlich lesend. Zweite Sicherheitsschicht neben dem <see cref="SqlGuard"/>.</summary>
    internal async Task<SqliteConnection> OpenReadOnlyAsync(CancellationToken ct)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DataSource,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA query_only = ON";
            await pragma.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string QuoteIdentifier(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private static object? ToJsonFriendly(object value) => value switch
    {
        DBNull => null,
        byte[] bytes => $"<blob, {bytes.Length} Bytes>",
        _ => value,
    };
}
