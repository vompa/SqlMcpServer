using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using SqlMcpServer.Security;

namespace SqlMcpServer.Tests.Api;

/// <summary>Server mit sehr knappem Rate Limit (2 pro Minute) und drei Keys: zwei verschiedene Clients plus ein zweiter Key für denselben Client.</summary>
public sealed class RateLimitedServerFactory : WebApplicationFactory<Program>
{
    public const string KeyA = "rate-key-a-0123456789";
    public const string KeyB = "rate-key-b-0123456789";
    public const string KeyA2 = "rate-key-a2-0123456789";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sqlmcp-rl-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("RateLimit:RequestsPerMinute", "2");
        builder.UseSetting("Authentication:ApiKeys:0:Name", "client-a");
        builder.UseSetting("Authentication:ApiKeys:0:KeySha256", ApiKeyHasher.Hash(KeyA));
        builder.UseSetting("Authentication:ApiKeys:1:Name", "client-b");
        builder.UseSetting("Authentication:ApiKeys:1:KeySha256", ApiKeyHasher.Hash(KeyB));
        builder.UseSetting("Authentication:ApiKeys:2:Name", "client-a");
        builder.UseSetting("Authentication:ApiKeys:2:KeySha256", ApiKeyHasher.Hash(KeyA2));
        builder.UseSetting("Database:DataSource", _dbPath);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }
}

/// <summary>Server, dessen Log-Einträge von einem Fake-<see cref="ILoggerProvider"/> mitgeschnitten werden (Audit-Log-Test).</summary>
public sealed class AuditLogServerFactory : WebApplicationFactory<Program>
{
    public const string ApiKey = "audit-key-0123456789";
    public const string ClientName = "audit-client";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sqlmcp-audit-{Guid.NewGuid():N}.db");

    public CapturingLoggerProvider Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:ApiKeys:0:Name", ClientName);
        builder.UseSetting("Authentication:ApiKeys:0:KeySha256", ApiKeyHasher.Hash(ApiKey));
        builder.UseSetting("Database:DataSource", _dbPath);
        builder.ConfigureLogging(logging => logging.AddProvider(Logs));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_dbPath);
        }
    }
}

public sealed record CapturedLog(string Category, LogLevel Level, string Message, IReadOnlyDictionary<string, string?> State);

public sealed class CapturingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public IReadOnlyCollection<CapturedLog> Entries => [.. _entries];

    public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class CapturingLogger(string category, ConcurrentQueue<CapturedLog> sink) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (state is IEnumerable<KeyValuePair<string, object?>> pairs)
            {
                foreach (var (key, value) in pairs)
                    values[key] = value?.ToString();
            }
            sink.Enqueue(new CapturedLog(category, logLevel, formatter(state, exception), values));
        }
    }
}
