# SqlMcpServer

Ein kleiner, bewusst sauber gehaltener **MCP-Server (Model Context Protocol) in C# / .NET 10**, der einem KI-Agenten
kontrollierten, **rein lesenden Zugriff auf eine SQL-Datenbank** gibt – abgesichert durch Authentifizierung,
Eingabeprüfung, eine schreibgeschützte Datenbankverbindung, Limits und Audit-Log.

Das Projekt ist als Referenz gedacht: wenig Code, aber jede Entscheidung ist begründet (siehe
[Designentscheidungen](#designentscheidungen)) und durch Tests belegt.

```
KI-Agent / MCP-Client                    SqlMcpServer (ASP.NET Core)                      SQLite
┌──────────────────┐   HTTPS + API-Key   ┌─────────────────────────────────────────┐   ┌──────────┐
│ Claude, VS Code, │ ─────────────────▶ │ API-Key-Auth → Rate Limit → MCP-Endpoint │   │          │
│ eigene Agenten   │ ◀───────────────── │   list_tables · describe_table · run_query│ ─▶│ read-only│
└──────────────────┘   JSON-Ergebnis     │   SqlGuard → DatabaseService (Limits)    │   │ Verbindung│
                                         └─────────────────────────────────────────┘   └──────────┘
```

## Funktionen

| MCP-Tool         | Zweck                                                                         |
|------------------|-------------------------------------------------------------------------------|
| `list_tables`    | Tabellen mit Zeilenzahl – erster Schritt, um das Schema zu erkunden           |
| `describe_table` | Spalten, Typen, `NOT NULL`, Primärschlüssel einer Tabelle                     |
| `run_query`      | Eine einzelne `SELECT`-/`WITH … SELECT`-Abfrage, Ergebnis auf N Zeilen begrenzt |

Fehler (z. B. verbotenes SQL oder Syntaxfehler) kommen mit **verständlichem Text** beim Agenten an, damit er seine
Abfrage selbst korrigieren kann. Interne Fehler dagegen werden nur generisch gemeldet.

## Schnellstart

Voraussetzung: [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
# 1. Server starten (Development-Profil enthält einen Demo-Key: dev-key-change-me)
ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/SqlMcpServer --urls http://localhost:5080

# 2. Testen
curl -s -X POST http://localhost:5080/mcp \
  -H "Content-Type: application/json" -H "Accept: application/json, text/event-stream" \
  -H "X-Api-Key: dev-key-change-me" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"run_query","arguments":{"sql":"SELECT country, COUNT(*) AS kunden FROM customers GROUP BY country"}}}'
```

Beim ersten Start wird eine Demo-Datenbank (`db/demo.db`) mit einem kleinen Shop-Schema
(`customers`, `products`, `orders`, `order_items`) angelegt.

PowerShell: `$env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run --project src/SqlMcpServer --urls http://localhost:5080`

### An einen Agenten anbinden

Claude Code:

```bash
claude mcp add --transport http sqlmcp http://localhost:5080/mcp --header "X-Api-Key: dev-key-change-me"
```

Danach kann man z. B. fragen: *„Welche Kunden aus der Schweiz haben bestellt, und wie hoch ist ihr Umsatz?“* – der
Agent erkundet das Schema mit `list_tables`/`describe_table` und formuliert die passende SQL-Abfrage.

### Eigenen Key erzeugen (Produktion)

Konfiguriert wird nur der **SHA-256-Hash** eines Keys, nie der Klartext:

```bash
KEY=$(openssl rand -base64 32)                      # zufälliger Key – nur an den Client geben
dotnet run --project src/SqlMcpServer -- hash-key "$KEY"   # Hash ausgeben
```

```jsonc
// Konfiguration, z. B. per Umgebungsvariable Authentication__ApiKeys__0__KeySha256 oder Secret Store
"Authentication": { "ApiKeys": [ { "Name": "reporting-agent", "KeySha256": "<hash>" } ] }
```

Ohne konfigurierten Key **startet der Server nicht** (fail closed).

## Sicherheitsmodell

Ein Agent, der SQL schreibt, ist ein nicht vertrauenswürdiger Client – auch ein „braver“ Agent kann per Prompt Injection
manipuliert werden. Deshalb gilt Defense in Depth:

| Schicht | Maßnahme | Wo |
|---|---|---|
| Authentifizierung | API-Key (`X-Api-Key` oder `Authorization: Bearer`), nur Hash gespeichert, Vergleich in konstanter Zeit | `Security/` |
| Secure by default | Fallback-Policy verlangt Login für **jeden** Endpunkt; nur `/health` ist anonym | `Program.cs` |
| Brute-Force-Bremse | Rate Limit pro Client bzw. IP (auch für fehlgeschlagene Versuche) | `Program.cs` |
| Eingabeprüfung | Nur eine Anweisung, nur `SELECT`/`WITH`, keine Kommentare, Schlüsselwort-Sperrliste, Längenlimit; Literale werden vor der Prüfung neutralisiert | `Data/SqlGuard.cs` |
| Schreibschutz in der DB | Verbindung mit `Mode=ReadOnly` **und** `PRAGMA query_only` – greift auch, falls der Guard umgangen würde | `Data/DatabaseService.cs` |
| Kein SQL-Zusammenbau | Tabellennamen werden gegen das Schema geprüft und quotiert, Werte laufen als Parameter | `Data/DatabaseService.cs` |
| Ressourcenschutz | Zeilenlimit (`MaxRows`) und Zeitlimit pro Abfrage | `Data/DatabaseService.cs` |
| Nachvollziehbarkeit | Audit-Log: welcher Client hat welches Tool mit welchem Argument aufgerufen | `Tools/DatabaseTools.cs` |

## Tests

```bash
dotnet test
```

| Bereich | Was geprüft wird |
|---|---|
| `SqlGuardTests` | erlaubte/abgelehnte Abfragen, Mehrfach-Statements, Kommentare, Quote-Tricks, Längenlimit |
| `DatabaseServiceTests` | Schema-Abfragen, Zeilenlimit, SQL-Fehler, **Schreibschutz der Verbindung auch ohne Guard** |
| `ApiKeyHasherTests` | Hashing gegen bekannten SHA-256-Testvektor, Validierung von Hash-Strings |
| `McpEndpointTests` | Integration gegen den echten Server im Speicher: 401 ohne/mit falschem Key, Key per Header und Bearer, Tool-Discovery per MCP-Client, `SELECT` Ende-zu-Ende, Schreibversuch → Tool-Fehler |

Die CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) baut mit `TreatWarningsAsErrors` und Analyzern
(`AnalysisLevel=latest-recommended`) und führt alle Tests aus.

## Projektstruktur

```
src/SqlMcpServer/
  Program.cs                    Verdrahtung: Konfiguration, Auth, Rate Limit, MCP
  Security/                     API-Key-Handler, Hashing, Optionen
  Data/                         SqlGuard, DatabaseService, Demo-Daten
  Tools/DatabaseTools.cs        die MCP-Tools, die der Agent sieht
tests/SqlMcpServer.Tests/       Unit- und Integrationstests (xUnit)
```

## Designentscheidungen

- **HTTP-Transport (stateless) statt stdio:** Ein Server mit Authentifizierung und Audit gehört in einen Dienst, nicht in
  einen Kindprozess des Clients. „Stateless“ erlaubt horizontale Skalierung ohne Sticky Sessions.
- **API-Key statt OAuth:** Die MCP-Spezifikation sieht für öffentliche Server OAuth 2.1 vor. Für einen internen Dienst mit
  wenigen bekannten Clients sind API-Keys einfacher und ehrlicher; die Erweiterung auf OAuth/Entra ID ist unten skizziert.
- **SHA-256 für Keys:** Keys sind zufällig und hochentropisch, ein schneller Hash genügt. Für Benutzerpasswörter wäre das
  falsch (dort PBKDF2/Argon2).
- **Guard + read-only Verbindung:** Der Guard ist Komfort (frühe, erklärende Fehler) und eine Verteidigungslinie, aber
  Textfilter für SQL sind nie allein vertrauenswürdig – darum ist die Datenbankverbindung die verbindliche Grenze.
- **SQLite:** Null Setup für Reviewer. Die Trennung `DatabaseService`/`SqlGuard` hält den Wechsel auf SQL Server oder
  PostgreSQL klein.

## Bekannte Grenzen und nächste Schritte

- **Zeitlimit bei SQLite ist kooperativ:** `Microsoft.Data.Sqlite` kann eine laufende Abfrage nicht hart unterbrechen; bei
  SQL Server/PostgreSQL übernimmt `CommandTimeout` bzw. Statement-Timeout das serverseitig.
- **Alle Tabellen sind sichtbar:** Eine Allowlist freigegebener Tabellen/Spalten (oder Views) wäre der nächste Schritt für
  produktive Daten, ebenso Maskierung personenbezogener Spalten.
- **Eine Rolle:** Alle Keys haben dieselben Rechte; Rollen/Scopes pro Key wären eine einfache Erweiterung.
- **OAuth 2.1 / Entra ID:** `AddJwtBearer` statt `ApiKey`-Schema – Handler und Policy-Struktur bleiben gleich.
- **Rate-Limit-Zustand pro Instanz:** Bei mehreren Instanzen braucht es einen gemeinsamen Speicher (z. B. Redis) oder ein Gateway.

## Lizenz

[MIT](LICENSE)
