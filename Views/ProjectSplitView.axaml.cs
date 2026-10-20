using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.ViewModels;

namespace OloopApp.Views;

public partial class ProjectSplitView : UserControl
{
    public ProjectSplitView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the new task dialog for this project. The shell owns the write,
    /// the view only forwards the click.
    /// </summary>
    private async void OnNewTaskClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProjectSplitViewModel split)
            await split.Shell.OpenNewTaskAsync(split.Project);
    }
}
