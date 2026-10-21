namespace OloopApp.ViewModels;

/// <summary>
/// What became of a write that the user started from the detail pane.
/// </summary>
public enum WriteOutcome
{
    /// <summary>The change is on disk.</summary>
    Saved,

    /// <summary>
    /// The user chose "Reload" in the conflict dialog: the change was thrown
    /// away on purpose and the task shows what is on disk.
    /// </summary>
    Discarded,

    /// <summary>Nothing was written. The change is still in the pane.</summary>
    NotSaved,
}
