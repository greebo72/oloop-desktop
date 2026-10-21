using System.Globalization;
using System.Text.RegularExpressions;
using OloopApp.Core.Models;
using YamlDotNet.Core;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Core.Io;

/// <summary>
/// Reads and writes project files (<c>&lt;slug&gt;/project.md</c>).
/// </summary>
public static class ProjectIo
{
    /// <summary>The only <c>formatVersion</c> this client knows.</summary>
    public const int SupportedFormatVersion = 1;

    /// <summary>Canonical frontmatter key order of a project file.</summary>
    public static readonly string[] KeyOrder =
    [
        "formatVersion", "id", "name", "createdAt", "referencePath", "referenceHost",
    ];

    private static readonly Regex UuidPattern = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly Regex TimestampPattern = new(
        @"^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})$",
        RegexOptions.Compiled);

    /// <summary>
    /// Parses one project file.
    /// </summary>
    /// <exception cref="OloopFormatException">The file violates the format.</exception>
    public static OloopProject Read(string content)
    {
        var parts = Frontmatter.Split(content);
        var map = YamlFrontmatter.Parse(parts.Yaml);

        int formatVersion = 1;
        if (map.Get("formatVersion") is YamlNode.Scalar versionScalar)
        {
            if (!int.TryParse(versionScalar.Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out formatVersion))
                throw new OloopFormatException("formatVersion must be an integer");
        }
        else if (map.Has("formatVersion"))
        {
            throw new OloopFormatException("formatVersion must be an integer");
        }

        if (formatVersion > SupportedFormatVersion)
            throw new OloopFormatException(
                $"formatVersion {formatVersion} is newer than supported ({SupportedFormatVersion})");

        // Keys this client does not know are kept and written back unchanged.
        var extraFields = map.Entries
            .Where(e => !KeyOrder.Contains(e.Key))
            .ToList();

        var id = ReadString(map, "id");
        if (!UuidPattern.IsMatch(id))
            throw new OloopFormatException($"id is not a valid UUID: '{id}'");

        var name = ReadString(map, "name");
        if (name.Length == 0)
            throw new OloopFormatException("name: must have at least 1 character(s)");
        if (name.EnumerateRunes().Count() > 200)
            throw new OloopFormatException("name: must have at most 200 characters");

        var createdAt = ReadTimestamp(map, "createdAt");

        string? referencePath = ReadOptionalString(map, "referencePath");
        string? referenceHost = ReadOptionalString(map, "referenceHost");

        return new OloopProject
        {
            FormatVersion = formatVersion,
            Id = id,
            Name = name,
            CreatedAt = createdAt,
            ReferencePath = referencePath,
            ReferenceHost = referenceHost,
            ExtraFields = extraFields,
        };
    }

    private static string ReadString(YamlNode.Mapping map, string key)
    {
        if (!map.Has(key))
            throw new OloopFormatException($"{key}: required property missing");

        if (map.Get(key) is not YamlNode.Scalar scalar)
            throw new OloopFormatException($"{key}: must be a string");
        if (scalar.IsPlainNull)
            throw new OloopFormatException($"{key}: must be a string");

        return scalar.Value;
    }

    private static string? ReadOptionalString(YamlNode.Mapping map, string key)
    {
        if (!map.Has(key))
            return null;

        if (map.Get(key) is not YamlNode.Scalar scalar)
            throw new OloopFormatException($"{key}: must be a string");
        if (scalar.IsPlainNull)
            return null;

        return scalar.Value;
    }

    private static string ReadTimestamp(YamlNode.Mapping map, string key)
    {
        var value = ReadString(map, key);
        if (!TimestampPattern.IsMatch(value))
            throw new OloopFormatException($"{key}: not an RFC 3339 date-time: '{value}'");
        return value;
    }

    /// <summary>
    /// Serializes a project back to the file format.
    ///
    /// The optional reference fields are omitted when they are not set: the
    /// schema types them as a string, not as a nullable one, so a
    /// <c>referencePath: null</c> would be rejected by the reference clients.
    /// </summary>
    public static string Write(OloopProject project)
    {
        var entries = new List<KeyValuePair<string, YamlNode>>
        {
            new("formatVersion", new YamlNode.Raw(project.FormatVersion.ToString(CultureInfo.InvariantCulture))),
            new("id", new YamlNode.Scalar(project.Id, ScalarStyle.Plain)),
            new("name", new YamlNode.Scalar(project.Name, ScalarStyle.Plain)),
            new("createdAt", new YamlNode.Scalar(project.CreatedAt, ScalarStyle.Plain)),
        };

        if (!string.IsNullOrWhiteSpace(project.ReferencePath))
            entries.Add(new("referencePath", new YamlNode.Scalar(project.ReferencePath, ScalarStyle.Plain)));

        if (!string.IsNullOrWhiteSpace(project.ReferenceHost))
            entries.Add(new("referenceHost", new YamlNode.Scalar(project.ReferenceHost, ScalarStyle.Plain)));

        entries.AddRange(project.ExtraFields);

        return $"---\n{YamlFrontmatter.Render(new YamlNode.Mapping(entries))}\n---\n";
    }

    /// <summary>
    /// The folder name of a project: a slug of its name — lowercase, only
    /// letters, digits and dashes (SPEC.md, "Project folders").
    /// </summary>
    public static string Slugify(string name)
    {
        var slug = Regex.Replace(name.Trim().ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        return slug.Length == 0 ? "project" : slug;
    }
}
