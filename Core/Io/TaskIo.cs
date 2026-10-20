using System.Globalization;
using System.Text.RegularExpressions;
using OloopApp.Core.Models;
using YamlDotNet.Core;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Core.Io;

/// <summary>
/// Reads and writes task files (<c>tasks/&lt;task-id&gt;.md</c>).
///
/// Writing uses the canonical key order of the spec with <c>formatVersion</c>
/// first; unknown keys are never written.
/// </summary>
public static class TaskIo
{
    /// <summary>The only <c>formatVersion</c> this client knows.</summary>
    public const int SupportedFormatVersion = 1;

    /// <summary>Canonical frontmatter key order of a task file.</summary>
    public static readonly string[] KeyOrder =
    [
        "formatVersion", "id", "projectId", "type", "title", "status",
        "imagePath", "labels", "createdAt", "createdBy", "updatedAt", "updatedBy",
        "comments",
    ];

    private static readonly Regex UuidPattern = new(
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly Regex TimestampPattern = new(
        @"^\d{4}-\d{2}-\d{2}[Tt]\d{2}:\d{2}:\d{2}(\.\d+)?([Zz]|[+-]\d{2}:\d{2})$",
        RegexOptions.Compiled);

    // ---------------- Reading ----------------

    /// <summary>
    /// Parses one task file. <paramref name="fileName"/> is the task file name
    /// with or without the <c>.md</c> suffix; it must equal the <c>id</c> in the
    /// frontmatter.
    /// </summary>
    public static OloopTask Read(string fileName, string content, string? expectedProjectId = null)
    {
        var stem = fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^3]
            : fileName;

        var parts = Frontmatter.Split(content);
        var map = YamlFrontmatter.Parse(parts.Yaml);
        var task = Decode(stem, map, parts.Body);

        if (expectedProjectId is not null && task.ProjectId != expectedProjectId)
            throw new OloopFormatException(
                $"projectId does not match the project of its folder");

        return task;
    }

    private static OloopTask Decode(string fileName, YamlNode.Mapping map, string body)
    {
        // formatVersion: absent means 1, a higher value is refused.
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

        // Unknown keys are not allowed (the schema sets additionalProperties: false).
        foreach (var (key, _) in map.Entries)
        {
            if (!KeyOrder.Contains(key))
                throw new OloopFormatException($"unknown key: {key}");
        }

        var id = ReadString(map, "id");
        if (id != fileName)
            throw new OloopFormatException($"file name {fileName} does not match id {id}");
        if (!UuidPattern.IsMatch(id))
            throw new OloopFormatException($"id is not a valid UUID: '{id}'");

        var projectId = ReadString(map, "projectId");
        if (!UuidPattern.IsMatch(projectId))
            throw new OloopFormatException($"projectId is not a valid UUID: '{projectId}'");

        var typeText = ReadString(map, "type");
        if (!TaskTypeExtensions.All.Any(t => t.ToString() == typeText))
            throw new OloopFormatException(
                $"type: must be one of {string.Join(", ", TaskTypeExtensions.All)}, got '{typeText}'");
        var type = Enum.Parse<TaskType>(typeText);

        var statusText = ReadString(map, "status");
        if (statusText is not ("open" or "closed"))
            throw new OloopFormatException($"status: must be one of open, closed, got '{statusText}'");
        var status = Enum.Parse<TaskStatus>(statusText);

        var title = ReadString(map, "title");
        if (title.Length == 0)
            throw new OloopFormatException("title: must have at least 1 character(s)");
        if (title.EnumerateRunes().Count() > 300)
            throw new OloopFormatException("title: must have at most 300 characters");

        var createdAt = ReadTimestamp(map, "createdAt");
        var createdBy = ReadString(map, "createdBy");
        var updatedAt = ReadTimestamp(map, "updatedAt");
        var updatedBy = ReadString(map, "updatedBy");

        // imagePath: null or a string.
        string? imagePath = null;
        if (map.Get("imagePath") is YamlNode.Scalar image)
        {
            if (!image.IsPlainNull)
                imagePath = image.Value;
        }
        else if (map.Has("imagePath"))
        {
            throw new OloopFormatException("key imagePath must be a string or null");
        }

        var labels = ReadLabels(map);
        var comments = ReadComments(map);

        return new OloopTask
        {
            FormatVersion = formatVersion,
            Id = id,
            ProjectId = projectId,
            Type = type,
            Title = title,
            Status = status,
            ImagePath = imagePath,
            Labels = labels,
            CreatedAt = createdAt,
            CreatedBy = createdBy,
            UpdatedAt = updatedAt,
            UpdatedBy = updatedBy,
            Comments = comments,
            Description = body,
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

    private static string ReadTimestamp(YamlNode.Mapping map, string key)
    {
        var value = ReadString(map, key);
        if (!TimestampPattern.IsMatch(value))
            throw new OloopFormatException($"{key}: not an RFC 3339 date-time: '{value}'");
        return value;
    }

    private static IReadOnlyList<string> ReadLabels(YamlNode.Mapping map)
    {
        if (!map.Has("labels"))
            throw new OloopFormatException("labels: required property missing");

        if (map.Get("labels") is not YamlNode.Sequence sequence)
            throw new OloopFormatException("labels: must be a list");

        var labels = new List<string>();
        foreach (var item in sequence.Items)
        {
            if (item is not YamlNode.Scalar scalar)
                throw new OloopFormatException("labels: items must be strings");
            var length = scalar.Value.EnumerateRunes().Count();
            if (length < 1 || length > 100)
                throw new OloopFormatException("labels: items must have 1 to 100 characters");
            labels.Add(scalar.Value);
        }

        if (labels.Distinct(StringComparer.Ordinal).Count() != labels.Count)
            throw new OloopFormatException("labels: items must be unique");

        return labels;
    }

    private static IReadOnlyList<TaskComment> ReadComments(YamlNode.Mapping map)
    {
        var comments = new List<TaskComment>();

        if (!map.Has("comments"))
            return comments;

        if (map.Get("comments") is not YamlNode.Sequence sequence)
            throw new OloopFormatException("comments: must be a list");

        foreach (var item in sequence.Items)
        {
            if (item is not YamlNode.Mapping commentMap)
                throw new OloopFormatException("comments: items must be mappings");

            foreach (var (key, _) in commentMap.Entries)
            {
                if (key is not ("body" or "createdAt"))
                    throw new OloopFormatException($"comments: unknown property '{key}'");
            }

            var body = ReadString(commentMap, "body");
            if (body.Length == 0)
                throw new OloopFormatException("comments: body must not be empty");
            var createdAt = ReadTimestamp(commentMap, "createdAt");

            comments.Add(new TaskComment { Body = body, CreatedAt = createdAt });
        }

        return comments;
    }

    // ---------------- Writing ----------------

    /// <summary>
    /// Serializes a task back to the file format. Timestamps are written
    /// verbatim — never re-derived — so a round-trip changes no value.
    /// </summary>
    public static string Write(OloopTask task)
    {
        var map = new YamlNode.Mapping(
        [
            new("formatVersion", new YamlNode.Raw(task.FormatVersion.ToString(CultureInfo.InvariantCulture))),
            new("id", new YamlNode.Scalar(task.Id, ScalarStyle.Plain)),
            new("projectId", new YamlNode.Scalar(task.ProjectId, ScalarStyle.Plain)),
            new("type", new YamlNode.Scalar(task.Type.ToString(), ScalarStyle.Plain)),
            new("title", new YamlNode.Scalar(task.Title, ScalarStyle.Plain)),
            new("status", new YamlNode.Scalar(task.Status.ToString(), ScalarStyle.Plain)),
            new("imagePath", task.ImagePath is null
                ? new YamlNode.Scalar("null", ScalarStyle.Plain)
                : new YamlNode.Scalar(task.ImagePath, ScalarStyle.Plain)),
            new("labels", new YamlNode.Sequence(
                task.Labels.Select(l => (YamlNode)new YamlNode.Scalar(l, ScalarStyle.Plain)).ToList())),
            new("createdAt", new YamlNode.Scalar(task.CreatedAt, ScalarStyle.Plain)),
            new("createdBy", new YamlNode.Scalar(task.CreatedBy, ScalarStyle.Plain)),
            new("updatedAt", new YamlNode.Scalar(task.UpdatedAt, ScalarStyle.Plain)),
            new("updatedBy", new YamlNode.Scalar(task.UpdatedBy, ScalarStyle.Plain)),
            new("comments", new YamlNode.Sequence(
                task.Comments.Select(c =>
                {
                    var commentMap = new YamlNode.Mapping(
                    [
                        new("body", new YamlNode.Scalar(c.Body, ScalarStyle.Plain)),
                        new("createdAt", new YamlNode.Scalar(c.CreatedAt, ScalarStyle.Plain)),
                    ]);
                    return (YamlNode)commentMap;
                }).ToList())),
        ]);

        var frontmatter = YamlFrontmatter.Render(map);
        var body = task.Description.Trim('\n');
        return body.Length == 0
            ? $"---\n{frontmatter}\n---\n"
            : $"---\n{frontmatter}\n---\n{body}\n";
    }
}
