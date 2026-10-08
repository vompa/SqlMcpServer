using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SqlMcpServer.Data;

namespace SqlMcpServer.Tools;

/// <summary>Die Tools, die der KI-Agent über MCP aufrufen kann. Alle sind rein lesend.</summary>
[McpServerToolType]
public sealed partial class DatabaseTools(DatabaseService database, IHttpContextAccessor httpContext, ILogger<DatabaseTools> logger)
{
    private string Client => httpContext.HttpContext?.User.Identity?.Name ?? "unbekannt";

    [McpServerTool(Name = "list_tables", ReadOnly = true, Idempotent = true)]
    [Description("Listet alle Tabellen der Datenbank mit ihrer Zeilenzahl. Das ist der richtige erste Schritt, um das Schema zu erkunden.")]
    public async Task<IReadOnlyList<TableInfo>> ListTables(CancellationToken cancellationToken)
    {
        LogToolCall("list_tables", null);
        return await Run(() => database.ListTablesAsync(cancellationToken));
    }

    [McpServerTool(Name = "describe_table", ReadOnly = true, Idempotent = true)]
    [Description("Beschreibt die Spalten einer Tabelle (Name, Typ, NOT NULL, Primärschlüssel).")]
    public async Task<IReadOnlyList<ColumnInfo>> DescribeTable(
        [Description("Exakter Tabellenname, wie ihn list_tables liefert.")] string table,
        CancellationToken cancellationToken)
    {
        LogToolCall("describe_table", table);
        return await Run(() => database.DescribeTableAsync(table, cancellationToken));
    }

    [McpServerTool(Name = "run_query", ReadOnly = true, Idempotent = true)]
    [Description("Führt eine einzelne lesende SQL-Abfrage (SQLite-Dialekt, nur SELECT oder WITH ... SELECT) aus. " +
                 "Ergebnis ist auf eine feste Zeilenzahl begrenzt; 'truncated' zeigt an, ob Zeilen fehlen. " +
                 "Verwende Aggregation und WHERE, statt ganze Tabellen zu laden.")]
    public async Task<QueryResult> RunQuery(
        [Description("Genau eine SELECT-Anweisung ohne Kommentare.")] string sql,
        CancellationToken cancellationToken)
    {
        LogToolCall("run_query", sql);
        return await Run(() => database.QueryAsync(sql, cancellationToken));
    }

    /// <summary>
    /// Regelverstöße werden als <see cref="McpException"/> weitergereicht: Das SDK liefert deren Text an den Agenten
    /// zurück, sodass er seine Abfrage korrigieren kann. Alle anderen Fehler bleiben intern (generische Meldung).
    /// </summary>
    private static async Task<T> Run<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (QueryRejectedException ex)
        {
            throw new McpException(ex.Message);
        }
    }

    /// <summary>Audit-Log: wer hat welches Tool mit welchem Argument aufgerufen (Argument auf 500 Zeichen gekürzt).</summary>
    private void LogToolCall(string tool, string? argument)
    {
        if (!logger.IsEnabled(LogLevel.Information))
            return;

        var shortened = argument is { Length: > 500 } ? argument[..500] + "…" : argument;
        LogAudit(Client, tool, shortened);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Client '{Client}' ruft {Tool} auf: {Argument}")]
    private partial void LogAudit(string client, string tool, string? argument);
}
