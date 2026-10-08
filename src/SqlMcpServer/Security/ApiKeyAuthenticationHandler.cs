using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace SqlMcpServer.Security;

public static class ApiKeyDefaults
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

/// <summary>
/// Authentifiziert Clients über <c>X-Api-Key: &lt;key&gt;</c> oder <c>Authorization: Bearer &lt;key&gt;</c>.
/// Der Vergleich erfolgt über Hashes in konstanter Zeit und ohne frühen Abbruch, damit weder Timing
/// noch die Position eines Treffers Informationen über gültige Keys preisgeben.
/// </summary>
public sealed partial class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> schemeOptions,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<ApiKeyOptions> apiKeyOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(schemeOptions, loggerFactory, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ReadKey();
        if (presented is null)
            return Task.FromResult(AuthenticateResult.NoResult());

        var presentedHash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(presented));

        string? clientName = null;
        foreach (var entry in apiKeyOptions.Value.ApiKeys)
        {
            if (ApiKeyHasher.TryParseHash(entry.KeySha256, out var expected)
                && CryptographicOperations.FixedTimeEquals(presentedHash, expected))
            {
                clientName = entry.Name;
            }
        }

        if (clientName is null)
        {
            LogInvalidKey(Logger, Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Ungültiger API-Key."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, clientName), new Claim(ClaimTypes.NameIdentifier, clientName)],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = "Bearer";
        return base.HandleChallengeAsync(properties);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ungültiger API-Key von {RemoteIp}")]
    private static partial void LogInvalidKey(ILogger logger, System.Net.IPAddress? remoteIp);

    private string? ReadKey()
    {
        if (Request.Headers.TryGetValue(ApiKeyDefaults.HeaderName, out var header) && header.Count == 1
            && !string.IsNullOrWhiteSpace(header[0]))
            return header[0];

        const string prefix = "Bearer ";
        var authorization = Request.Headers.Authorization.ToString();
        if (authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var token = authorization[prefix.Length..].Trim();
            return token.Length > 0 ? token : null;
        }

        return null;
    }
}
