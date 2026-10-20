using Avalonia.Controls;
using Avalonia.Input;
using OloopApp.ViewModels;

namespace OloopApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The keyboard shortcuts of the shell: Ctrl+F focuses the search, Ctrl+N
    /// opens the new task dialog and F5 reloads the data root.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not MainViewModel shell)
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl && e.Key == Key.F)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.N)
        {
            if (shell.Split is not null)
                _ = shell.OpenNewTaskAsync();

            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            _ = shell.RefreshAsync();
            e.Handled = true;
        }
    }

    /// <summary>Puts the cursor into the search field of the top bar.</summary>
    private void FocusSearch()
    {
        var box = this.FindControl<TextBox>("SearchBox");
        if (box is null)
            return;

        box.Focus();
    }
}
