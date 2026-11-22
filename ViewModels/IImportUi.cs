namespace OloopApp.ViewModels;

/// <summary>
/// What the import dialog needs from its window: the choosers of a folder and of a file. The window
/// implements it, so the view model stays testable.
/// </summary>
public interface IImportUi
{
    /// <summary>The chooser for the folder to import. Null when the user cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>The chooser for the text file of the plan or the report. Null when the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName);
}
