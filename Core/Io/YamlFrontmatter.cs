using System.Text.RegularExpressions;
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
        /// True for a plain scalar that was read from a file. It is written
        /// back exactly as it was read: whether <c>42</c> is a number, a string
        /// or something else is not up to this client when it does not know
        /// the field. A scalar that a writer builds is quoted when needed.
        /// </summary>
        public bool Verbatim { get; init; }

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
    /// An explicit null value. It is distinct from a scalar whose text is
    /// "null": that one is a string and has to be quoted.
    /// </summary>
    public sealed class NullNode : YamlNode
    {
        public static NullNode Instance { get; } = new();
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
        Scalar s => new YamlNode.Scalar(s.Value, s.Style) { Verbatim = s.Style == ScalarStyle.Plain },
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
        string name = RenderKey(key);

        switch (value)
        {
            case YamlNode.NullNode:
                lines.Add($"{pad}{name}: null");
                break;

            case YamlNode.Raw raw:
                lines.Add($"{pad}{name}: {raw.Value}");
                break;

            case YamlNode.Scalar scalar:
                lines.Add($"{pad}{name}: {RenderScalar(scalar)}");
                break;

            case YamlNode.Sequence { Items.Count: 0 }:
                lines.Add($"{pad}{name}: []");
                break;

            case YamlNode.Sequence sequence:
                lines.Add($"{pad}{name}:");
                foreach (var item in sequence.Items)
                    RenderItem(lines, item, indent);
                break;

            case YamlNode.Mapping { Entries.Count: 0 }:
                lines.Add($"{pad}{name}: {{}}");
                break;

            case YamlNode.Mapping mapping:
                lines.Add($"{pad}{name}:");
                foreach (var (k, v) in mapping.Entries)
                    RenderEntry(lines, k, v, indent + 2);
                break;
        }
    }

    /// <summary>
    /// Renders one item of a sequence. A mapping starts on the dash line, so
    /// a list of flat mappings looks the way it always did:
    /// <c>- body: ...</c> followed by the other keys, indented.
    /// </summary>
    private static void RenderItem(List<string> lines, YamlNode item, int indent)
    {
        string pad = new(' ', indent);

        switch (item)
        {
            case YamlNode.NullNode:
                lines.Add($"{pad}- null");
                break;

            case YamlNode.Raw raw:
                lines.Add($"{pad}- {raw.Value}");
                break;

            case YamlNode.Scalar scalar:
                lines.Add($"{pad}- {RenderScalar(scalar)}");
                break;

            case YamlNode.Sequence { Items.Count: 0 }:
                lines.Add($"{pad}- []");
                break;

            case YamlNode.Sequence sequence:
                lines.Add($"{pad}-");
                foreach (var inner in sequence.Items)
                    RenderItem(lines, inner, indent + 2);
                break;

            case YamlNode.Mapping { Entries.Count: 0 }:
                lines.Add($"{pad}- {{}}");
                break;

            case YamlNode.Mapping mapping:
            {
                var inner = new List<string>();
                foreach (var (k, v) in mapping.Entries)
                    RenderEntry(inner, k, v, indent + 2);

                // The first line moves up to the dash.
                inner[0] = $"{pad}- {inner[0][(indent + 2)..]}";
                lines.AddRange(inner);
                break;
            }
        }
    }

    /// <summary>
    /// A scalar as it goes into the file: a plain scalar that was read stays
    /// as it was, every other one is quoted when it would not survive.
    /// </summary>
    private static string RenderScalar(YamlNode.Scalar scalar)
        => scalar.Verbatim ? scalar.Value : Quote(scalar.Value);

    /// <summary>
    /// A key as it goes into the file. The keys of the format are plain words;
    /// the key of a field this client does not know is quoted when it has to be.
    /// </summary>
    private static string RenderKey(string key) => Quote(key);

    /// <summary>
    /// Quotes a scalar so no value is ever read back as a number, a date or a
    /// null. Plain scalars that are safe stay unquoted, matching the reference
    /// clients (the Kotlin client always double-quotes).
    ///
    /// The set of "unsafe" values is the one of YAML 1.1, because that is what
    /// the reference clients parse with (PyYAML resolves <c>on</c>, <c>off</c>,
    /// <c>yes</c> and <c>no</c> as booleans). A title like "on" would otherwise
    /// be read back as <c>true</c> and break the format.
    ///
    /// A value is also quoted when a plain scalar would not survive the
    /// parser: a line break or a tab ends the scalar, and an indicator at the
    /// start (<c>#</c>, <c>&gt;</c>, <c>*</c>, …) starts a different node.
    /// </summary>
    public static string Quote(string value)
    {
        if (value.Length == 0)
            return "\"\"";

        bool needsQuotes = value != value.Trim()
            || value.Contains(": ")
            || value.EndsWith(':')
            || value.Contains(" #")
            || StartsWithIndicator(value)
            || IsYaml11Bool(value)
            || IsYaml11Null(value)
            || IsYaml11Number(value)
            || IsYaml11Timestamp(value)
            || value.Any(c => c is '\n' or '\r' or '\t' || char.IsControl(c));

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

    /// <summary>
    /// Whether the value starts with a character that would turn a plain
    /// scalar into a different node: a comment, an anchor, a tag, a block
    /// scalar, a flow collection or a reserved character.
    ///
    /// A dash, a question mark and a colon only count when they are followed
    /// by a space or stand alone — otherwise they are part of the text
    /// (<c>-x</c>, <c>http://host</c>).
    /// </summary>
    private static bool StartsWithIndicator(string value)
    {
        char first = value[0];

        if (first is '#' or '&' or '*' or '!' or '|' or '>' or '%' or '@' or '`'
            or '"' or '\'' or '{' or '}' or '[' or ']' or ',')
        {
            return true;
        }

        if (first is '-' or '?' or ':')
        {
            return value.Length == 1 || value[1] == ' ';
        }

        return false;
    }

    // ---------------- YAML 1.1 ----------------
    //
    // The reference clients parse the frontmatter with YAML 1.1 semantics
    // (PyYAML, SnakeYAML). A plain scalar that YAML 1.1 resolves to another
    // type has to be quoted, otherwise the value changes when the file is
    // read back — the exact thing the spec forbids.
    //
    // The patterns below are the implicit resolvers of YAML 1.1, taken
    // verbatim from PyYAML (yaml/resolver.py). They are not an
    // approximation: a value the resolver does not match stays a string, and
    // a value it does match has to be quoted.

    /// <summary>
    /// The boolean resolver: yes/no, true/false and on/off, in the three
    /// capitalizations the resolver accepts.
    /// </summary>
    private static readonly Regex Yaml11BoolPattern = new(
        @"^(?:yes|Yes|YES|no|No|NO|true|True|TRUE|false|False|FALSE|on|On|ON|off|Off|OFF)\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>The null resolver: <c>~</c> and the three spellings of null.</summary>
    private static readonly Regex Yaml11NullPattern = new(
        @"^(?:~|null|Null|NULL)\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The integer resolver: plain decimals, the binary, octal and
    /// hexadecimal notations, the underscore separators and the sexagesimal
    /// notation.
    /// </summary>
    private static readonly Regex Yaml11IntPattern = new(
        @"^(?:[-+]?0b[0-1_]+|[-+]?0[0-7_]+|[-+]?(?:0|[1-9][0-9_]*)|[-+]?0x[0-9a-fA-F_]+|[-+]?[1-9][0-9_]*(?::[0-5]?[0-9])+)\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The float resolver: decimals, the exponent notation (with an explicit
    /// sign), the sexagesimal notation with a fraction, infinity and NaN.
    /// </summary>
    private static readonly Regex Yaml11FloatPattern = new(
        @"^(?:[-+]?(?:[0-9][0-9_]*)\.[0-9_]*(?:[eE][-+][0-9]+)?|\.[0-9][0-9_]*(?:[eE][-+][0-9]+)?|[-+]?[0-9][0-9_]*(?::[0-5]?[0-9])+\.[0-9_]*|[-+]?\.(?:inf|Inf|INF)|\.(?:nan|NaN|NAN))\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The timestamp resolver: a bare date and a date with a time. The
    /// reference clients keep timestamps as strings, but a client with a
    /// different resolver would not — so a date-like value is quoted anyway.
    /// </summary>
    private static readonly Regex Yaml11TimestampPattern = new(
        @"^(?:[0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{4}-[0-9]{1,2}-[0-9]{1,2}(?:[Tt]|[ \t]+)[0-9]{1,2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]*)?(?:[ \t]*(?:Z|[-+][0-9]{1,2}(?::[0-9]{2})?))?)\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Whether YAML 1.1 resolves the value to a boolean.</summary>
    private static bool IsYaml11Bool(string value) => Yaml11BoolPattern.IsMatch(value);

    /// <summary>Whether YAML 1.1 resolves the value to null.</summary>
    private static bool IsYaml11Null(string value) => Yaml11NullPattern.IsMatch(value);

    /// <summary>Whether YAML 1.1 resolves the value to a number.</summary>
    private static bool IsYaml11Number(string value)
        => Yaml11IntPattern.IsMatch(value) || Yaml11FloatPattern.IsMatch(value);

    /// <summary>Whether YAML 1.1 resolves the value to a timestamp.</summary>
    private static bool IsYaml11Timestamp(string value) => Yaml11TimestampPattern.IsMatch(value);
}
