using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// One bucket of the "silent since" view: a header and the rows of the
/// bucket.
/// </summary>
public partial class SilenceGroupViewModel : ViewModelBase
{
    /// <summary>Creates a bucket of the view.</summary>
    public SilenceGroupViewModel(
        string title,
        IEnumerable<OloopTask> tasks,
        SilentTasksViewModel? owner = null)
    {
        Title = title;

        foreach (var task in tasks)
        {
            var project = owner?.ProjectOf(task) ?? FallbackProject(task);
            Rows.Add(new SilentTaskRowViewModel(task, project));
        }

        if (owner is not null)
        {
            SelectCommand = new RelayCommand<SilentTaskRowViewModel>(owner.SelectRow);
            EditCommand = new AsyncRelayCommand<SilentTaskRowViewModel>(owner.EditRowAsync);
        }
    }

    /// <summary>The header of the bucket.</summary>
    public string Title { get; }

    /// <summary>The rows of the bucket.</summary>
    public ObservableCollection<SilentTaskRowViewModel> Rows { get; } = [];

    /// <summary>Shows the detail of a row. Null when the group stands alone.</summary>
    public IRelayCommand<SilentTaskRowViewModel>? SelectCommand { get; }

    /// <summary>Opens the edit dialog of a row. Null when the group stands alone.</summary>
    public IAsyncRelayCommand<SilentTaskRowViewModel>? EditCommand { get; }

    /// <summary>
    /// The project of a task, when the owner does not know it. Only used
    /// when the group stands alone — then the name of the task is all that
    /// is left.
    /// </summary>
    private static OloopProject FallbackProject(OloopTask task) => new()
    {
        FormatVersion = 1,
        Id = task.ProjectId,
        Name = task.ProjectName,
        CreatedAt = task.CreatedAt,
    };
}
