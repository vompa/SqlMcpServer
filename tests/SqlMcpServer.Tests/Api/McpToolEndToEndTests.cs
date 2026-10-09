using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace SqlMcpServer.Tests.Api;

public sealed class McpToolEndToEndTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    private async Task<McpClient> ConnectAsync()
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                AdditionalHeaders = new Dictionary<string, string> { ["X-Api-Key"] = McpServerFactory.ValidApiKey },
            },
            factory.CreateClient(),
            ownsHttpClient: true);
        return await McpClient.CreateAsync(transport);
    }

    private static string TextOf(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));

    [Fact]
    public async Task List_tables_returns_demo_tables_over_mcp()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("list_tables");

        Assert.NotEqual(true, result.IsError);
        var text = TextOf(result);
        foreach (var table in new[] { "customers", "order_items", "orders", "products" })
            Assert.Contains(table, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_table_returns_columns_over_mcp()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("describe_table",
            new Dictionary<string, object?> { ["table"] = "customers" });

        Assert.NotEqual(true, result.IsError);
        var text = TextOf(result);
        Assert.Contains("email", text, StringComparison.Ordinal);
        Assert.Contains("country", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Describe_unknown_table_returns_a_tool_error()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("describe_table",
            new Dictionary<string, object?> { ["table"] = "does_not_exist" });

        Assert.True(result.IsError);
        Assert.Contains("existiert nicht", TextOf(result), StringComparison.Ordinal);
    }
}
