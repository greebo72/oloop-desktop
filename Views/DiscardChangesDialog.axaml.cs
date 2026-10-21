using Avalonia.Controls;
using Avalonia.Interactivity;

namespace OloopApp.Views;

/// <summary>
/// The dialog that asks whether input that is not written yet may be thrown
/// away, for example because the window is closing.
///
/// The safe answer is the primary one: "Keep editing". The dialog returns
/// true only when the user picks "Discard" explicitly.
/// </summary>
public partial class DiscardChangesDialog : Window
{
    /// <summary>Creates the dialog for the tasks with unsaved input.</summary>
    /// <param name="taskTitles">The titles of the tasks concerned.</param>
    public DiscardChangesDialog(IReadOnlyList<string> taskTitles)
    {
        InitializeComponent();

        MessageText.Text = taskTitles.Count == 1
            ? "This task has input that is not saved. If you continue, it is lost."
            : $"{taskTitles.Count} tasks have input that is not saved. If you continue, it is lost.";

        TaskList.ItemsSource = taskTitles;
    }

    /// <summary>
    /// The constructor the XAML runtime loader needs. The dialog is always
    /// created with the titles, so this one only exists for the designer.
    /// </summary>
    public DiscardChangesDialog()
        : this([])
    {
    }

    private void OnDiscardClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnKeepClick(object? sender, RoutedEventArgs e) => Close(false);
}
