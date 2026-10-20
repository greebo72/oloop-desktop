using OloopApp.Core.Grouping;
using OloopApp.Core.Models;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

public class GroupingTests
{
    private static OloopTask Task(
        string id,
        TaskType type = TaskType.todo,
        TaskStatus status = TaskStatus.open,
        string createdAt = "2026-01-01T00:00:00Z",
        string updatedAt = "2026-01-01T00:00:00Z")
        => new()
        {
            FormatVersion = 1,
            Id = id,
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = type,
            Title = id,
            Status = status,
            CreatedAt = createdAt,
            CreatedBy = "alice",
            UpdatedAt = updatedAt,
            UpdatedBy = "alice",
        };

    [Fact]
    public void None_Keeps_Sort_Order_By_UpdatedAt_Desc()
    {
        var tasks = new[]
        {
            Task("a", updatedAt: "2026-01-01T00:00:00Z"),
            Task("b", updatedAt: "2026-03-01T00:00:00Z"),
            Task("c", updatedAt: "2026-02-01T00:00:00Z"),
        };

        var groups = TaskGrouping.Group(tasks, GroupingMode.None);

        var group = Assert.Single(groups);
        Assert.Null(group.Title);
        Assert.Equal(["b", "c", "a"], group.Tasks.Select(t => t.Id));
    }

    [Fact]
    public void Type_Groups_Follow_The_Spec_Order()
    {
        var tasks = new[]
        {
            Task("a", TaskType.bug),
            Task("b", TaskType.todo),
            Task("c", TaskType.question),
        };

        var groups = TaskGrouping.Group(tasks, GroupingMode.Type);

        Assert.Equal(["Todo", "Question", "Bug"], groups.Select(g => g.Title));
    }

    [Fact]
    public void Status_Splits_Open_And_Closed()
    {
        var tasks = new[]
        {
            Task("a", status: TaskStatus.open),
            Task("b", status: TaskStatus.closed),
        };

        var groups = TaskGrouping.Group(tasks, GroupingMode.Status);

        Assert.Equal(["Open", "Closed"], groups.Select(g => g.Title));
        Assert.Single(groups[0].Tasks);
        Assert.Single(groups[1].Tasks);
    }

    [Fact]
    public void Modified_Date_Groups_Use_The_Calendar_Buckets()
    {
        var now = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        var tasks = new[]
        {
            Task("today", updatedAt: "2026-04-10T08:00:00Z"),
            Task("yesterday", updatedAt: "2026-04-09T08:00:00Z"),
            Task("week", updatedAt: "2026-04-06T08:00:00Z"),
            Task("month", updatedAt: "2026-03-20T08:00:00Z"),
            Task("older", updatedAt: "2026-01-10T08:00:00Z"),
        };

        var groups = TaskGrouping.Group(tasks, GroupingMode.Modified, now, TimeZoneInfo.Utc);

        Assert.Equal(["Today", "Yesterday", "This week", "This month", "Older"], groups.Select(g => g.Title));
    }

    [Theory]
    [InlineData("2026-04-10T11:59:30Z", "just now")]
    [InlineData("2026-04-10T11:55:00Z", "5 min")]
    [InlineData("2026-04-10T09:00:00Z", "3 h")]
    [InlineData("2026-04-09T12:00:00Z", "1 day")]
    [InlineData("2026-03-29T12:00:00Z", "12 days")]
    [InlineData("2025-12-10T12:00:00Z", "4 months")]
    [InlineData("2024-04-10T12:00:00Z", "2 years")]
    public void Age_Uses_The_Spec_Format(string timestamp, string expected)
    {
        var now = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, RelativeAge.Format(timestamp, now));
    }
}
