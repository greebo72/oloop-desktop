using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// The settings dialog. It is self-contained: the view model is passed in,
/// the result is a plain bool — true when the settings were saved.
/// </summary>
public partial class SettingsDialog : Window
{
    /// <summary>
    /// Creates the dialog for the given settings.
    /// </summary>
    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        DataContext = viewModel;
    }

    /// <summary>
    /// Opens the tab of the actor and puts the cursor into the field when the
    /// name is missing. The user is asked for the name then, so the field is
    /// where the typing has to go.
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (DataContext is not SettingsViewModel vm || !vm.HasNoActor)
            return;

        Tabs.SelectedItem = TasksTab;

        // The tab is only part of the window once it is selected and laid out.
        Dispatcher.UIThread.Post(() => ActorBox.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Opens the dialog and waits for it. Returns true when the user saved
    /// the settings.
    /// </summary>
    public static async Task<bool> ShowAsync(Window owner, SettingsViewModel viewModel)
    {
        var dialog = new SettingsDialog(viewModel);
        return await dialog.ShowDialog<bool>(owner);
    }

    /// <summary>
    /// Opens the agents of the signed-in user on top of this dialog. The view model is asked for here
    /// and not earlier, so it belongs to the session as it is now (keys open or not).
    /// </summary>
    private async void OnManageAgentsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.CreateAgentsViewModel() is { } agents)
            await AgentsDialog.ShowAsync(this, agents);
    }

    /// <summary>
    /// Opens the folder picker and puts the chosen folder into the data root.
    /// A platform without a storage provider (the headless tests) simply does
    /// nothing.
    /// </summary>
    private async void OnBrowseDataRootClick(object? sender, RoutedEventArgs e)
    {
        var provider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (provider is null || !provider.CanPickFolder)
            return;

        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Choose the oloop data folder",
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

        if (DataContext is SettingsViewModel vm)
            vm.DataRoot = path;
    }

    /// <summary>Saves the settings and closes the dialog with true.</summary>
    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            Close(false);
            return;
        }

        vm.Save();
        if (vm.Error is not null)
            return;

        Close(true);
    }

    /// <summary>Closes the dialog without saving.</summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
