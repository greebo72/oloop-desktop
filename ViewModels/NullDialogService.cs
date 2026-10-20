using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// The dialog service that is used before the real one is set: it never shows
/// a window and reports "cancelled" for every dialog.
///
/// It is the default of <see cref="MainViewModel.Dialogs"/>, so a view model
/// that is created without a window — in a test, or before the app has a
/// window — does not crash when a dialog is requested.
/// </summary>
public sealed class NullDialogService : IDialogService
{
    /// <summary>The shared instance.</summary>
    public static NullDialogService Instance { get; } = new();

    private NullDialogService()
    {
    }

    /// <inheritdoc />
    public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
        => Task.FromResult<OloopProject?>(null);

    /// <inheritdoc />
    public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<bool> ShowEditTaskAsync(EditTaskViewModel viewModel) => Task.FromResult(false);

    /// <inheritdoc />
    public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
        => Task.FromResult(ConflictChoice.Cancel);
}
