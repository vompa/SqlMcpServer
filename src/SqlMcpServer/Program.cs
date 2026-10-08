using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using SqlMcpServer.Data;
using SqlMcpServer.Security;
using SqlMcpServer.Tools;

// Hilfsbefehl: dotnet run --project src/SqlMcpServer -- hash-key "<dein-key>"
if (args is ["hash-key", var plainKey])
{
    Console.WriteLine(ApiKeyHasher.Hash(plainKey));
    return;
}

var builder = WebApplication.CreateBuilder(args);

// --- Konfiguration (Fehler werden beim Start gemeldet, nicht erst beim ersten Request) ---
builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .PostConfigure(o => o.DataSource = Path.GetFullPath(o.DataSource, builder.Environment.ContentRootPath))
    .Validate(o => o.MaxRows > 0 && o.QueryTimeoutSeconds > 0, "MaxRows und QueryTimeoutSeconds müssen > 0 sein.")
    .ValidateOnStart();

builder.Services.AddOptions<ApiKeyOptions>()
    .Bind(builder.Configuration.GetSection(ApiKeyOptions.SectionName))
    .Validate(o => o.ApiKeys.Count > 0, "Es ist kein API-Key konfiguriert (Authentication:ApiKeys). Der Server startet nicht ungeschützt.")
    .Validate(o => o.ApiKeys.All(k => k.Name.Length > 0 && ApiKeyHasher.TryParseHash(k.KeySha256, out _)),
        "Jeder API-Key braucht einen Namen und einen gültigen SHA-256-Hash (64 Hex-Zeichen).")
    .ValidateOnStart();

// --- Authentifizierung & Autorisierung: standardmäßig ist alles geschützt ---
builder.Services.AddAuthentication(ApiKeyDefaults.Scheme)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyDefaults.Scheme, _ => { });
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// --- Rate Limiting pro Client (bzw. pro IP für nicht authentifizierte Anfragen, bremst Key-Raten) ---
var requestsPerMinute = builder.Configuration.GetValue("RateLimit:RequestsPerMinute", 60);
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = requestsPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

// --- Anwendung ---
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DatabaseService>();
builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<DatabaseTools>();

var app = builder.Build();

var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
if (database.SeedDemoData)
    DemoDataSeeder.EnsureCreated(database.DataSource);

app.UseAuthentication();
app.UseRateLimiter();   // nach Authentication (kennt den Client), vor Authorization (bremst auch fehlgeschlagene Versuche)
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapMcp("/mcp");

app.Run();

public partial class Program;
