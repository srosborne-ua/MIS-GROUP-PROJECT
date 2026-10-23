using System.Text;
using System.Text.RegularExpressions;

namespace API.Concierge;

// Backup layer for the concierge's model-written knowledge queries: a query runs only
// if every check here passes. The real protection is the druid_llm_ro login, which
// can only SELECT from the approved view (create-llm-ro-user.sql).
//
// Required checks run on the full query text, string literals included. The
// structural checks (one approved view, no subqueries) run on the query with its
// string literals blanked out, so a search term like '%from%' isn't mistaken for SQL.
public static class KnowledgeQueryValidator
{
    public const string ApprovedView = "v_knowledge_documents";
    public static readonly IReadOnlyList<string> ApprovedViews = [ApprovedView];

    private const int MaxLength = 1000;

    // Whole words only, so columns like created_at and updated_at aren't blocked.
    private static readonly Regex BlockedKeywords = new(
        @"\b(DROP|DELETE|UPDATE|INSERT|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|GRANT|REVOKE)\b|\bINTO\s+OUTFILE\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StartsWithSelect = new(@"^SELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Beyond the required list, outside string literals: other ways a SELECT can write
    // files, stall the server, lock rows, or read session and connection details.
    private static readonly Regex BlockedOutsideLiterals = new(
        @"\b(INTO|LOAD_FILE|SLEEP|BENCHMARK|FOR\s+SHARE|LOCK\s+IN|JOIN|UNION)\b" +
        @"|\b(USER|CURRENT_USER|SESSION_USER|SYSTEM_USER|DATABASE|SCHEMA|VERSION|CONNECTION_ID)\s*\(" +
        @"|@",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Word = new(@"\b(SELECT|FROM)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // After FROM: the view (optionally `mis321`.-qualified and/or backquoted), an
    // optional alias, then only a WHERE / GROUP BY / HAVING / ORDER BY / LIMIT tail.
    // A comma (a second table) or anything else after the view fails to match.
    private static readonly Regex FromClause = new(
        @"\bFROM\s+(?:`?mis321`?\s*\.\s*)?`?(?<view>[A-Za-z0-9_]+)`?" +
        @"(?:\s+(?:AS\s+)?(?!(?:WHERE|GROUP|HAVING|ORDER|LIMIT)\b)[A-Za-z_][A-Za-z0-9_]*)?" +
        @"\s*(?:\b(?:WHERE|GROUP|HAVING|ORDER|LIMIT)\b.*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    // Returns null when the query may run, or the reason it was rejected. The reason is
    // for the server log only; the model gets a generic message.
    public static string? Check(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return "empty";
        var query = sql.Trim();
        if (query.Length > MaxLength) return "too long";

        // One statement only: no statement separators at all.
        if (query.Contains(';')) return "semicolon";
        if (query.Contains("--") || query.Contains("/*") || query.Contains("*/") || query.Contains('#')) return "comment";
        if (!StartsWithSelect.IsMatch(query)) return "not a SELECT";
        if (BlockedKeywords.IsMatch(query)) return "blocked keyword";

        var structure = BlankStringLiterals(query);
        if (structure is null) return "unterminated string";
        if (BlockedOutsideLiterals.IsMatch(structure)) return "blocked construct";

        var words = Word.Matches(structure).Select(m => m.Value.ToUpperInvariant()).ToList();
        if (words.Count(w => w == "SELECT") != 1) return "subquery";
        if (words.Count(w => w == "FROM") != 1) return "one FROM required";

        var from = FromClause.Match(structure);
        if (!from.Success) return "unexpected FROM clause";
        if (!ApprovedViews.Contains(from.Groups["view"].Value, StringComparer.OrdinalIgnoreCase)) return "not an approved view";

        return null;
    }

    // Replaces each '...' or "..." literal with '' so structural checks only see SQL.
    // Returns null for an unterminated literal.
    private static string? BlankStringLiterals(string sql)
    {
        var result = new StringBuilder(sql.Length);
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (c != '\'' && c != '"')
            {
                result.Append(c);
                continue;
            }

            var quote = c;
            var closed = false;
            for (i++; i < sql.Length; i++)
            {
                if (sql[i] == '\\') { i++; continue; }
                if (sql[i] != quote) continue;
                if (i + 1 < sql.Length && sql[i + 1] == quote) { i++; continue; }  // doubled quote
                closed = true;
                break;
            }
            if (!closed) return null;
            result.Append("''");
        }
        return result.ToString();
    }
}
