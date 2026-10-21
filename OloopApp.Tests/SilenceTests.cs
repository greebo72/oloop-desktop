using OloopApp.Core.Grouping;
using OloopApp.Core.Models;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The "silent since" view: the tasks are grouped by how long they have
/// been quiet, and the quietest come first.
/// </summary>
public class SilenceTests
{
    private static DateTimeOffset Now => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A task that was last touched the given days ago.</summary>
    private static OloopTask Task(string title, int daysAgo, TaskStatus status = TaskStatus.open)
    {
        var updated = Now.AddDays(-daysAgo).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        return new OloopTask
        {
            FormatVersion = 1,
            Id = Guid.NewGuid().ToString("D"),
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = TaskType.todo,
            Title = title,
            Status = status,
            CreatedAt = updated,
            CreatedBy = "alice",
            UpdatedAt = updated,
            UpdatedBy = "alice",
        };
    }

    // ---------------- The buckets ----------------

    [Fact]
    public void A_Touched_Task_Is_Recent()
    {
        Assert.Equal(SilenceBucket.Recent, Silence.BucketOf(0));
        Assert.Equal(SilenceBucket.Recent, Silence.BucketOf(6));
    }

    [Fact]
    public void A_Quiet_Task_Is_In_Weeks()
    {
        Assert.Equal(SilenceBucket.Weeks, Silence.BucketOf(7));
        Assert.Equal(SilenceBucket.Weeks, Silence.BucketOf(29));
    }

    [Fact]
    public void A_Very_Quiet_Task_Is_In_Months()
    {
        Assert.Equal(SilenceBucket.Months, Silence.BucketOf(30));
        Assert.Equal(SilenceBucket.Months, Silence.BucketOf(89));
    }

    [Fact]
    public void A_Task_Of_More_Than_Three_Months_Is_Stale()
    {
        Assert.Equal(SilenceBucket.Stale, Silence.BucketOf(90));
        Assert.Equal(SilenceBucket.Stale, Silence.BucketOf(400));
    }

    [Fact]
    public void The_Days_Since_A_Timestamp_Are_Never_Negative()
    {
        // A clock that is off must not put a task into the oldest bucket.
        var future = Now.AddDays(5).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        Assert.Equal(0, Silence.DaysSince(future, Now));
    }

    // ---------------- The grouping ----------------

    [Fact]
    public void The_Groups_Come_From_The_Quietest_To_The_Loudest()
    {
        var groups = Silence.Group(
            [
                Task("fresh", 1),
                Task("ancient", 200),
                Task("old", 40),
            ],
            Now);

        Assert.Equal(
            [SilenceBucket.Stale, SilenceBucket.Months, SilenceBucket.Recent],
            groups.Select(g => g.Bucket));
    }

    [Fact]
    public void Inside_A_Group_The_Oldest_Comes_First()
    {
        var groups = Silence.Group(
            [
                Task("newer", 40),
                Task("older", 80),
            ],
            Now);

        var group = Assert.Single(groups);
        Assert.Equal(["older", "newer"], group.Tasks.Select(t => t.Title));
    }

    [Fact]
    public void An_Empty_Bucket_Is_Left_Out()
    {
        var groups = Silence.Group([Task("fresh", 1)], Now);

        var group = Assert.Single(groups);
        Assert.Equal(SilenceBucket.Recent, group.Bucket);
    }

    [Fact]
    public void The_Title_Of_A_Bucket_Says_How_Long_It_Was_Quiet()
    {
        Assert.Equal("This week", Silence.TitleOf(SilenceBucket.Recent));
        Assert.Equal("Weeks", Silence.TitleOf(SilenceBucket.Weeks));
        Assert.Equal("Months", Silence.TitleOf(SilenceBucket.Months));
        Assert.Equal("Over 3 months", Silence.TitleOf(SilenceBucket.Stale));
    }
}
