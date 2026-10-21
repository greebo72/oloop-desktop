using OloopApp.Core.Models;

namespace OloopApp.Core.Grouping;

/// <summary>
/// The buckets of the "silent since" view: how long a task has been quiet.
///
/// The order is from quiet to loud — the oldest task first, because that is
/// the one the view is about (SPEC.md, Native clients: "'Silent since'
/// (follows from updatedAt) is a first-class view").
/// </summary>
public enum SilenceBucket
{
    /// <summary>Not quiet for long — touched within the last week.</summary>
    Recent,

    /// <summary>Quiet for one to four weeks.</summary>
    Weeks,

    /// <summary>Quiet for one to three months.</summary>
    Months,

    /// <summary>Quiet for more than three months.</summary>
    Stale,
}

/// <summary>One bucket of the "silent since" view.</summary>
/// <param name="Title">The header of the bucket.</param>
/// <param name="Bucket">The bucket the tasks belong to.</param>
/// <param name="Tasks">The tasks of the bucket, the oldest first.</param>
public sealed record SilenceGroup(string Title, SilenceBucket Bucket, IReadOnlyList<OloopTask> Tasks);

/// <summary>
/// Groups the tasks of all projects by how long they have been quiet.
///
/// The view is about the tasks that were forgotten, so the buckets count the
/// days since <c>updatedAt</c> and the tasks inside a bucket come oldest
/// first — the opposite of the task list, where the newest comes first.
///
/// No UI code, so it is directly testable.
/// </summary>
public static class Silence
{
    /// <summary>The titles of the buckets, quietest first.</summary>
    public static readonly string[] BucketTitles = ["This week", "Weeks", "Months", "Over 3 months"];

    /// <summary>
    /// Groups the tasks by the time since they were last touched.
    ///
    /// Closed tasks are left out: a task that is done is not waiting for
    /// anybody. The caller decides about them.
    /// </summary>
    public static IReadOnlyList<SilenceGroup> Group(
        IEnumerable<OloopTask> tasks,
        DateTimeOffset? now = null)
    {
        var nowUtc = now ?? DateTimeOffset.UtcNow;

        var buckets = new Dictionary<SilenceBucket, List<OloopTask>>();
        foreach (var bucket in Enum.GetValues<SilenceBucket>())
            buckets[bucket] = [];

        foreach (var task in tasks)
        {
            var days = DaysSince(task.UpdatedAt, nowUtc);
            buckets[BucketOf(days)].Add(task);
        }

        var result = new List<SilenceGroup>();

        // The buckets come from the quietest to the loudest — the view is
        // about the tasks that were forgotten.
        foreach (var bucket in new[]
                 {
                     SilenceBucket.Stale, SilenceBucket.Months,
                     SilenceBucket.Weeks, SilenceBucket.Recent,
                 })
        {
            var list = buckets[bucket];

            // Inside a bucket the oldest comes first.
            list.Sort((a, b) => string.CompareOrdinal(a.UpdatedAt, b.UpdatedAt));

            if (list.Count == 0)
                continue;

            result.Add(new SilenceGroup(TitleOf(bucket), bucket, list));
        }

        return result;
    }

    /// <summary>
    /// The bucket a task belongs to, by the days since it was touched.
    /// </summary>
    public static SilenceBucket BucketOf(int daysSince) => daysSince switch
    {
        < 7 => SilenceBucket.Recent,
        < 30 => SilenceBucket.Weeks,
        < 90 => SilenceBucket.Months,
        _ => SilenceBucket.Stale,
    };

    /// <summary>The title of a bucket.</summary>
    public static string TitleOf(SilenceBucket bucket) => bucket switch
    {
        SilenceBucket.Recent => BucketTitles[0],
        SilenceBucket.Weeks => BucketTitles[1],
        SilenceBucket.Months => BucketTitles[2],
        SilenceBucket.Stale => BucketTitles[3],
        _ => "",
    };

    /// <summary>
    /// The days since the given timestamp. A timestamp in the future gives 0
    /// — a clock that is off must not put a task into the "over 3 months"
    /// bucket.
    /// </summary>
    public static int DaysSince(string timestamp, DateTimeOffset now)
    {
        var value = TaskGrouping.Parse(timestamp);
        var days = (int)(now - value).TotalDays;
        return days < 0 ? 0 : days;
    }
}
