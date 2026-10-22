using OloopApp.Core.Models;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Core.Grouping;

/// <summary>How the task list is grouped.</summary>
public enum GroupingMode
{
    None,
    Created,
    Modified,
    Type,
    Status,
    DueDate,
}

/// <summary>A group of tasks. <c>Title</c> is null for <see cref="GroupingMode.None"/>.</summary>
public sealed record TaskGroup(string? Title, IReadOnlyList<OloopTask> Tasks);

/// <summary>
/// Groups tasks for the list view. No UI code, so it is directly testable.
///
/// Inside a group the newest <c>updatedAt</c> comes first. Date buckets count
/// calendar days: today, yesterday, 2 to 6 days ago (this week), 7 to 30 days
/// ago (this month), older.
/// </summary>
public static class TaskGrouping
{
    /// <summary>The labels of the date buckets, oldest last.</summary>
    public static readonly string[] DateBuckets = ["Today", "Yesterday", "This week", "This month", "Older"];

    public static IReadOnlyList<TaskGroup> Group(
        IEnumerable<OloopTask> tasks,
        GroupingMode mode,
        DateTimeOffset? now = null,
        TimeZoneInfo? zone = null)
    {
        var nowUtc = now ?? DateTimeOffset.UtcNow;
        var local = zone ?? TimeZoneInfo.Local;
        var sorted = tasks
            .OrderByDescending(t => t.UpdatedAt, StringComparer.Ordinal)
            .ToList();

        switch (mode)
        {
            case GroupingMode.None:
                return [new TaskGroup(null, sorted)];

            case GroupingMode.Created:
                return ByDate(sorted, nowUtc, local, t => t.CreatedAt);

            case GroupingMode.Modified:
                return ByDate(sorted, nowUtc, local, t => t.UpdatedAt);

            case GroupingMode.Type:
                return TaskTypeExtensions.All
                    .Select(type =>
                    {
                        var group = sorted.Where(t => t.Type == type).ToList();
                        return group.Count == 0 ? null : new TaskGroup(type.DisplayName(), group);
                    })
                    .Where(g => g is not null)
                    .Select(g => g!)
                    .ToList();

            case GroupingMode.Status:
                return new[]
                    {
                        new TaskGroup("Open", sorted.Where(t => t.Status == TaskStatus.open).ToList()),
                        new TaskGroup("Closed", sorted.Where(t => t.Status == TaskStatus.closed).ToList()),
                    }
                    .Where(g => g.Tasks.Count > 0)
                    .ToList();

            case GroupingMode.DueDate:
                return ByDueDate(tasks, TimeZoneInfo.ConvertTime(nowUtc, local).Date);

            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }
    }

    /// <summary>The labels of the due date groups, in the order they are shown.</summary>
    public static readonly string[] DueDateBuckets = ["Overdue", "Today", "This week", "Later", "Earlier", "No due date"];

    /// <summary>
    /// Groups by due date, the earliest first inside a group, tasks without a
    /// date last. "This week" runs from tomorrow to the end of the calendar
    /// week of the system's culture. A closed task with a past date is never
    /// overdue, it goes to "Earlier".
    /// </summary>
    private static IReadOnlyList<TaskGroup> ByDueDate(IEnumerable<OloopTask> tasks, DateTime today)
    {
        var first = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
        var daysIntoWeek = ((int)today.DayOfWeek - (int)first + 7) % 7;
        var endOfWeek = today.AddDays(6 - daysIntoWeek);

        var buckets = DueDateBuckets.ToDictionary(name => name, _ => new List<OloopTask>());

        foreach (var task in tasks
                     .OrderBy(t => DueDate.Parse(t.DueDate) ?? DateTime.MaxValue)
                     .ThenByDescending(t => t.UpdatedAt, StringComparer.Ordinal))
        {
            var date = DueDate.Parse(task.DueDate);
            string name;
            if (date is null)
                name = "No due date";
            else if (date.Value.Date < today)
                name = task.IsClosed ? "Earlier" : "Overdue";
            else if (date.Value.Date == today)
                name = "Today";
            else if (date.Value.Date <= endOfWeek)
                name = "This week";
            else
                name = "Later";

            buckets[name].Add(task);
        }

        return DueDateBuckets
            .Where(name => buckets[name].Count > 0)
            .Select(name => new TaskGroup(name, buckets[name]))
            .ToList();
    }

    private static IReadOnlyList<TaskGroup> ByDate(
        IReadOnlyList<OloopTask> sorted,
        DateTimeOffset now,
        TimeZoneInfo zone,
        Func<OloopTask, string> stamp)
    {
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        var buckets = new Dictionary<string, List<OloopTask>>();

        foreach (var task in sorted)
        {
            var day = TimeZoneInfo.ConvertTime(Parse(stamp(task)), zone).Date;
            var daysAgo = (int)(today - day).TotalDays;
            var name = Bucket(daysAgo);
            if (!buckets.TryGetValue(name, out var list))
                buckets[name] = list = [];
            list.Add(task);
        }

        return DateBuckets
            .Where(buckets.ContainsKey)
            .Select(name => new TaskGroup(name, buckets[name]))
            .ToList();
    }

    private static string Bucket(int daysAgo) => daysAgo switch
    {
        <= 0 => "Today",
        1 => "Yesterday",
        <= 6 => "This week",
        <= 30 => "This month",
        _ => "Older",
    };

    /// <summary>
    /// Parses an ISO 8601 timestamp. Unparsable values fall back to the
    /// epoch, so one broken timestamp never breaks the whole list.
    /// </summary>
    public static DateTimeOffset Parse(string timestamp)
    {
        if (DateTimeOffset.TryParse(timestamp, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var value))
            return value;
        return DateTimeOffset.UnixEpoch;
    }
}
