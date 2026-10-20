using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using OloopApp.ViewModels;
using OloopApp.Views;

namespace OloopApp;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var main = new MainViewModel();
            var window = new MainWindow
            {
                DataContext = main,
            };

            desktop.MainWindow = window;

            // The dialogs are shown as windows of the main window, so the
            // service needs the window — and the window is only there now.
            main.Dialogs = new DialogService(() => desktop.MainWindow);

            // Load the data root in the background so the window shows up right away.
            _ = main.RefreshAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
