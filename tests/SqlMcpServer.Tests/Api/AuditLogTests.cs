using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace SqlMcpServer.Tests.Api;

public sealed class AuditLogTests(AuditLogServerFactory factory) : IClassFixture<AuditLogServerFactory>
{
    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = AuditLogServerFactory.ApiKey },
            },
            factory.CreateClient(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private List<CapturedLog> AuditEntries(string tool) =>
        [.. factory.Logs.Entries.Where(e =>
            e.Category.EndsWith("DatabaseTools", StringComparison.Ordinal)
            && e.Level == LogLevel.Information
            && e.State.TryGetValue("tool", out var t) && t == tool)];

    [Fact]
    public async Task Tool_call_is_audited_with_client_and_tool()
    {
        await using var client = await ConnectAsync();

        await client.CallToolAsync("describe_table", new Dictionary<string, object?> { ["table"] = "customers" });

        var entry = Assert.Single(AuditEntries("describe_table"));
        Assert.Equal(AuditLogServerFactory.ClientName, entry.State["client"]);
        Assert.Equal("customers", entry.State["argument"]);
    }

    [Fact]
    public async Task Long_arguments_are_truncated_to_500_characters_in_the_audit_log()
    {
        await using var client = await ConnectAsync();
        var sql = "SELECT '" + new string('x', 700) + "'";

        await client.CallToolAsync("run_query", new Dictionary<string, object?> { ["sql"] = sql });

        var entry = Assert.Single(AuditEntries("run_query"));
        Assert.Equal(AuditLogServerFactory.ClientName, entry.State["client"]);
        Assert.Equal(sql[..500] + "…", entry.State["argument"]);
        Assert.DoesNotContain(new string('x', 600), entry.Message, StringComparison.Ordinal);
    }
}
