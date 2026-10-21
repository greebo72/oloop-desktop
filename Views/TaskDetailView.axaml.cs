using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using OloopApp.ViewModels;

namespace OloopApp.Views;

public partial class TaskDetailView : UserControl
{
    private TaskDetailViewModel? _subscribed;

    public TaskDetailView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_subscribed is not null)
            _subscribed.PropertyChanged -= OnDetailPropertyChanged;

        _subscribed = DataContext as TaskDetailViewModel;

        if (_subscribed is not null)
            _subscribed.PropertyChanged += OnDetailPropertyChanged;
    }

    /// <summary>
    /// When the edit mode starts, the cursor goes into the title field, so the
    /// user can type right away.
    /// </summary>
    private void OnDetailPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TaskDetailViewModel.IsEditing))
            return;

        if (_subscribed is not { IsEditing: true })
            return;

        Dispatcher.UIThread.Post(() =>
        {
            var box = this.FindControl<TextBox>("TitleBox");
            if (box is null)
                return;

            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Background);
    }
}
