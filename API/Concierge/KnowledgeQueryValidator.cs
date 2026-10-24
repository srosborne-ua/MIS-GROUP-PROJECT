using System.Text;
using System.Text.RegularExpressions;

namespace API.Concierge;

// Backup layer for the concierge's model-written queries: a query runs only if every
// check here passes. The real protection is the druid_llm_ro login, which can only
// SELECT from the approved views (create-llm-ro-user.sql, migrate-003).
//
// Required checks run on the full query text, string literals included. The
// structural checks (approved views only, no blocked functions) run on the query with
// its string literals blanked out, so a search term like '%from%' isn't mistaken for SQL.
//
// JOINs, UNIONs, GROUP BY, and subqueries are allowed, so the model can count and
// combine rows, but every FROM and JOIN must name an approved view (or open a subquery,
// whose own FROM is checked the same way).
public static class KnowledgeQueryValidator
{
    public static readonly IReadOnlyList<string> ApprovedViews =
        ["v_knowledge_documents", "v_hotel", "v_room_types", "v_rooms", "v_special_rates", "v_room_unavailable"];

    private const int MaxLength = 2000;

    // Whole words only, so columns like created_at and updated_at aren't blocked.
    private static readonly Regex BlockedKeywords = new(
        @"\b(DROP|DELETE|UPDATE|INSERT|ALTER|TRUNCATE|CREATE|EXEC|EXECUTE|GRANT|REVOKE)\b|\bINTO\s+OUTFILE\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex StartsWithSelect = new(@"^SELECT\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Beyond the required list, outside string literals: other ways a SELECT can write
    // files, stall the server, lock rows, read session and connection details, or reach
    // a table without FROM (TABLE t, system schemas). Backquotes are refused so names
    // can't be disguised.
    private static readonly Regex BlockedOutsideLiterals = new(
        @"\b(INTO|LOAD_FILE|SLEEP|BENCHMARK|FOR\s+SHARE|LOCK\s+IN|TABLE|HANDLER|PROCEDURE)\b" +
        @"|\b(USER|CURRENT_USER|SESSION_USER|SYSTEM_USER|DATABASE|SCHEMA|VERSION|CONNECTION_ID|\w*LOCK\w*)\s*\(" +
        @"|\b(information_schema|performance_schema|mysql|sys)\b" +
        @"|[@`]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Base tables. The views never expose these names as columns, so any mention is
    // an attempt to reach a table directly.
    private static readonly Regex BaseTables = new(
        @"\b(guest|booking|booking_night|booking_message|payment|hotel|room|room_type|seasonal_rate|amenity|" +
        @"room_type_amenity|service_request|maintenance_block|event_block|event_block_room|knowledge_document)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // What follows each FROM or JOIN: an opening parenthesis (subquery) or a name,
    // optionally mis321-qualified.
    private static readonly Regex TableReference = new(
        @"\b(?:FROM|JOIN)\s+(?:(?<sub>\()|(?:mis321\s*\.\s*)?(?<name>[A-Za-z_][A-Za-z0-9_$]*)(?<qualified>\s*\.)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Comma joins (FROM a, b) would slip a second name past TableReference; use JOIN.
    private static readonly Regex CommaJoin = new(
        @"\bFROM\s+(?:mis321\s*\.\s*)?[A-Za-z_][A-Za-z0-9_$]*(?:\s+(?:AS\s+)?[A-Za-z_][A-Za-z0-9_$]*)?\s*,",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        if (BaseTables.IsMatch(structure)) return "base table name";
        if (CommaJoin.IsMatch(structure)) return "comma join";

        var references = TableReference.Matches(structure);
        if (references.Count == 0) return "no FROM";
        foreach (Match reference in references)
        {
            if (reference.Groups["sub"].Success) continue;
            if (reference.Groups["qualified"].Success) return "other schema";
            if (!ApprovedViews.Contains(reference.Groups["name"].Value, StringComparer.OrdinalIgnoreCase)) return "not an approved view";
        }

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
