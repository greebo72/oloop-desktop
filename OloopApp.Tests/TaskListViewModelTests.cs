using OloopApp.Core.Grouping;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The list of a project filters and groups the tasks of the data root.
/// Pure logic, so it runs without a window.
/// </summary>
public class TaskListViewModelTests
{
    private const string ProjectId = "11111111-1111-4111-8111-111111111111";

    private static OloopProject Project() => new()
    {
        FormatVersion = 1,
        Id = ProjectId,
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
    };

    private static OloopTask Task(
        string id,
        TaskType type = TaskType.todo,
        TaskStatus status = TaskStatus.open,
        string title = "A task",
        string? updatedAt = null,
        string[]? labels = null,
        string projectId = ProjectId) => new()
        {
            FormatVersion = 1,
            Id = id,
            ProjectId = projectId,
            Type = type,
            Title = title,
            Status = status,
            CreatedAt = updatedAt ?? "2026-01-01T09:00:00Z",
            CreatedBy = "alice",
            UpdatedAt = updatedAt ?? "2026-01-01T09:00:00Z",
            UpdatedBy = "alice",
            Labels = labels ?? [],
        };

    private static TaskListViewModel List(MainViewModel shell) =>
        new(Project(), shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

    [Fact]
    public void Only_The_Tasks_Of_The_Project_Are_Listed()
    {
        var shell = new MainViewModel();
        shell.Tasks.Add(Task("1", title: "Mine"));
        shell.Tasks.Add(Task("2", title: "Other", projectId: "other-project"));

        var list = List(shell);

        Assert.Equal(1, list.TotalCount);
        Assert.Single(list.Groups);
        Assert.Single(list.Groups[0].Rows);
        Assert.Equal("Mine", list.Groups[0].Rows[0].Title);
    }

    [Fact]
    public void The_Search_Text_Of_The_Shell_Narrows_The_List()
    {
        var shell = new MainViewModel();
        shell.Tasks.Add(Task("1", title: "Write the docs"));
        shell.Tasks.Add(Task("2", title: "Fix the build"));

        var list = List(shell);
        Assert.Equal(2, list.Groups[0].Rows.Count);

        shell.SearchText = "docs";

        Assert.Single(list.Groups);
        Assert.Single(list.Groups[0].Rows);
        Assert.Equal("Write the docs", list.Groups[0].Rows[0].Title);
    }

    [Fact]
    public void Closed_Tasks_Are_Hidden_When_Show_Closed_Is_Off()
    {
        var shell = new MainViewModel();
        shell.Tasks.Add(Task("1", status: TaskStatus.closed, title: "Done"));
        shell.Tasks.Add(Task("2", status: TaskStatus.open, title: "Open"));

        var list = List(shell);
        Assert.Equal(2, list.Groups[0].Rows.Count);
        Assert.Equal(1, list.OpenCount);

        list.ShowClosed = false;

        Assert.Single(list.Groups[0].Rows);
        Assert.Equal("Open", list.Groups[0].Rows[0].Title);
    }

    [Fact]
    public void Closed_Tasks_Are_Hidden_By_Default()
    {
        // app-brief.md: "Closed tasks are hidden by default, with a switch
        // to show them." The list takes the default from the settings.
        var shell = new MainViewModel();
        shell.Tasks.Add(Task("1", status: TaskStatus.closed, title: "Done"));
        shell.Tasks.Add(Task("2", status: TaskStatus.open, title: "Open"));

        var list = new TaskListViewModel(Project(), shell, new OloopSettings { Grouping = "None" });

        Assert.False(list.ShowClosed);
        Assert.Single(list.Groups[0].Rows);
        Assert.Equal("Open", list.Groups[0].Rows[0].Title);
    }

    [Fact]
    public void The_Grouping_Mode_Creates_One_Group_Per_Type()
    {
        var shell = new MainViewModel();
        shell.Tasks.Add(Task("1", type: TaskType.todo, title: "Todo"));
        shell.Tasks.Add(Task("2", type: TaskType.bug, title: "Bug"));

        var list = List(shell);
        Assert.Single(list.Groups);
        Assert.False(list.Groups[0].HasTitle);

        list.Grouping = GroupingMode.Type;

        Assert.Equal(2, list.Groups.Count);
        Assert.Equal("Todo", list.Groups[0].Title);
        Assert.Equal("Bug", list.Groups[1].Title);
    }

    [Fact]
    public void The_Stale_Days_Filter_Keeps_The_Old_Tasks_Only()
    {
        var now = DateTimeOffset.UtcNow;

        var shell = new MainViewModel();
        shell.Tasks.Add(Task("1", title: "Old", updatedAt: Stamp(now.AddDays(-100))));
        shell.Tasks.Add(Task("2", title: "Fresh", updatedAt: Stamp(now.AddDays(-1))));

        var list = List(shell);
        Assert.Equal(2, list.Groups[0].Rows.Count);

        list.StaleDays = 30;

        Assert.Single(list.Groups[0].Rows);
        Assert.Equal("Old", list.Groups[0].Rows[0].Title);
    }

    [Fact]
    public void The_Open_Count_Counts_The_Open_Tasks_After_The_Filter()
    {
        var shell = new MainViewModel();
        shell.Tasks.Add(Task("1", status: TaskStatus.open));
        shell.Tasks.Add(Task("2", status: TaskStatus.closed));

        var list = List(shell);

        Assert.Equal(2, list.TotalCount);
        Assert.Equal(1, list.OpenCount);
        Assert.False(list.IsEmpty);
    }

    [Fact]
    public void An_Empty_Result_Is_Marked_As_Empty()
    {
        var shell = new MainViewModel();
        var list = List(shell);

        Assert.True(list.IsEmpty);
        Assert.Empty(list.Groups);
    }

    [Fact]
    public void Selecting_A_Row_Opens_The_Detail_Of_The_Task()
    {
        var shell = new MainViewModel();
        var task = Task("1", title: "Write the docs");
        shell.Tasks.Add(task);

        var list = List(shell);

        list.SelectRow(list.Groups[0].Rows[0]);

        Assert.NotNull(list.Selected);
        Assert.Equal("Write the docs", list.Selected!.Title);
        Assert.True(list.HasSelection);
    }

    private static string Stamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ");
}
