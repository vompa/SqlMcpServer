namespace SqlMcpServer.Data;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>Pfad zur SQLite-Datei (relativ zum Content-Root oder absolut).</summary>
    public string DataSource { get; set; } = "db/demo.db";

    /// <summary>Maximale Zeilenzahl pro Abfrage. Schützt Server und Kontextfenster des Agenten.</summary>
    public int MaxRows { get; set; } = 200;

    public int QueryTimeoutSeconds { get; set; } = 10;

    /// <summary>Legt beim Start eine Demo-Datenbank an, falls die Datei nicht existiert.</summary>
    public bool SeedDemoData { get; set; } = true;
}
