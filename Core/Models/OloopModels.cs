using OloopApp.Core.Io;

namespace OloopApp.Core.Models;

/// <summary>
/// The task types of the oloop format. The closed label is display only —
/// the data model stays open/closed (SPEC.md, "One status field, no enum per type").
/// </summary>
public enum TaskType
{
    todo,
    question,
    decision,
    feature,
    bug,
}

public static class TaskTypeExtensions
{
    /// <summary>Label shown when a task of this type is closed (SPEC.md).</summary>
    public static string ClosedLabel(this TaskType type) => type switch
    {
        TaskType.todo => "Done",
        TaskType.question => "Answered",
        TaskType.decision => "Decided",
        TaskType.feature => "Implemented",
        TaskType.bug => "Fixed",
        _ => "Closed",
    };

    /// <summary>Label shown when a task of this type is open.</summary>
    public static string OpenLabel(this TaskType type) => "Open";

    /// <summary>Capitalized name for group headers.</summary>
    public static string DisplayName(this TaskType type) => type switch
    {
        TaskType.todo => "Todo",
        TaskType.question => "Question",
        TaskType.decision => "Decision",
        TaskType.feature => "Feature",
        TaskType.bug => "Bug",
        _ => type.ToString(),
    };

    /// <summary>The order used by the type grouping (SPEC: todo, question, decision, feature, bug).</summary>
    public static IReadOnlyList<TaskType> All { get; } =
    [
        TaskType.todo, TaskType.question, TaskType.decision, TaskType.feature, TaskType.bug,
    ];
}

/// <summary>The only two statuses the data model knows.</summary>
public enum TaskStatus
{
    open,
    closed,
}

/// <summary>A comment. Append-only, never edited, never deleted.</summary>
public sealed class TaskComment
{
    public required string Body { get; init; }
    public required string CreatedAt { get; init; }

    /// <summary>
    /// Fields of the comment that this client does not know, in the order of
    /// the file. They are written back unchanged (SPEC.md, "Unknown fields").
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, YamlNode>> ExtraFields { get; init; } = [];
}

/// <summary>
/// A task as stored in <c>tasks/&lt;task-id&gt;.md</c>. Timestamps stay strings
/// (ISO 8601 UTC) so a round-trip changes no value.
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

    /// <summary>
    /// The calendar date the task should be done by, <c>YYYY-MM-DD</c> without
    /// a time zone. Null when the task has no due date: the field is absent or
    /// null in the file.
    /// </summary>
    public string? DueDate { get; init; }

    public IReadOnlyList<string> Labels { get; init; } = [];
    public required string CreatedAt { get; init; }
    public required string CreatedBy { get; init; }
    public required string UpdatedAt { get; init; }
    public required string UpdatedBy { get; init; }
    public IReadOnlyList<TaskComment> Comments { get; init; } = [];
    /// <summary>The Markdown body of the file.</summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Frontmatter fields that this client does not know, in the order of the
    /// file. They are written back unchanged, so a client that is older than
    /// the file never erases what a newer one added (SPEC.md, "Unknown fields").
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, YamlNode>> ExtraFields { get; init; } = [];

    /// <summary>Name of the owning project. Filled in by the store, not stored in the file.</summary>
    public string ProjectName { get; set; } = "";

    public bool IsClosed => Status == TaskStatus.closed;

    public OloopTask With(
        string? title = null,
        string? description = null,
        IReadOnlyList<string>? labels = null,
        TaskStatus? status = null,
        string? updatedAt = null,
        string? updatedBy = null,
        IReadOnlyList<TaskComment>? comments = null)
    {
        return new OloopTask
        {
            FormatVersion = FormatVersion,
            Id = Id,
            ProjectId = ProjectId,
            Type = Type,
            Title = title ?? Title,
            Status = status ?? Status,
            ImagePath = ImagePath,
            DueDate = DueDate,
            Labels = labels ?? Labels,
            CreatedAt = CreatedAt,
            CreatedBy = CreatedBy,
            UpdatedAt = updatedAt ?? UpdatedAt,
            UpdatedBy = updatedBy ?? UpdatedBy,
            Comments = comments ?? Comments,
            Description = description ?? Description,
            ExtraFields = ExtraFields,
            ProjectName = ProjectName,
        };
    }
}

/// <summary>A project as stored in <c>&lt;slug&gt;/project.md</c>.</summary>
public sealed class OloopProject
{
    public required int FormatVersion { get; init; }
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string CreatedAt { get; init; }
    public string? ReferencePath { get; init; }
    public string? ReferenceHost { get; init; }

    /// <summary>
    /// Frontmatter fields that this client does not know, in the order of the
    /// file. They are written back unchanged.
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, YamlNode>> ExtraFields { get; init; } = [];

    /// <summary>Folder name (slug) under the data root. Not stored in the file.</summary>
    public string FolderName { get; set; } = "";

    /// <summary>Statistics filled in by the store.</summary>
    public int TaskCount { get; set; }
    public int OpenTaskCount { get; set; }
    /// <summary>Most recent <c>updatedAt</c> across the tasks of this project, or null.</summary>
    public string? LatestUpdatedAt { get; set; }
}

/// <summary>A file that could not be read. Shown in the UI, never written to.</summary>
public sealed record UnreadableFile(string Path, string Reason);

/// <summary>The whole data root as read at one point in time.</summary>
public sealed class OloopSnapshot
{
    public required IReadOnlyList<OloopProject> Projects { get; init; }
    public required IReadOnlyList<OloopTask> Tasks { get; init; }
    public required IReadOnlyList<UnreadableFile> Unreadable { get; init; }

    public static OloopSnapshot Empty { get; } = new()
    {
        Projects = [],
        Tasks = [],
        Unreadable = [],
    };
}
