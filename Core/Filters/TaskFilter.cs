using OloopApp.Core.Grouping;
using OloopApp.Core.Models;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Core.Filters;

/// <summary>
/// Filters the task list: free-text search, "silent since" (stale days),
/// type, status and label.
/// </summary>
public sealed class TaskFilter
{
    /// <summary>Free-text search over title and labels. Empty matches everything.</summary>
    public string SearchText { get; set; } = "";

    /// <summary>
    /// "Silent since" in days. A task counts as stale when it was not touched
    /// for at least that many days, i.e. <c>updatedAt &lt;= now - days</c>.
    /// Null disables the filter.
    /// </summary>
    public double? StaleDays { get; set; }

    /// <summary>Only tasks of this type. Null shows all types.</summary>
    public TaskType? Type { get; set; }

    /// <summary>Only tasks with this status. Null shows both.</summary>
    public TaskStatus? Status { get; set; }

    /// <summary>Only tasks carrying this label. Null shows all labels.</summary>
    public string? Label { get; set; }

    /// <summary>Whether closed tasks are part of the result.</summary>
    public bool ShowClosed { get; set; } = true;

    /// <summary>Applies the filter.</summary>
    public IEnumerable<OloopTask> Apply(IEnumerable<OloopTask> tasks, DateTimeOffset? now = null)
    {
        var nowUtc = now ?? DateTimeOffset.UtcNow;
        var needle = SearchText.Trim();

        foreach (var task in tasks)
        {
            if (!ShowClosed && task.IsClosed)
                continue;

            if (Type is not null && task.Type != Type)
                continue;

            if (Status is not null && task.Status != Status)
                continue;

            if (Label is not null && !task.Labels.Contains(Label, StringComparer.OrdinalIgnoreCase))
                continue;

            if (StaleDays is { } days)
            {
                var limit = nowUtc.AddDays(-days);
                if (TaskGrouping.Parse(task.UpdatedAt) > limit)
                    continue;
            }

            if (needle.Length > 0 && !Matches(task, needle))
                continue;

            yield return task;
        }
    }

    private static bool Matches(OloopTask task, string needle)
    {
        if (task.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
            return true;

        return task.Labels.Any(l => l.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
