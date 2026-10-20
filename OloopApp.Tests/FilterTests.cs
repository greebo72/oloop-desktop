using OloopApp.Core.Filters;
using OloopApp.Core.Models;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

public class FilterTests
{
    private static OloopTask Task(
        string id,
        string title,
        string updatedAt = "2026-01-01T00:00:00Z",
        TaskStatus status = TaskStatus.open,
        params string[] labels)
        => new()
        {
            FormatVersion = 1,
            Id = id,
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = TaskType.todo,
            Title = title,
            Status = status,
            Labels = labels,
            CreatedAt = "2026-01-01T00:00:00Z",
            CreatedBy = "alice",
            UpdatedAt = updatedAt,
            UpdatedBy = "alice",
        };

    [Fact]
    public void Search_Matches_Title_Case_Insensitively()
    {
        var tasks = new[] { Task("a", "Report for Q3"), Task("b", "Something else") };
        var filter = new TaskFilter { SearchText = "q3" };

        Assert.Equal(["a"], filter.Apply(tasks).Select(t => t.Id));
    }

    [Fact]
    public void Search_Matches_Labels()
    {
        var tasks = new[] { Task("a", "Report", labels: ["urgent"]), Task("b", "Other", labels: ["docs"]) };
        var filter = new TaskFilter { SearchText = "URGENT" };

        Assert.Equal(["a"], filter.Apply(tasks).Select(t => t.Id));
    }

    [Fact]
    public void Stale_Keeps_Tasks_Not_Touched_Lately()
    {
        var now = new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero);
        var old = Task("old", "Old", updatedAt: "2026-04-01T12:00:00Z");
        var fresh = Task("fresh", "Fresh", updatedAt: "2026-04-10T06:00:00Z");
        var filter = new TaskFilter { StaleDays = 7 };

        Assert.Equal(["old"], filter.Apply([old, fresh], now).Select(t => t.Id));
    }

    [Fact]
    public void ShowClosed_False_Hides_Closed_Tasks()
    {
        var open = Task("a", "Open");
        var closed = Task("b", "Closed", status: TaskStatus.closed);

        var filter = new TaskFilter { ShowClosed = false };

        Assert.Equal(["a"], filter.Apply([open, closed]).Select(t => t.Id));
    }
}
