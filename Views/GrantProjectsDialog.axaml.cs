using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// Gives an existing agent more projects. It reads which projects the agent has when it opens, and stays
/// open after a partial result so that the projects that failed can be tried again.
/// </summary>
public partial class GrantProjectsDialog : Window
{
    public GrantProjectsDialog(GrantProjectsViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        DataContext = viewModel;
        Opened += async (_, _) => await viewModel.LoadAsync();
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with a view model.</summary>
    public GrantProjectsDialog()
    {
        InitializeComponent();
    }

    public static Task ShowAsync(Window owner, GrantProjectsViewModel viewModel)
        => new GrantProjectsDialog(viewModel).ShowDialog(owner);

    private async void OnGrantClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GrantProjectsViewModel vm)
            await vm.GrantAsync();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
