using OloopApp.Core.Io;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// What the user chose in the conflict dialog.
/// </summary>
public enum ConflictChoice
{
    /// <summary>Discard the change and load the file from disk again.</summary>
    Reload,

    /// <summary>Write the in-memory version over the file on disk.</summary>
    Overwrite,

    /// <summary>Leave everything as it is.</summary>
    Cancel,
}

/// <summary>
/// Explains a write conflict to the user: the file on disk changed since it
/// was loaded, so the store refused the write.
///
/// The dialog never overwrites silently — the user picks between reloading
/// and overwriting explicitly.
/// </summary>
public partial class ConflictViewModel : ViewModelBase
{
    /// <summary>
    /// Creates the view model for a task whose write was refused.
    /// </summary>
    /// <param name="task">The task that was not written.</param>
    /// <param name="reason">The message of the conflict exception.</param>
    public ConflictViewModel(OloopTask task, string reason)
    {
        Task = task;
        Reason = reason ?? "";
    }

    /// <summary>
    /// Creates the view model for a task whose write was refused by
    /// <see cref="OloopConflictException"/>.
    /// </summary>
    /// <param name="task">The task that was not written.</param>
    /// <param name="conflict">The conflict the store raised.</param>
    public ConflictViewModel(OloopTask task, OloopConflictException conflict)
        : this(task, conflict?.Message ?? "")
    {
        CurrentUpdatedAt = conflict?.CurrentUpdatedAt;
    }

    /// <summary>The task that was not written.</summary>
    public OloopTask Task { get; }

    /// <summary>
    /// The explanation shown above the reason: what happened and what the
    /// user can do about it.
    /// </summary>
    public string Message =>
        "This task changed on disk since it was loaded. " +
        "Reload to take the version from disk, or overwrite to write your version over it.";

    /// <summary>The message of the conflict exception — the technical reason.</summary>
    public string Reason { get; }

    /// <summary>The title of the task that was not written.</summary>
    public string TaskTitle => Task.Title;

    /// <summary>The <c>updatedAt</c> currently on disk, when known.</summary>
    public string? CurrentUpdatedAt { get; }
}
