namespace OloopApp.Core.Grouping;

/// <summary>
/// The age of a task as shown in the list: "just now", "5 min", "3 h",
/// "1 day", "12 days", "4 months", "2 years".
/// </summary>
public static class RelativeAge
{
    public static string Format(string timestamp, DateTimeOffset? now = null)
    {
        var nowUtc = now ?? DateTimeOffset.UtcNow;
        var value = TaskGrouping.Parse(timestamp);
        var span = nowUtc - value;

        var minutes = (long)Math.Floor(span.TotalMinutes);
        if (minutes < 1)
            return "just now";

        var hours = (long)Math.Floor(span.TotalHours);
        if (hours < 1)
            return $"{minutes} min";

        var days = (long)Math.Floor(span.TotalDays);
        if (days < 1)
            return $"{hours} h";
        if (days == 1)
            return "1 day";
        if (days < 60)
            return $"{days} days";
        if (days < 730)
            return $"{days / 30} months";
        return $"{days / 365} years";
    }
}
