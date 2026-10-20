using System.Text.Json.Serialization;

namespace OloopApp.Models;

/// <summary>Task type. Mirrors the `type` enum in task.schema.json.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskType
{
    todo,
    question,
    decision,
    feature,
    bug
}

/// <summary>Task status. The data model only knows open/closed (see SPEC.md).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskStatus
{
    open,
    closed
}

/// <summary>A single comment entry (append-only, no edits).</summary>
public sealed class TaskComment
{
    public required string Body { get; init; }
    public required string CreatedAt { get; init; }
}

/// <summary>
/// A task as read from `tasks/&lt;task-id&gt;.md`. Timestamps are kept as strings
/// verbatim so round-trips are lossless; parse for display/sorting with
/// <c>DateTimeOffset.Parse</c>.
/// </summary>
public sealed class OloopTask
{
    public required int FormatVersion { get; init; }
    public required string Id { get; init; }
    public required string ProjectId { get; init; }
    public required TaskType Type { get; init; }
    public required string Title { get; init; }
    public required TaskStatus Status { get; init; }
    public string? ImagePath { get; init; }
    public IReadOnlyList<string> Labels { get; init; } = Array.Empty<string>();
    public required string CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedBy { get; init; }
    public IReadOnlyList<TaskComment> Comments { get; init; } = Array.Empty<TaskComment>();
    public string Description { get; init; } = "";
    /// <summary>Project name, populated by the reader for display (not a file field).</summary>
    public string? ProjectName { get; set; }
}

/// <summary>A project as read from `&lt;slug&gt;/project.md`.</summary>
public sealed class OloopProject
{
    public required int FormatVersion { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CreatedAt { get; init; }
    public string? ReferencePath { get; init; }
    public string? ReferenceHost { get; init; }
    /// <summary>Markdown body of project.md (usually empty).</summary>
    public string Description { get; init; } = "";
    /// <summary>Folder name (slug) under the data root.</summary>
    public string? FolderName { get; set; }
    /// <summary>Stats filled in by the reader.</summary>
    public int TaskCount { get; set; }
    public int OpenTaskCount { get; set; }
}
