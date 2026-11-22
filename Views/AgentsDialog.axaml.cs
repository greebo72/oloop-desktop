using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// The agents of the signed-in user. It is the <see cref="IAgentUi"/> of its view model, so the
/// dialogs it opens (new agent, questions, file choosers) belong to this window and not to the one
/// behind it.
/// </summary>
public partial class AgentsDialog : Window, IAgentUi
{
    public AgentsDialog(AgentsViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        viewModel.Ui = this;
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync();
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with a view model.</summary>
    public AgentsDialog()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window owner, AgentsViewModel viewModel)
        => new AgentsDialog(viewModel).ShowDialog(owner);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    public Task<bool> ShowNewAgentAsync(NewAgentViewModel viewModel)
        => NewAgentDialog.ShowAsync(this, viewModel);

    public Task ShowGrantProjectsAsync(GrantProjectsViewModel viewModel)
        => GrantProjectsDialog.ShowAsync(this, viewModel);

    public async Task<bool> ConfirmAsync(string title, string message, string confirmText)
        => await new ConfirmDialog(title, message, confirmText).ShowDialog<bool>(this);

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
                DefaultExtension = "json",
                ShowOverwritePrompt = true,
                FileTypeChoices = [new FilePickerFileType("Credentials file") { Patterns = ["*.json"] }],
            });
            return file?.TryGetLocalPath();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    public async Task<string?> PickOpenFileAsync(string title)
    {
        var provider = StorageProvider;
        if (!provider.CanOpen)
            return null;

        try
        {
            var files = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Credentials file") { Patterns = ["*.json"] }],
            });
            return files.Count == 1 ? files[0].TryGetLocalPath() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }
}
