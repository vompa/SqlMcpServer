using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using SqlMcpServer.Security;

namespace SqlMcpServer.Tests.Api;

/// <summary>Startet den echten Server im Speicher – mit eigener Demo-Datenbank und einem bekannten Test-Key.</summary>
public sealed class McpServerFactory : WebApplicationFactory<Program>
{
    public const string ValidApiKey = "test-key-0123456789";

    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"sqlmcp-it-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Authentication:ApiKeys:0:Name", "test-client");
        builder.UseSetting("Authentication:ApiKeys:0:KeySha256", ApiKeyHasher.Hash(ValidApiKey));
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
