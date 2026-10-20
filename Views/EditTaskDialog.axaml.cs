using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Views;

/// <summary>
/// The dialog for editing an existing task. It only collects the input — the
/// shell reads the values off <see cref="ViewModel"/> and performs the write.
/// </summary>
public partial class EditTaskDialog : Window
{
    /// <summary>
    /// Creates the dialog for an existing task.
    /// </summary>
    /// <param name="viewModel">The view model that collects the input.</param>
    public EditTaskDialog(EditTaskViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// The constructor the XAML runtime loader needs. The dialog is always
    /// created with a view model, so this one only exists for the designer.
    /// </summary>
    public EditTaskDialog()
        : this(new EditTaskViewModel(new OloopTask
        {
            FormatVersion = 1,
            Id = Guid.Empty.ToString("D"),
            ProjectId = Guid.Empty.ToString("D"),
            Type = TaskType.todo,
            Title = "",
            Status = TaskStatus.open,
            CreatedAt = "",
            CreatedBy = "",
            UpdatedAt = "",
            UpdatedBy = "",
        }))
    {
    }

    /// <summary>The view model that collects the input of the dialog.</summary>
    public EditTaskViewModel ViewModel { get; }

    private void OnSaveClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
