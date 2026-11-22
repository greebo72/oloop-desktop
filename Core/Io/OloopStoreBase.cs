using System.Globalization;
using OloopApp.Core.Models;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Core.Io;

/// <summary>
/// The rules of every write that do not depend on where the data lives:
/// what a new task looks like, what an edit may touch, how labels are
/// cleaned. An implementation only stores: it reads, writes one task with the
/// conflict check, and creates a project.
/// </summary>
public abstract class OloopStoreBase : IOloopStore
{
    /// <summary>The only <c>formatVersion</c> this client knows.</summary>
    public const int SupportedFormatVersion = 1;

    public abstract string? CheckAvailable();

    public abstract string Fingerprint();

    public abstract IChangeSource CreateChangeSource(TimeSpan debounce);

    public abstract Task<OloopSnapshot> LoadAsync(CancellationToken ct = default);

    public abstract ImageOpenResult OpenImage(OloopProject project, string? imagePath);

    public virtual Task<ImageOpenResult> OpenImageAsync(OloopProject project, OloopTask task, CancellationToken ct = default)
        => Task.FromResult(OpenImage(project, task.ImagePath));

    public abstract Task<OloopProject> CreateProjectAsync(
        string name,
        string? referencePath = null,
        string? referenceHost = null,
        CancellationToken ct = default);

    public abstract Task WriteTaskAsync(
        OloopProject project,
        OloopTask task,
        string? expectedUpdatedAt,
        CancellationToken ct = default);

    // ---------------- Mutations ----------------

    /// <summary>
    /// Creates a task: fresh UUID4, status open, imagePath null,
    /// createdAt = updatedAt = now, createdBy = updatedBy = actor.
    /// </summary>
    public async Task<OloopTask> CreateTaskAsync(
        OloopProject project,
        TaskType type,
        string title,
        string description,
        IEnumerable<string>? labels,
        string actor,
        CancellationToken ct = default,
        string? dueDate = null)
    {
        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length == 0)
            throw new OloopFormatException("title must not be empty");

        var now = NowUtc();
        var task = new OloopTask
        {
            FormatVersion = SupportedFormatVersion,
            Id = Guid.NewGuid().ToString("D"),
            ProjectId = project.Id,
            Type = type,
            Title = trimmedTitle,
            Status = TaskStatus.open,
            ImagePath = null,
            DueDate = dueDate,
            Labels = NormalizeLabels(labels),
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor,
            Comments = [],
            Description = description ?? "",
            ProjectName = project.Name,
        };

        await WriteTaskAsync(project, task, expectedUpdatedAt: null, ct).ConfigureAwait(false);
        return task;
    }

    /// <summary>
    /// Edits type, title, description, labels and the due date. Never touches
    /// id, createdAt, createdBy or the file name.
    ///
    /// The due date is only changed when <paramref name="setDueDate"/> is set:
    /// then null removes it, otherwise the given date is written.
    /// </summary>
    public async Task<OloopTask> EditTaskAsync(
        OloopTask task,
        OloopProject project,
        string? title,
        string? description,
        IEnumerable<string>? labels,
        string actor,
        CancellationToken ct = default,
        bool setDueDate = false,
        string? dueDate = null,
        TaskType? type = null)
    {
        var updated = task.With(
            type: type,
            title: title is null ? null : title.Trim(),
            description: description,
            labels: labels is null ? null : NormalizeLabels(labels),
            updatedAt: NowUtc(),
            updatedBy: actor,
            setDueDate: setDueDate,
            dueDate: dueDate);

        await WriteTaskAsync(project, updated, task.UpdatedAt, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Flips the status open ↔ closed. Closing is the only completion
    /// mechanism — there is no delete anywhere.
    /// </summary>
    public async Task<OloopTask> SetStatusAsync(
        OloopTask task,
        OloopProject project,
        bool closed,
        string actor,
        CancellationToken ct = default)
    {
        var updated = task.With(
            status: closed ? TaskStatus.closed : TaskStatus.open,
            updatedAt: NowUtc(),
            updatedBy: actor);

        await WriteTaskAsync(project, updated, task.UpdatedAt, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>Appends a comment (append-only, no edits, no deletes).</summary>
    public async Task<OloopTask> AddCommentAsync(
        OloopTask task,
        OloopProject project,
        string body,
        string actor,
        CancellationToken ct = default)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
            throw new OloopFormatException("comment body must not be empty");

        var comments = task.Comments.ToList();
        comments.Add(new TaskComment { Body = trimmed, CreatedAt = NowUtc() });

        var updated = task.With(
            comments: comments,
            updatedAt: NowUtc(),
            updatedBy: actor);

        await WriteTaskAsync(project, updated, task.UpdatedAt, ct).ConfigureAwait(false);
        return updated;
    }

    // ---------------- Helpers ----------------

    /// <summary>The current time as ISO 8601 UTC with a trailing Z.</summary>
    public static string NowUtc()
        => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Trims labels, drops empty ones and removes duplicates (first wins).
    /// </summary>
    public static IReadOnlyList<string> NormalizeLabels(IEnumerable<string>? labels)
    {
        if (labels is null)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var label in labels)
        {
            var trimmed = label.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
                continue;
            result.Add(trimmed);
        }
        return result;
    }
}
