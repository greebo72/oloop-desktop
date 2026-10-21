using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.ViewModels;

namespace OloopApp.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the new project dialog. The shell owns the write, the view only
    /// forwards the click.
    /// </summary>
    private async void OnNewProjectClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is { } shell)
            await shell.OpenNewProjectAsync();
    }

    /// <summary>
    /// Opens the settings. It is the way out of the empty state when no data
    /// folder is set.
    /// </summary>
    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (Shell is { } shell)
            await shell.OpenSettingsAsync();
    }

    /// <summary>
    /// The shell of the app. The dashboard is bound to the dashboard view
    /// model, so the shell is reached through the window.
    /// </summary>
    private MainViewModel? Shell =>
        TopLevel.GetTopLevel(this) is Window window
            ? window.DataContext as MainViewModel
            : null;
}
