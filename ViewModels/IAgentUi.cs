namespace OloopApp.ViewModels;

/// <summary>
/// What the agent dialogs need from the window they run in: a second dialog, a question and the file
/// choosers. The window implements it, so a dialog opens on top of its own window and not of the one
/// behind it, and the view models stay testable.
/// </summary>
public interface IAgentUi
{
    /// <summary>Shows the dialog that makes an agent. True when an agent was made.</summary>
    Task<bool> ShowNewAgentAsync(NewAgentViewModel viewModel);

    /// <summary>Asks a question that is answered with the confirm button or by cancelling.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);

    /// <summary>The file chooser for a new file. Null when the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName);

    /// <summary>The file chooser for an existing file. Null when the user cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title);
}
