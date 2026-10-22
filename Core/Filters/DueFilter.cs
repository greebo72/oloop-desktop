using System.Globalization;
using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.Core.Filters;

/// <summary>The choices of the due date filter of the task search.</summary>
public enum DueFilter
{
    /// <summary>No filter, every task passes.</summary>
    All,

    /// <summary>Open tasks with a due date before today. A closed task is never overdue.</summary>
    Overdue,

    /// <summary>Tasks that are due today.</summary>
    Today,

    /// <summary>Tasks that are due in the current calendar week.</summary>
    ThisWeek,

    /// <summary>Tasks that have a due date.</summary>
    WithDate,

    /// <summary>Tasks without a due date.</summary>
    WithoutDate,
}

/// <summary>
/// The rules of <see cref="DueFilter"/>. The due date is a plain calendar
/// date without a time zone, so it is compared with the local date of today
/// and never converted.
/// </summary>
public static class DueFilters
{
    /// <summary>The label of a filter choice, as the filter box shows it.</summary>
    public static string Label(this DueFilter filter) => filter switch
    {
        DueFilter.All => "Any due date",
        DueFilter.Overdue => "Overdue",
        DueFilter.Today => "Due today",
        DueFilter.ThisWeek => "Due this week",
        DueFilter.WithDate => "With due date",
        DueFilter.WithoutDate => "Without due date",
        _ => filter.ToString(),
    };

    /// <summary>
    /// Whether a task passes the filter.
    ///
    /// "This week" is the calendar week of <paramref name="today"/>, from the
    /// first day of the week of the culture to the day before the next one.
    /// It includes the days of this week that are already past.
    /// </summary>
    /// <param name="task">The task to check.</param>
    /// <param name="filter">The filter choice.</param>
    /// <param name="today">The local calendar day of today.</param>
    /// <param name="firstDayOfWeek">The first day of the week. Null takes the one of the system culture.</param>
    public static bool Matches(
        OloopTask task, DueFilter filter, DateTime today, DayOfWeek? firstDayOfWeek = null)
    {
        var date = DueDate.Parse(task.DueDate)?.Date;
        var day = today.Date;

        switch (filter)
        {
            case DueFilter.All:
                return true;

            case DueFilter.WithDate:
                return date is not null;

            case DueFilter.WithoutDate:
                return date is null;

            case DueFilter.Overdue:
                return DueDate.IsOverdue(task, day);

            case DueFilter.Today:
                return date == day;

            case DueFilter.ThisWeek:
                if (date is null)
                    return false;

                var first = firstDayOfWeek ?? CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
                var start = day.AddDays(-(((int)day.DayOfWeek - (int)first + 7) % 7));
                return date >= start && date < start.AddDays(7);

            default:
                throw new ArgumentOutOfRangeException(nameof(filter), filter, null);
        }
    }
}
