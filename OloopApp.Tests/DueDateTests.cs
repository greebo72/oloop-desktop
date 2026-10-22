using OloopApp.Core.Grouping;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The due date in the desktop app: reading and writing it as a plain date,
/// the overdue rule, the grouping and the edit of it.
/// </summary>
public class DueDateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-due-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static OloopTask Task(string id, string? dueDate, TaskStatus status = TaskStatus.open)
        => new()
        {
            FormatVersion = 1,
            Id = id,
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = TaskType.todo,
            Title = id,
            Status = status,
            DueDate = dueDate,
            CreatedAt = "2026-01-01T00:00:00Z",
            CreatedBy = "alice",
            UpdatedAt = "2026-01-01T00:00:00Z",
            UpdatedBy = "alice",
        };

    // ---------------- The date ----------------

    [Fact]
    public void A_Stored_Date_Has_No_Kind_And_No_Time()
    {
        var date = DueDate.Parse("2026-03-09");

        Assert.Equal(new DateTime(2026, 3, 9), date);
        Assert.Equal(DateTimeKind.Unspecified, date!.Value.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("09.03.2026")]
    [InlineData("2026-13-01")]
    public void A_Missing_Or_Invalid_Date_Reads_As_None(string? value)
    {
        Assert.Null(DueDate.Parse(value));
    }

    [Fact]
    public void Only_The_Date_Part_Is_Stored_Whatever_The_Kind()
    {
        // 23:30 UTC and a local time must not move to the next or the
        // previous day.
        Assert.Equal("2026-03-09", DueDate.ToStorage(new DateTime(2026, 3, 9, 23, 30, 0, DateTimeKind.Utc)));
        Assert.Equal("2026-03-09", DueDate.ToStorage(new DateTime(2026, 3, 9, 0, 5, 0, DateTimeKind.Local)));
        Assert.Null(DueDate.ToStorage(null));
    }

    // ---------------- Overdue ----------------

    [Fact]
    public void An_Open_Task_With_A_Past_Date_Is_Overdue()
    {
        var today = new DateTime(2026, 3, 9);

        Assert.True(DueDate.IsOverdue(Task("a", "2026-03-08"), today));
        Assert.False(DueDate.IsOverdue(Task("b", "2026-03-09"), today));
        Assert.False(DueDate.IsOverdue(Task("c", "2026-03-10"), today));
        Assert.False(DueDate.IsOverdue(Task("d", null), today));
    }

    [Fact]
    public void A_Closed_Task_Is_Never_Overdue()
    {
        Assert.False(DueDate.IsOverdue(Task("a", "2020-01-01", TaskStatus.closed), new DateTime(2026, 3, 9)));
    }

    [Fact]
    public void The_Row_Shows_The_Date_And_The_Overdue_Flag()
    {
        var now = new DateTimeOffset(2026, 3, 9, 12, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 3, 9)));

        var late = new TaskRowViewModel(Task("a", "2026-03-01"), now);
        var none = new TaskRowViewModel(Task("b", null), now);

        Assert.True(late.HasDueDate);
        Assert.True(late.IsOverdue);
        Assert.False(none.HasDueDate);
        Assert.False(none.IsOverdue);
    }

    // ---------------- Grouping ----------------

    [Fact]
    public void The_Due_Date_Grouping_Orders_The_Groups_And_Puts_Tasks_Without_A_Date_Last()
    {
        // 2026-03-11 is a Wednesday.
        var now = new DateTimeOffset(2026, 3, 11, 12, 0, 0, TimeSpan.Zero);
        var tasks = new[]
        {
            Task("none", null),
            Task("later", "2026-12-01"),
            Task("late-b", "2026-03-05"),
            Task("late-a", "2026-03-01"),
            Task("today", "2026-03-11"),
            Task("old-closed", "2026-01-01", TaskStatus.closed),
        };

        var groups = TaskGrouping.Group(tasks, GroupingMode.DueDate, now, TimeZoneInfo.Utc);

        Assert.Equal(["Overdue", "Today", "Later", "Earlier", "No due date"],
            groups.Select(g => g.Title).ToArray());
        Assert.Equal(["late-a", "late-b"], groups[0].Tasks.Select(t => t.Id).ToArray());
    }

    // ---------------- Writing ----------------

    [Fact]
    public async Task A_New_Task_Can_Have_A_Due_Date()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Due");

        var created = await store.CreateTaskAsync(
            project, TaskType.todo, "With date", "", [], "alice", dueDate: "2026-05-17");

        var snapshot = await store.LoadAsync();
        Assert.Equal("2026-05-17", snapshot.Tasks.Single(t => t.Id == created.Id).DueDate);
    }

    [Fact]
    public async Task An_Edit_Sets_Changes_And_Removes_The_Date_And_An_Edit_Without_It_Keeps_It()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Due");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "T", "", [], "alice");

        task = await store.EditTaskAsync(task, project, null, null, null, "alice",
            setDueDate: true, dueDate: "2026-05-17");
        Assert.Equal("2026-05-17", task.DueDate);

        // An edit that does not mention the date leaves it alone.
        task = await store.EditTaskAsync(task, project, "T2", null, null, "alice");
        Assert.Equal("2026-05-17", task.DueDate);

        task = await store.EditTaskAsync(task, project, null, null, null, "alice",
            setDueDate: true, dueDate: null);
        Assert.Null(task.DueDate);

        // The key is left out of the file, not written as null.
        var file = Path.Combine(_root, "due", "tasks", task.Id + ".md");
        Assert.DoesNotContain("dueDate", File.ReadAllText(file));
    }

    // ---------------- The edit ----------------

    [Fact]
    public void Changing_Or_Clearing_The_Date_Makes_The_Edit_Dirty()
    {
        var editor = new EditTaskViewModel(Task("a", "2026-03-09"));
        Assert.False(editor.IsDirty);
        Assert.Equal("2026-03-09", editor.DueDateStorage);

        editor.DueDate = new DateTime(2026, 3, 10, 15, 0, 0);
        Assert.True(editor.IsDirty);
        Assert.Equal("2026-03-10", editor.DueDateStorage);

        editor.DueDate = new DateTime(2026, 3, 9);
        Assert.False(editor.IsDirty);

        editor.ClearDueDateCommand.Execute(null);
        Assert.True(editor.IsDirty);
        Assert.Null(editor.DueDateStorage);
    }
}
