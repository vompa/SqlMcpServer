using System.Text;
using System.Text.RegularExpressions;

namespace SqlMcpServer.Data;

/// <summary>
/// Erste Verteidigungslinie: lässt nur einzelne, lesende Abfragen (SELECT / WITH ... SELECT) durch.
/// Die eigentliche Absicherung ist die schreibgeschützte Datenbankverbindung (zweite Linie) –
/// der Guard liefert dem Agenten aber früh eine verständliche Fehlermeldung.
/// </summary>
public static partial class SqlGuard
{
    public const int MaxLength = 4000;

    private static readonly HashSet<string> ForbiddenKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "insert", "update", "delete", "drop", "alter", "create", "truncate",
        "attach", "detach", "pragma", "vacuum", "reindex", "analyze",
        "load_extension", "grant", "revoke", "exec", "execute", "begin", "commit", "rollback", "savepoint",
    };

    public static void EnsureReadOnly(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
            throw new QueryRejectedException("Die Abfrage ist leer.");
        if (sql.Length > MaxLength)
            throw new QueryRejectedException($"Die Abfrage ist zu lang (maximal {MaxLength} Zeichen).");

        // Literale und quotierte Bezeichner entfernen, damit z. B. ein Schlüsselwort in einem String
        // oder eine Spalte namens "update" nicht fälschlich abgelehnt wird.
        var stripped = StripQuoted(sql);

        if (stripped.Contains("--", StringComparison.Ordinal) || stripped.Contains("/*", StringComparison.Ordinal))
            throw new QueryRejectedException("Kommentare sind in Abfragen nicht erlaubt.");

        var body = stripped.TrimEnd().TrimEnd(';');
        if (body.Contains(';', StringComparison.Ordinal))
            throw new QueryRejectedException("Es ist nur genau eine Anweisung erlaubt.");

        var words = WordRegex().Matches(body).Select(m => m.Value).ToList();
        if (words.Count == 0 || !(words[0].Equals("select", StringComparison.OrdinalIgnoreCase)
                                  || words[0].Equals("with", StringComparison.OrdinalIgnoreCase)))
            throw new QueryRejectedException("Nur SELECT-Abfragen sind erlaubt.");

        var forbidden = words.FirstOrDefault(ForbiddenKeywords.Contains);
        if (forbidden is not null)
            throw new QueryRejectedException($"Das Schlüsselwort '{forbidden.ToUpperInvariant()}' ist nicht erlaubt.");
    }

    /// <summary>Ersetzt '...', "...", `...` und [...] durch Platzhalter. Unterminierte Literale werden abgelehnt.</summary>
    private static string StripQuoted(string sql)
    {
        var result = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c is not ('\'' or '"' or '`' or '['))
            {
                result.Append(c);
                continue;
            }

            var close = c == '[' ? ']' : c;
            var j = i + 1;
            while (true)
            {
                if (j >= sql.Length)
                    throw new QueryRejectedException("Unvollständiges Literal oder Bezeichner in der Abfrage.");
                if (sql[j] == close)
                {
                    // Verdoppeltes Quote ('') ist ein escaptes Zeichen
                    if (close != ']' && j + 1 < sql.Length && sql[j + 1] == close) { j += 2; continue; }
                    break;
                }
                j++;
            }

            result.Append("''");
            i = j;
        }
        return result.ToString();
    }

    [GeneratedRegex("[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex WordRegex();
}
