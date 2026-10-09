using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace SqlMcpServer.Tests.Api;

public sealed class AuthEdgeCaseTests(McpServerFactory factory) : IClassFixture<McpServerFactory>
{
    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """;

    private async Task<HttpStatusCode> PostAsync(Action<HttpRequestHeaders> headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(InitializeRequest, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        headers(request.Headers);
        using var client = factory.CreateClient();
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Duplicate_api_key_headers_are_rejected_even_if_both_are_valid()
    {
        var status = await PostAsync(h =>
        {
            h.Add("X-Api-Key", McpServerFactory.ValidApiKey);
            h.Add("X-Api-Key", McpServerFactory.ValidApiKey);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Theory]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer    ")]
    public async Task Bearer_without_token_is_rejected(string value)
    {
        var status = await PostAsync(h => h.TryAddWithoutValidation("Authorization", value));

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Wrong_api_key_header_wins_over_valid_bearer()
    {
        var status = await PostAsync(h =>
        {
            h.Add("X-Api-Key", "wrong-key");
            h.Authorization = new AuthenticationHeaderValue("Bearer", McpServerFactory.ValidApiKey);
        });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    [Fact]
    public async Task Valid_api_key_header_wins_over_wrong_bearer()
    {
        var status = await PostAsync(h =>
        {
            h.Add("X-Api-Key", McpServerFactory.ValidApiKey);
            h.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-key");
        });

        Assert.Equal(HttpStatusCode.OK, status);
    }

    [Fact]
    public async Task Whitespace_api_key_header_falls_back_to_valid_bearer()
    {
        var status = await PostAsync(h =>
        {
            h.TryAddWithoutValidation("X-Api-Key", " ");
            h.Authorization = new AuthenticationHeaderValue("Bearer", McpServerFactory.ValidApiKey);
        });

        Assert.Equal(HttpStatusCode.OK, status);
    }
}
