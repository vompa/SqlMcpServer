using System.Net;
using System.Net.Http.Headers;
using System.Text;
using ModelContextProtocol.Client;

namespace SqlMcpServer.Tests.Api;

public sealed class McpEndpointTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """;

    private static StringContent JsonBody() => new(InitializeRequest, Encoding.UTF8, "application/json");

    private static HttpRequestMessage McpPost(Action<HttpRequestHeaders>? headers = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = JsonBody() };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        headers?.Invoke(request.Headers);
        return request;
    }

    [Fact]
    public async Task Health_endpoint_is_public()
    {
        var response = await factory.CreateClient().GetAsync(new Uri("/health", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_without_credentials_is_rejected()
    {
        var response = await factory.CreateClient().SendAsync(McpPost());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Bearer", response.Headers.WwwAuthenticate.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("wrong-key")]
    [InlineData("")]
    public async Task Mcp_with_wrong_api_key_is_rejected(string key)
    {
        var response = await factory.CreateClient().SendAsync(McpPost(h => h.Add("X-Api-Key", key)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_with_wrong_bearer_token_is_rejected()
    {
        var response = await factory.CreateClient().SendAsync(
            McpPost(h => h.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key")));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Mcp_accepts_valid_key_as_header_and_as_bearer()
    {
        var viaHeader = await factory.CreateClient().SendAsync(McpPost(h => h.Add("X-Api-Key", McpServerFactory.ValidApiKey)));
        var viaBearer = await factory.CreateClient().SendAsync(
            McpPost(h => h.Authorization = new AuthenticationHeaderValue("Bearer", McpServerFactory.ValidApiKey)));

        Assert.Equal(HttpStatusCode.OK, viaHeader.StatusCode);
        Assert.Equal(HttpStatusCode.OK, viaBearer.StatusCode);
    }

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

    [Fact]
    public async Task Client_discovers_the_three_tools()
    {
        await using var client = await ConnectAsync();

        var tools = await client.ListToolsAsync();

        Assert.Equal(["describe_table", "list_tables", "run_query"], tools.Select(t => t.Name).Order());
    }

    [Fact]
    public async Task Agent_can_run_a_select_query_end_to_end()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("run_query",
            new Dictionary<string, object?> { ["sql"] = "SELECT name FROM customers WHERE country = 'CH' ORDER BY name" });

        Assert.NotEqual(true, result.IsError);
        var text = string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        Assert.Contains("Greta Keller", text, StringComparison.Ordinal);
        Assert.Contains("Hans Brunner", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_attempt_returns_a_tool_error_with_explanation()
    {
        await using var client = await ConnectAsync();

        var result = await client.CallToolAsync("run_query",
            new Dictionary<string, object?> { ["sql"] = "DELETE FROM customers" });

        Assert.True(result.IsError);
        var text = string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        Assert.Contains("Nur SELECT-Abfragen sind erlaubt", text, StringComparison.Ordinal);
    }
}
