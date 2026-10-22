using System.Globalization;
using OloopApp.Core.Models;

namespace OloopApp.Core.Grouping;

/// <summary>
/// The due date of a task. It is a calendar date without a time zone: it is
/// stored as <c>YYYY-MM-DD</c> and compared with the local date, never
/// converted.
/// </summary>
public static class DueDate
{
    private const string StorageFormat = "yyyy-MM-dd";

    /// <summary>
    /// Reads a stored date. The result has the kind Unspecified, so no
    /// conversion can move it by a day. Null for a missing or invalid value.
    /// </summary>
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        return DateTime.TryParseExact(value, StorageFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var date)
            ? DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified)
            : null;
    }

    /// <summary>
    /// The text that is stored: the date part only, formatted with the
    /// invariant culture. Null when there is no date.
    /// </summary>
    public static string? ToStorage(DateTime? date)
        => date?.Date.ToString(StorageFormat, CultureInfo.InvariantCulture);

    /// <summary>The date as the user reads it, in the culture of the system.</summary>
    public static string? Display(string? value)
        => Parse(value)?.ToString("d", CultureInfo.CurrentCulture);

    /// <summary>
    /// Whether the task is overdue: open, with a date before today. A closed
    /// task is never overdue, it only keeps its date.
    /// </summary>
    public static bool IsOverdue(OloopTask task, DateTime today)
        => !task.IsClosed && Parse(task.DueDate) is { } date && date.Date < today.Date;
}
