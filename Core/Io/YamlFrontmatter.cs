using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace OloopApp.Core.Io;

/// <summary>
/// A parsed YAML node. Deliberately tiny: the oloop frontmatter only uses
/// scalars, sequences of scalars and sequences of flat mappings.
/// </summary>
public abstract class YamlNode
{
    /// <summary>A scalar: text plus the style it was written in.</summary>
    public sealed class Scalar(string value, ScalarStyle style) : YamlNode
    {
        public string Value { get; } = value;
        public ScalarStyle Style { get; } = style;

        /// <summary>
        /// True for a plain <c>null</c> or <c>~</c>. A quoted "null" is a value,
        /// not a null (the style decides, not the text).
        /// </summary>
        public bool IsPlainNull =>
            Style == ScalarStyle.Plain && (Value == "null" || Value == "~");
    }

    /// <summary>A sequence of nodes.</summary>
    public sealed class Sequence(List<YamlNode> items) : YamlNode
    {
        public IReadOnlyList<YamlNode> Items { get; } = items;
    }

    /// <summary>
    /// A scalar written verbatim, without quoting. Only used for values whose
    /// type is known (the integer <c>formatVersion</c>).
    /// </summary>
    public sealed class Raw(string value) : YamlNode
    {
        public string Value { get; } = value;
    }

    /// <summary>A mapping of key to node.</summary>
    public sealed class Mapping(List<KeyValuePair<string, YamlNode>> entries) : YamlNode
    {
        public IReadOnlyList<KeyValuePair<string, YamlNode>> Entries { get; } = entries;

        public YamlNode? Get(string key)
        {
            foreach (var entry in Entries)
                if (entry.Key == key)
                    return entry.Value;
            return null;
        }

        public bool Has(string key) => Get(key) is not null;
    }
}

/// <summary>
/// Reads and writes the YAML frontmatter of oloop data files.
///
/// Reading uses YamlDotNet's low-level event parser on purpose: the object
/// model would turn <c>2026-01-02T10:00:00Z</c> into a <c>DateTime</c>, and the
/// spec requires timestamps to stay strings so a round-trip changes no value.
/// </summary>
public static class YamlFrontmatter
{
    /// <summary>
    /// Parses a YAML mapping into a node tree.
    /// </summary>
    /// <exception cref="OloopFormatException">The YAML is invalid or not a mapping.</exception>
    public static YamlNode.Mapping Parse(string yaml)
    {
        var parser = new Parser(new StringReader(yaml));

        try
        {
            // StreamStart, DocumentStart, then the content.
            if (!parser.MoveNext())
                throw new OloopFormatException("frontmatter is empty");
            if (!parser.MoveNext())
                throw new OloopFormatException("frontmatter is empty");
            if (!parser.MoveNext())
                throw new OloopFormatException("frontmatter is empty");

            if (parser.Current is not MappingStart)
                throw new OloopFormatException("frontmatter is not a mapping");

            return ReadMapping(parser);
        }
        catch (OloopFormatException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new OloopFormatException($"invalid YAML: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a mapping. The parser is positioned on its MappingStart.</summary>
    private static YamlNode.Mapping ReadMapping(Parser parser)
    {
        var entries = new List<KeyValuePair<string, YamlNode>>();

        while (parser.MoveNext())
        {
            if (parser.Current is MappingEnd)
                break;

            if (parser.Current is not Scalar key)
                throw new OloopFormatException("frontmatter key is not a string");

            if (!parser.MoveNext())
                throw new OloopFormatException($"frontmatter key '{key.Value}' has no value");

            entries.Add(new KeyValuePair<string, YamlNode>(key.Value, ReadValue(parser)));
        }

        return new YamlNode.Mapping(entries);
    }

    /// <summary>Reads a sequence. The parser is positioned on its SequenceStart.</summary>
    private static YamlNode.Sequence ReadSequence(Parser parser)
    {
        var items = new List<YamlNode>();

        while (parser.MoveNext())
        {
            if (parser.Current is SequenceEnd)
                break;

            items.Add(ReadValue(parser));
        }

        return new YamlNode.Sequence(items);
    }

    /// <summary>Reads the value the parser is currently positioned on.</summary>
    private static YamlNode ReadValue(Parser parser) => parser.Current switch
    {
        Scalar s => new YamlNode.Scalar(s.Value, s.Style),
        SequenceStart => ReadSequence(parser),
        MappingStart => ReadMapping(parser),
        _ => throw new OloopFormatException("unsupported YAML value in frontmatter"),
    };

    // ---------------- Writing ----------------

    /// <summary>
    /// Renders a frontmatter mapping. Keys keep the order of the mapping, so
    /// the caller controls the canonical key order of the spec.
    /// </summary>
    public static string Render(YamlNode.Mapping mapping)
    {
        var lines = new List<string>();
        foreach (var (key, value) in mapping.Entries)
            RenderEntry(lines, key, value, indent: 0);
        return string.Join('\n', lines);
    }

    private static void RenderEntry(List<string> lines, string key, YamlNode value, int indent)
    {
        string pad = new(' ', indent);

        switch (value)
        {
            case YamlNode.Scalar { IsPlainNull: true }:
                lines.Add($"{pad}{key}: null");
                break;

            case YamlNode.Raw raw:
                lines.Add($"{pad}{key}: {raw.Value}");
                break;

            case YamlNode.Scalar scalar:
                lines.Add($"{pad}{key}: {Quote(scalar.Value)}");
                break;

            case YamlNode.Sequence { Items.Count: 0 }:
                lines.Add($"{pad}{key}: []");
                break;

            case YamlNode.Sequence sequence:
                lines.Add($"{pad}{key}:");
                foreach (var item in sequence.Items)
                {
                    switch (item)
                    {
                        case YamlNode.Scalar scalar:
                            lines.Add($"{pad}- {Quote(scalar.Value)}");
                            break;
                        case YamlNode.Mapping mapping:
                        {
                            // A flat mapping: first key on the dash line, the rest indented.
                            bool first = true;
                            foreach (var (k, v) in mapping.Entries)
                            {
                                string prefix = first ? $"{pad}- " : $"{pad}  ";
                                lines.Add($"{prefix}{k}: {RenderInline(v)}");
                                first = false;
                            }
                            break;
                        }
                        default:
                            throw new OloopFormatException($"unsupported YAML value for key '{key}'");
                    }
                }
                break;

            case YamlNode.Mapping mapping:
                lines.Add($"{pad}{key}:");
                foreach (var (k, v) in mapping.Entries)
                    RenderEntry(lines, k, v, indent + 2);
                break;
        }
    }

    private static string RenderInline(YamlNode value) => value switch
    {
        YamlNode.Scalar { IsPlainNull: true } => "null",
        YamlNode.Raw raw => raw.Value,
        YamlNode.Scalar scalar => Quote(scalar.Value),
        _ => throw new OloopFormatException("unsupported YAML value"),
    };

    /// <summary>
    /// Quotes a scalar so no value is ever read back as a number, a date or a
    /// null. Plain scalars that are safe stay unquoted, matching the reference
    /// clients (the Kotlin client always double-quotes).
    /// </summary>
    public static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        bool needsQuotes = value != value.Trim()
            || value.Contains(": ")
            || value.EndsWith(':')
            || value.Contains(" #")
            || value.StartsWith("- ")
            || value.StartsWith('#')
            || "null".Equals(value, StringComparison.OrdinalIgnoreCase)
            || value == "~"
            || value is "true" or "false" or "True" or "False"
            || "yes".Equals(value, StringComparison.OrdinalIgnoreCase)
            || "no".Equals(value, StringComparison.OrdinalIgnoreCase)
            || value.Any(c => c is ':' or '{' or '}' or '[' or ']' or ',' or '&' or '*'
                                  or '!' or '|' or '>' or '%' or '@' or '`' or '"' or '\'')
            || double.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _)
            || long.TryParse(value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _);

        if (!needsQuotes)
            return value;

        var sb = new System.Text.StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (char c in value)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ' || c == '\u007f')
                        sb.Append($"\\u{(int)c:x4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
