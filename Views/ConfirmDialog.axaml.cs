using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OloopApp.Views;

/// <summary>
/// A question for something that cannot be undone. The safe answer is the primary button, cancelling;
/// the dialog returns true only when the user picks the named action.
/// </summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string confirmText)
    {
        InitializeComponent();
        UiScale.Attach(this);
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with its texts.</summary>
    public ConfirmDialog()
        : this("", "", "OK")
    {
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
