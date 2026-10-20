using System.Text;

namespace OloopApp.Services;

/// <summary>
/// A minimal, dependency-free YAML-subset parser/writer covering exactly the
/// constructs the oloop frontmatter uses (see schemas/*.schema.json and the
/// conformance fixtures): scalars (null, int, quoted/unquoted string, date-time
/// strings), inline lists (<c>[a, b]</c>), block lists (<c>- item</c>), and
/// block lists of flat objects (comments).
///
/// This is intentionally NOT a general YAML parser. It follows the rules the
/// Python reference emits (python-frontmatter) so round-trips are stable.
/// </summary>
public static partial class OloopYaml
{
    private const string FrontmatterDelim = "---";

    // ---------------- Splitting ----------------

    /// <summary>
    /// Splits raw file text into (frontmatter, description). Throws
    /// <see cref="OloopDataException"/> with a "no frontmatter" message when the
    /// document does not start with a frontmatter block.
    /// </summary>
    public static (string? Frontmatter, string Description) Split(string raw)
    {
        raw = NormalizeNewlines(raw);
        if (raw.Length == 0)
            throw new OloopDataException("no frontmatter: empty file");

        var lines = raw.Split('\n');
        // Skip a leading BOM if present.
        if (lines[0].StartsWith("﻿"))
            lines[0] = lines[0][1..];

        if (!lines[0].TrimEnd().Equals(FrontmatterDelim, StringComparison.Ordinal))
            throw new OloopDataException("no frontmatter: file does not start with '---'");

        // Find the closing delimiter.
        int end = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd() == FrontmatterDelim)
            {
                end = i;
                break;
            }
        }
        if (end < 0)
            throw new OloopDataException("no frontmatter: missing closing '---'");

        var frontmatter = string.Join('\n', lines[1..end]);
        var body = string.Join('\n', lines[(end + 1)..]);
        return (frontmatter, TrimLeadingNewline(body));
    }

    /// <summary>Joins a parsed map + description back into a full file.</summary>
    public static string Render(FrontmatterDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append(FrontmatterDelim).Append('\n');
        sb.Append(doc.Yaml);
        if (!doc.Yaml.EndsWith('\n'))
            sb.Append('\n');
        sb.Append(FrontmatterDelim).Append('\n');
        if (!string.IsNullOrEmpty(doc.Description))
            sb.Append(doc.Description);
        return sb.ToString();
    }

    // ---------------- Document model ----------------

    /// <summary>The parsed result of a frontmatter block: ordered key/value map.</summary>
    public sealed class FrontmatterDocument
    {
        public List<KeyValuePair<string, object?>> Fields { get; } = new();
        public string Description { get; set; } = "";

        public string Yaml => string.Join('\n', Fields.Select(FormatField));

        public object? this[string key]
        {
            get
            {
                var kv = Fields.FirstOrDefault(f => f.Key == key);
                return kv.Key != "" ? kv.Value : null;
            }
        }

        public bool Has(string key) => Fields.Any(f => f.Key == key);

        public string? GetString(string key) => this[key] as string;
        public int? GetInt(string key) => this[key] is int i ? i : null;
        public string? GetNullableString(string key) => this[key] as string;
        public IReadOnlyList<string>? GetStringList(string key) => this[key] as List<string>;
        public IReadOnlyList<FrontmatterDocument>? GetObjectList(string key) =>
            this[key] as List<FrontmatterDocument>;
    }

    // ---------------- Parse ----------------

    /// <summary>Parses a frontmatter block into an ordered document.</summary>
    public static FrontmatterDocument Parse(string yaml)
    {
        var doc = new FrontmatterDocument();
        var lines = NormalizeNewlines(yaml).Split('\n');
        int i = 0;

        while (i < lines.Length)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
            {
                i++;
                continue;
            }

            // Top-level keys are column 0.
            var (key, rest) = SplitKeyValue(line);
            if (key is null)
                throw new OloopDataException($"YAML parse error at: {line}");

            // Empty value => start of a block list / nested structure.
            if (string.IsNullOrWhiteSpace(rest))
            {
                // Collect the indented block that belongs to this key.
                var block = new List<string>();
                int j = i + 1;
                while (j < lines.Length)
                {
                    string bl = lines[j];
                    if (string.IsNullOrWhiteSpace(bl)) { block.Add(bl); j++; continue; }
                    if (!IsIndented(bl)) break;
                    block.Add(bl);
                    j++;
                }
                doc.Fields.Add(new KeyValuePair<string, object?>(key, ParseBlock(block, key)));
                i = j;
                continue;
            }

            doc.Fields.Add(new KeyValuePair<string, object?>(key, ParseScalar(rest, key)));
            i++;
        }

        return doc;
    }

    private static object? ParseBlock(List<string> block, string ownerKey)
    {
        // Determine if it is a list of scalars, a list of objects, or something
        // else (which we refuse).
        var content = block.Where(b => !string.IsNullOrWhiteSpace(b)).ToList();
        if (content.Count == 0)
            return new List<string>(); // empty list

        bool allListItems = content.All(b =>
            b.TrimStart().StartsWith("- ") || b.TrimEnd() == "-");

        if (allListItems)
        {
            // List of objects: first item after "- " itself contains "key: value"
            // and the item is followed by sibling keys at deeper indent.
            if (IsListOfObjects(block, content))
                return ParseObjectList(block, ownerKey);
            return ParseScalarList(block, ownerKey);
        }

        throw new OloopDataException(
            $"unsupported YAML structure for key '{ownerKey}'");
    }

    private static bool IsListOfObjects(List<string> block, List<string> content)
    {
        // A list-of-objects looks like:
        //   - body: First comment
        //     createdAt: ...
        // i.e. the item after "- " contains a "key: value", and the following
        // line is indented deeper than the dash and is itself "key: value".
        for (int idx = 0; idx < content.Count; idx++)
        {
            var (firstKey, firstRest) = StripDash(content[idx]);
            if (firstKey is null) return false;
            if (string.IsNullOrWhiteSpace(firstRest)) return false;

            // Need a continuation line for it to be a multi-key object.
            if (idx + 1 < content.Count && ContainsKeyValue(content[idx + 1]) &&
                IndentOf(content[idx + 1]) > IndentOf(content[idx]))
                return true;
        }
        return false;
    }

    private static List<FrontmatterDocument> ParseObjectList(List<string> block, string ownerKey)
    {
        var objects = new List<FrontmatterDocument>();
        var items = SplitObjectItems(block, ownerKey);
        foreach (var itemLines in items)
        {
            var doc = new FrontmatterDocument();
            foreach (var raw in itemLines)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string line = raw.TrimEnd();
                var (key, rest) = StripDash(line);
                if (key is null)
                    throw new OloopDataException($"YAML parse error in list for '{ownerKey}': {line}");
                doc.Fields.Add(new KeyValuePair<string, object?>(key, ParseScalar(rest, key)));
            }
            objects.Add(doc);
        }
        return objects;
    }

    /// <summary>Splits an object-list block into per-object line groups.</summary>
    private static List<List<string>> SplitObjectItems(List<string> block, string ownerKey)
    {
        var groups = new List<List<string>>();
        List<string>? current = null;
        foreach (var raw in block)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string line = raw.TrimEnd();
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("- "))
            {
                current = new List<string>();
                groups.Add(current);
                // Preserve the rest of the line after the dash as its own entry.
                var (key, rest) = StripDash(line);
                current.Add($"- {key}: {rest}".TrimEnd());
            }
            else
            {
                if (current is null)
                    throw new OloopDataException($"YAML parse error in list for '{ownerKey}': {line}");
                current.Add(line);
            }
        }
        return groups;
    }

    private static List<string> ParseScalarList(List<string> block, string ownerKey)
    {
        var items = new List<string>();
        foreach (var raw in block)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            string line = raw.TrimEnd();
            var (key, rest) = StripDash(line);
            if (key is null)
                throw new OloopDataException($"YAML parse error in list for '{ownerKey}': {line}");
            items.Add(ScalarToString(rest, ownerKey));
        }
        return items;
    }

    // ---------------- Scalar handling ----------------

    private static object? ParseScalar(string raw, string key)
    {
        string s = raw.Trim();

        // Explicit null (also the empty string, e.g. "key:" with nothing after).
        if (s.Length == 0 || s.Equals("null", StringComparison.OrdinalIgnoreCase) || s == "~")
            return null;

        // Inline list: [a, b]
        if (s.StartsWith("[") && s.EndsWith("]"))
        {
            string inner = s[1..^1].Trim();
            if (inner.Length == 0)
                return new List<string>();
            return inner.Split(',', StringSplitOptions.TrimEntries)
                        .Select(x => ScalarToString(x, key)).ToList();
        }

        // int
        if (int.TryParse(s, out int n) && LooksLikePlainInt(s))
            return n;

        return ScalarToString(s, key);
    }

    private static string ScalarToString(string raw, string key)
    {
        string s = raw.Trim();
        if (s.Length == 0) return "";
        if (s.Equals("null", StringComparison.OrdinalIgnoreCase) || s == "~") return "";

        if (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            return Unquote(s);

        return s;
    }

    private static string Unquote(string s)
    {
        char q = s[0];
        string inner = s[1..^1];
        if (q == '\'')
            return inner.Replace("''", "'");
        return UnescapeDouble(inner);
    }

    private static string UnescapeDouble(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[++i];
                sb.Append(n switch
                {
                    'n' => '\n',
                    't' => '\t',
                    '\\' => '\\',
                    '"' => '"',
                    _ => n
                });
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool LooksLikePlainInt(string s)
    {
        // A UUID or date contains '-' or ':' and must not be read as int. Only
        // a pure digit string (optionally with a leading sign) is an int.
        foreach (char c in s)
            if (!char.IsDigit(c) && c is not '+' and not '-') return false;
        return s.Length > 0 && char.IsDigit(s[^1]);
    }

    // ---------------- Formatting (write path) ----------------

    private static string FormatField(KeyValuePair<string, object?> field) =>
        field.Value switch
        {
            null => $"{field.Key}: null",
            int i => $"{field.Key}: {i}",
            List<string> list => FormatStringList(field.Key, list),
            List<FrontmatterDocument> objList => FormatObjectList(field.Key, objList),
            string s => $"{field.Key}: {FormatScalar(s)}",
            _ => $"{field.Key}: {field.Value!}"
        };

    private static string FormatStringList(string key, List<string> list)
    {
        if (list.Count == 0)
            return $"{key}: []";
        var lines = new List<string> { $"{key}:" };
        foreach (var item in list)
            lines.Add($"- {FormatScalar(item)}");
        return string.Join('\n', lines);
    }

    private static string FormatObjectList(string key, List<FrontmatterDocument> objList)
    {
        if (objList.Count == 0)
            return $"{key}: []";
        var lines = new List<string> { $"{key}:" };
        foreach (var obj in objList)
        {
            var first = true;
            foreach (var f in obj.Fields)
            {
                string prefix = first ? "- " : "  ";
                lines.Add($"{prefix}{f.Key}: {FormatScalar(ScalarToStringOf(f.Value))}");
                first = false;
            }
        }
        return string.Join('\n', lines);
    }

    private static string ScalarToStringOf(object? v) => v switch
    {
        null => "",
        int i => i.ToString(),
        string s => s,
        _ => v.ToString() ?? ""
    };

    private static string FormatScalar(string s)
    {
        if (s.Length == 0) return "null";
        // Quote when the value has characters that need it.
        bool needsQuotes = s.Any(c => c is ':' or '#' or '{' or '}' or '[' or ']' or ','
                                        or '&' or '*' or '!' or '|' or '>' or '"' or '\''
                                        or '%' or '@' or '`')
                            || s != s.Trim()
                            || s.StartsWith("- ")
                            || bool.TryParse(s, out bool b) && (s == "true" || s == "false");
        return needsQuotes ? "\"" + EscapeDouble(s) + "\"" : s;
    }

    private static string EscapeDouble(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");

    // ---------------- Small helpers ----------------

    private static string NormalizeNewlines(string s) =>
        s.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string TrimLeadingNewline(string s)
    {
        if (s.StartsWith('\n')) s = s[1..];
        return s;
    }

    private static (string? Key, string Rest) SplitKeyValue(string line)
    {
        int colon = line.IndexOf(':');
        if (colon <= 0) return (null, "");
        string key = line[..colon].Trim();
        string rest = line[(colon + 1)..].Trim();
        if (!key.All(c => char.IsLetterOrDigit(c) || c == '_')) return (null, "");
        return (key, rest);
    }

    private static (string? Key, string Rest) StripDash(string line)
    {
        string t = line.TrimStart();
        if (!t.StartsWith("- ")) return (null, "");
        t = t[2..];
        var (k, r) = SplitKeyValue(t);
        return (k, r);
    }

    private static bool IsIndented(string line) => line.StartsWith(' ') || line.StartsWith('\t');

    private static int IndentOf(string line)
    {
        int n = 0;
        foreach (char c in line)
        {
            if (c == ' ') n++;
            else break;
        }
        return n;
    }

    private static bool ContainsKeyValue(string line)
    {
        string t = line.TrimStart();
        int colon = t.IndexOf(':');
        return colon > 0;
    }
}
