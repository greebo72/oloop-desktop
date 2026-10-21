using Avalonia.Controls;
using OloopApp.Core.Models;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// Shows the dialogs of the app as windows.
///
/// This is the only place that knows about the windows: the view models talk
/// to <see cref="IDialogService"/> and never to a window themselves.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly Func<Window?> _owner;

    /// <summary>
    /// Creates the service. The owner is the window the dialogs are centred
    /// on; it is asked for every dialog, because the window can be replaced
    /// while the app runs.
    /// </summary>
    /// <param name="owner">Returns the window the dialogs belong to, or null.</param>
    public DialogService(Func<Window?> owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public async Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
    {
        var owner = _owner();
        if (owner is null)
            return false;

        return await SettingsDialog.ShowAsync(owner, viewModel);
    }

    /// <inheritdoc />
    public async Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
    {
        var owner = _owner();
        if (owner is null)
            return null;

        return await NewProjectDialog.ShowAsync(owner, viewModel);
    }

    /// <inheritdoc />
    public async Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel)
    {
        var owner = _owner();
        if (owner is null)
            return false;

        var dialog = new NewTaskDialog(viewModel);
        return await dialog.ShowDialog<bool>(owner);
    }

    /// <inheritdoc />
    public async Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
    {
        var owner = _owner();
        if (owner is null)
            return ConflictChoice.Cancel;

        var dialog = new ConflictDialog(viewModel);
        return await dialog.ShowDialog<ConflictChoice>(owner);
    }

    /// <inheritdoc />
    public async Task<bool> ShowDiscardChangesAsync(IReadOnlyList<string> taskTitles)
    {
        var owner = _owner();
        if (owner is null)
            return false;

        var dialog = new DiscardChangesDialog(taskTitles);
        return await dialog.ShowDialog<bool>(owner);
    }
}
