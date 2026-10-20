using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// Shows the dialogs of the app.
///
/// The view models talk to the dialogs only through this interface, so the
/// windows stay in the view layer and the view models stay testable: a test
/// replaces the service with a fake and never opens a window.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the settings. Returns true when the user saved them, false when
    /// the dialog was cancelled.
    /// </summary>
    Task<bool> ShowSettingsAsync(SettingsViewModel viewModel);

    /// <summary>
    /// Shows the new project dialog. Returns the created project, or null
    /// when the user cancelled.
    /// </summary>
    Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel);

    /// <summary>
    /// Shows the new task dialog. Returns true when the user confirmed the
    /// input, false when the dialog was cancelled.
    /// </summary>
    Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel);

    /// <summary>
    /// Shows the edit task dialog. Returns true when the user confirmed the
    /// input, false when the dialog was cancelled.
    /// </summary>
    Task<bool> ShowEditTaskAsync(EditTaskViewModel viewModel);

    /// <summary>
    /// Shows the conflict dialog. Returns what the user chose.
    /// </summary>
    Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel);
}
