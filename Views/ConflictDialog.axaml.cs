using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// The dialog that asks the user what to do when a write was refused because
/// the file on disk changed in the meantime.
///
/// The dialog never overwrites silently: the user picks "Reload" (take the
/// version from disk) or "Overwrite" (write the in-memory version over it)
/// explicitly. "Cancel" leaves everything as it is.
/// </summary>
public partial class ConflictDialog : Window
{
    /// <summary>
    /// Creates the dialog for a refused write.
    /// </summary>
    /// <param name="viewModel">The view model that explains the conflict.</param>
    public ConflictDialog(ConflictViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// The constructor the XAML runtime loader needs. The dialog is always
    /// created with a view model, so this one only exists for the designer.
    /// </summary>
    public ConflictDialog()
        : this(new ConflictViewModel(
            new Core.Models.OloopTask
            {
                FormatVersion = 1,
                Id = Guid.Empty.ToString("D"),
                ProjectId = Guid.Empty.ToString("D"),
                Type = Core.Models.TaskType.todo,
                Title = "",
                Status = Core.Models.TaskStatus.open,
                CreatedAt = "",
                CreatedBy = "",
                UpdatedAt = "",
                UpdatedBy = "",
            },
            ""))
    {
    }

    /// <summary>The view model that explains the conflict.</summary>
    public ConflictViewModel ViewModel { get; }

    private void OnReloadClick(object? sender, RoutedEventArgs e) => Close(ConflictChoice.Reload);

    private void OnOverwriteClick(object? sender, RoutedEventArgs e) => Close(ConflictChoice.Overwrite);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(ConflictChoice.Cancel);
}
