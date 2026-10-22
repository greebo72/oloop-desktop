using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.Core.Models;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// The dialog for a new task. It only collects the input — the shell reads
/// the values off <see cref="ViewModel"/> and performs the write.
/// </summary>
public partial class NewTaskDialog : Window
{
    /// <summary>
    /// Creates the dialog for a new task.
    /// </summary>
    /// <param name="viewModel">The view model that collects the input.</param>
    public NewTaskDialog(NewTaskViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        ViewModel = viewModel;
        DataContext = viewModel;
    }

    /// <summary>
    /// The constructor the XAML runtime loader needs. The dialog is always
    /// created with a view model, so this one only exists for the designer.
    /// </summary>
    public NewTaskDialog()
        : this(new NewTaskViewModel(new OloopProject
        {
            FormatVersion = 1,
            Id = Guid.Empty.ToString("D"),
            Name = "",
            CreatedAt = "",
        }))
    {
    }

    /// <summary>The view model that collects the input of the dialog.</summary>
    public NewTaskViewModel ViewModel { get; }

    private void OnCreateClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
