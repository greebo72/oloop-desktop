using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OloopApp.Core.Models;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// The "new project" dialog. It is self-contained: the view model is passed
/// in, the result is the created project or null.
/// </summary>
public partial class NewProjectDialog : Window
{
    /// <summary>
    /// Creates the dialog for the given view model.
    /// </summary>
    public NewProjectDialog(NewProjectViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        DataContext = viewModel;
    }

    /// <summary>
    /// Opens the dialog and waits for it. Returns the created project or null
    /// when the user cancelled.
    /// </summary>
    public static async Task<OloopProject?> ShowAsync(Window owner, NewProjectViewModel viewModel)
    {
        var dialog = new NewProjectDialog(viewModel);
        return await dialog.ShowDialog<OloopProject?>(owner);
    }

    /// <summary>
    /// Opens the folder picker and puts the chosen folder into the reference
    /// path. A platform without a storage provider (the headless tests) simply
    /// does nothing.
    /// </summary>
    private async void OnBrowseReferencePathClick(object? sender, RoutedEventArgs e)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null || !provider.CanPickFolder)
            return;

        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the repository folder",
                AllowMultiple = false,
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                       or NotSupportedException)
        {
            return;
        }

        var folder = folders.FirstOrDefault();
        var path = folder?.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
            return;

        if (DataContext is NewProjectViewModel vm)
            vm.ReferencePath = path;
    }

    /// <summary>Creates the project and closes the dialog with it.</summary>
    private async void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NewProjectViewModel vm)
        {
            Close(null);
            return;
        }

        var project = await vm.SaveAsync();
        if (project is null)
            return;

        Close(project);
    }

    /// <summary>Closes the dialog without creating anything.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
