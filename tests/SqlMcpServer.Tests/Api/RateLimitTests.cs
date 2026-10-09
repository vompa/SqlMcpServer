using System.Net;
using System.Text;

namespace SqlMcpServer.Tests.Api;

public sealed class RateLimitTests(RateLimitedServerFactory factory) : IClassFixture<RateLimitedServerFactory>
{
    private const string InitializeRequest = """
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}
        """;

    private static async Task<HttpStatusCode> PostAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(InitializeRequest, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Headers.Add("X-Api-Key", key);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Third_request_within_a_minute_is_rate_limited_and_other_client_has_own_partition()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, RateLimitedServerFactory.KeyA));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, RateLimitedServerFactory.KeyA));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostAsync(client, RateLimitedServerFactory.KeyA));

        // Partition = Client-Name: ein anderer Client ist vom Limit des ersten unberührt.
        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, RateLimitedServerFactory.KeyB));
    }

    [Fact]
    public async Task Partition_is_the_client_name_so_two_keys_of_one_client_share_the_limit()
    {
        // Eigene Factory-Instanz, damit der Zähler von den anderen Tests unberührt ist.
        await using var isolated = new RateLimitedServerFactory();
        using var client = isolated.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, RateLimitedServerFactory.KeyA));
        Assert.Equal(HttpStatusCode.OK, await PostAsync(client, RateLimitedServerFactory.KeyA2));
        Assert.Equal(HttpStatusCode.TooManyRequests, await PostAsync(client, RateLimitedServerFactory.KeyA));
    }
}
