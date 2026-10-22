using Avalonia.Controls;
using Avalonia.Input;
using OloopApp.ViewModels;

namespace OloopApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UiScale.Attach(this);

        // The title of the window carries the version, so it is visible
        // without opening anything.
        Title = AppVersion.WindowTitle;
    }

    private bool _closeConfirmed;

    /// <summary>
    /// Asks before the window closes while a task holds input that is not
    /// written (an edit that changed something, a typed comment). The window
    /// stays open until the user picks "Discard".
    /// </summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);

        if (_closeConfirmed || e.Cancel)
            return;

        if (DataContext is not MainViewModel { HasUnsavedChanges: true } shell)
            return;

        e.Cancel = true;
        _ = ConfirmCloseAsync(shell);
    }

    private async Task ConfirmCloseAsync(MainViewModel shell)
    {
        var discard = await shell.Dialogs.ShowDiscardChangesAsync(shell.UnsavedTaskTitles);
        if (!discard)
            return;

        _closeConfirmed = true;
        Close();
    }

    /// <summary>
    /// The keyboard shortcuts of the shell:
    ///
    /// <list type="bullet">
    /// <item>Ctrl+F — focus the search field.</item>
    /// <item>Ctrl+N — new task in the open project.</item>
    /// <item>Ctrl+E — edit the selected task in place.</item>
    /// <item>Ctrl+, — the settings.</item>
    /// <item>Escape — clear the search, or go back to the dashboard.</item>
    /// <item>F5 — reload the data root.</item>
    /// </list>
    ///
    /// The shortcuts that would type something (Ctrl+N, Ctrl+E, Ctrl+,) only
    /// fire when the focus is not in a text field, so the usual typing in the
    /// search box and in the comment field is not hijacked. Ctrl+F and F5
    /// work everywhere.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (DataContext is not MainViewModel shell)
            return;

        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        // Escape: in the search field it clears the search, elsewhere it
        // leaves the open project. A text field that is not the search field
        // owns the key — the comment field keeps its text.
        if (e.Key == Key.Escape)
        {
            if (IsTextInputFocused())
            {
                if (ClearSearch())
                    e.Handled = true;

                return;
            }

            if (shell.SearchText.Length > 0)
            {
                shell.ClearSearchCommand.Execute(null);
                e.Handled = true;
                return;
            }

            if (shell.HasSubView)
            {
                shell.BackToDashboardCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        // These two do not type anything, so they work in a text field too.
        if (ctrl && e.Key == Key.F)
        {
            FocusSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5)
        {
            shell.RefreshCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // A text field owns every other key — the shortcuts would break typing.
        if (IsTextInputFocused())
            return;

        if (ctrl && e.Key == Key.N)
        {
            if (shell.Split is not null)
                shell.NewTaskCommand.Execute(null);

            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.E)
        {
            shell.EditTaskCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.OemComma)
        {
            shell.SettingsCommand.Execute(null);
            e.Handled = true;
            return;
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

    /// <summary>
    /// Clears the search when it has text and the focus is in the search
    /// field. Returns whether it did something.
    /// </summary>
    private bool ClearSearch()
    {
        if (DataContext is not MainViewModel shell)
            return false;

        if (string.IsNullOrEmpty(shell.SearchText))
            return false;

        // Only the search field is cleared — a comment field keeps its text.
        if (FocusManager?.GetFocusedElement() is not TextBox box)
            return false;

        if (box.Name != "SearchBox")
            return false;

        shell.ClearSearchCommand.Execute(null);
        return true;
    }

    /// <summary>
    /// Whether the focused element is a text field, where the keyboard belongs
    /// to the text and not to the shell.
    /// </summary>
    private bool IsTextInputFocused()
        => FocusManager?.GetFocusedElement() is TextBox or AutoCompleteBox;
}
