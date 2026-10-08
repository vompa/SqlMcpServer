using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using SqlMcpServer.Data;

namespace SqlMcpServer.Tests.Data;

public sealed class DatabaseServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sqlmcp-{Guid.NewGuid():N}.db");

    public DatabaseServiceTests() => DemoDataSeeder.EnsureCreated(_dbPath);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
    }

    private DatabaseService CreateService(int maxRows = 200) =>
        new(Options.Create(new DatabaseOptions { DataSource = _dbPath, MaxRows = maxRows }));

    [Fact]
    public async Task ListTables_returns_demo_tables_with_row_counts()
    {
        var tables = await CreateService().ListTablesAsync(CancellationToken.None);

        Assert.Equal(["customers", "order_items", "orders", "products"], tables.Select(t => t.Name));
        Assert.Equal(8, tables.Single(t => t.Name == "customers").RowCount);
    }

    [Fact]
    public async Task DescribeTable_returns_columns()
    {
        var columns = await CreateService().DescribeTableAsync("customers", CancellationToken.None);

        Assert.Contains(columns, c => c is { Name: "id", PrimaryKey: true });
        Assert.Contains(columns, c => c is { Name: "email", NotNull: true });
    }

    [Theory]
    [InlineData("does_not_exist")]
    [InlineData("customers; drop table customers")]
    [InlineData("\"customers\" --")]
    public async Task DescribeTable_rejects_unknown_or_malicious_names(string table) =>
        await Assert.ThrowsAsync<QueryRejectedException>(() =>
            CreateService().DescribeTableAsync(table, CancellationToken.None));

    [Fact]
    public async Task Query_returns_columns_and_rows()
    {
        var result = await CreateService().QueryAsync(
            "SELECT country, COUNT(*) AS n FROM customers GROUP BY country ORDER BY country", CancellationToken.None);

        Assert.Equal(["country", "n"], result.Columns);
        Assert.Equal("AT", result.Rows[0][0]);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task Query_truncates_at_max_rows()
    {
        var result = await CreateService(maxRows: 3).QueryAsync("SELECT * FROM orders", CancellationToken.None);

        Assert.Equal(3, result.Rows.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Query_reports_sql_errors_as_rejected_query() =>
        await Assert.ThrowsAsync<QueryRejectedException>(() =>
            CreateService().QueryAsync("SELECT * FROM missing_table", CancellationToken.None));

    [Fact]
    public async Task Query_blocks_writes_in_guard_and_leaves_data_untouched()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<QueryRejectedException>(() =>
            service.QueryAsync("DELETE FROM customers", CancellationToken.None));

        var count = await service.QueryAsync("SELECT COUNT(*) FROM customers", CancellationToken.None);
        Assert.Equal(8L, count.Rows[0][0]);
    }

    [Fact]
    public async Task Connection_itself_is_read_only_even_without_guard()
    {
        // Zweite Schicht: selbst wenn der Guard umgangen würde, darf die Verbindung nicht schreiben.
        await using var connection = await CreateService().OpenReadOnlyAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM customers";

        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
    }
}
