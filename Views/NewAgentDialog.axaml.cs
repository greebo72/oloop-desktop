using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// Makes an agent: the form, then the result that names the credentials file. Returns true when an
/// agent was made.
/// </summary>
public partial class NewAgentDialog : Window
{
    public NewAgentDialog(NewAgentViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        DataContext = viewModel;
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with a view model.</summary>
    public NewAgentDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, NewAgentViewModel viewModel)
    {
        await new NewAgentDialog(viewModel).ShowDialog<bool>(owner);
        return viewModel.IsDone;
    }

    private async void OnCreateClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NewAgentViewModel vm)
            await vm.CreateAsync();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnDoneClick(object? sender, RoutedEventArgs e) => Close(true);
}
