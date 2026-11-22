using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// The import of a folder into the server. It is the <see cref="IImportUi"/> of its view model, so the
/// choosers belong to this window. Closing the window while it writes stops the import after the
/// current task.
/// </summary>
public partial class ImportFolderDialog : Window, IImportUi
{
    public ImportFolderDialog(ImportFolderViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        viewModel.Ui = this;
        DataContext = viewModel;
        Closing += (_, _) => viewModel.CancelRun();
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with a view model.</summary>
    public ImportFolderDialog()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window owner, ImportFolderViewModel viewModel)
        => new ImportFolderDialog(viewModel).ShowDialog(owner);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    public async Task<string?> PickFolderAsync(string title)
    {
        var provider = StorageProvider;
        if (!provider.CanPickFolder)
            return null;

        try
        {
            var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
            });
            return folders.Count == 1 ? folders[0].TryGetLocalPath() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        var provider = StorageProvider;
        if (!provider.CanSave)
            return null;

        try
        {
            var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = title,
                SuggestedFileName = suggestedName,
                DefaultExtension = "txt",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("Text file") { Patterns = ["*.txt"] }],
            });
            return file?.TryGetLocalPath();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
