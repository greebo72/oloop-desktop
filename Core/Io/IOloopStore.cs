using OloopApp.Core.Models;

namespace OloopApp.Core.Io;

/// <summary>
/// The result of opening the image of a task: either the bytes of the image
/// or the reason why there is none.
/// </summary>
public sealed record ImageOpenResult(Stream? Stream, string? Error);

/// <summary>
/// Access to one oloop data set: reads it as a snapshot and performs every
/// write. The folder of Markdown files is one implementation
/// (<see cref="FileOloopStore"/>); the rules of the writes live in
/// <see cref="OloopStoreBase"/> and are the same for every implementation.
///
/// Failures are reported as <see cref="IOException"/>,
/// <see cref="UnauthorizedAccessException"/>, <see cref="OloopFormatException"/>
/// and <see cref="OloopConflictException"/>; the view models handle exactly
/// these.
/// </summary>
public interface IOloopStore
{
    /// <summary>
    /// Why the data cannot be read right now, for example because the folder
    /// is gone. Null when the store is usable.
    /// </summary>
    string? CheckAvailable();

    /// <summary>
    /// A cheap fingerprint of everything the app shows. Two equal
    /// fingerprints mean that a reload would show the same.
    /// </summary>
    string Fingerprint();

    /// <summary>
    /// Starts to listen for changes behind the back of the app. Throws like
    /// the file system watcher does when that is not possible.
    /// </summary>
    /// <param name="debounce">The quiet time after the last change.</param>
    IChangeSource CreateChangeSource(TimeSpan debounce);

    /// <summary>Reads everything. Unreadable items do not abort the read.</summary>
    Task<OloopSnapshot> LoadAsync(CancellationToken ct = default);

    /// <summary>Opens the image of a task. The path is the <c>imagePath</c> of the task.</summary>
    ImageOpenResult OpenImage(OloopProject project, string? imagePath);

    /// <summary>
    /// The image of a task. A store that has to fetch it over a network does it here without
    /// blocking the caller; the others answer at once. The task is given and not only its path:
    /// an image on a server is bound to its task by the encryption.
    /// </summary>
    Task<ImageOpenResult> OpenImageAsync(OloopProject project, OloopTask task, CancellationToken ct = default)
        => Task.FromResult(OpenImage(project, task.ImagePath));

    Task<OloopProject> CreateProjectAsync(
        string name,
        string? referencePath = null,
        string? referenceHost = null,
        CancellationToken ct = default);

    Task<OloopTask> CreateTaskAsync(
        OloopProject project,
        TaskType type,
        string title,
        string description,
        IEnumerable<string>? labels,
        string actor,
        CancellationToken ct = default,
        string? dueDate = null);

    Task<OloopTask> EditTaskAsync(
        OloopTask task,
        OloopProject project,
        string? title,
        string? description,
        IEnumerable<string>? labels,
        string actor,
        CancellationToken ct = default,
        bool setDueDate = false,
        string? dueDate = null,
        TaskType? type = null);

    Task<OloopTask> SetStatusAsync(
        OloopTask task,
        OloopProject project,
        bool closed,
        string actor,
        CancellationToken ct = default);

    Task<OloopTask> AddCommentAsync(
        OloopTask task,
        OloopProject project,
        string body,
        string actor,
        CancellationToken ct = default);

    /// <summary>
    /// Writes a task. Refuses with <see cref="OloopConflictException"/> when
    /// the stored task has another <c>updatedAt</c> than
    /// <paramref name="expectedUpdatedAt"/>; null means a new task.
    /// </summary>
    Task WriteTaskAsync(
        OloopProject project,
        OloopTask task,
        string? expectedUpdatedAt,
        CancellationToken ct = default);
}
