namespace SqlMcpServer.Security;

public sealed class ApiKeyOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Zugelassene Clients. Gespeichert wird nur der SHA-256-Hash, nie der Klartext-Key.</summary>
    public IList<ApiKeyEntry> ApiKeys { get; } = [];
}

public sealed class ApiKeyEntry
{
    /// <summary>Anzeigename des Clients, taucht im Audit-Log auf.</summary>
    public string Name { get; set; } = "";

    /// <summary>SHA-256 des Keys als Hex-String (erzeugbar mit <c>dotnet run -- hash-key &lt;key&gt;</c>).</summary>
    public string KeySha256 { get; set; } = "";
}
