using OloopApp.Core.Filters;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The due date filter of the task search: the rules, and how it combines
/// with the search text, "Show closed" and the sort.
/// </summary>
public class DueFilterTests : IDisposable
{
    // 2026-03-11 is a Wednesday.
    private static readonly DateTime Today = new(2026, 3, 11);

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-duefilter-" + Guid.NewGuid().ToString("N"));

    public DueFilterTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static OloopTask Task(string due, TaskStatus status = TaskStatus.open)
        => new()
        {
            FormatVersion = 1,
            Id = "t",
            ProjectId = "p",
            Type = TaskType.todo,
            Title = "t",
            Status = status,
            DueDate = string.IsNullOrEmpty(due) ? null : due,
            CreatedAt = "2026-01-01T00:00:00Z",
            CreatedBy = "a",
            UpdatedAt = "2026-01-01T00:00:00Z",
            UpdatedBy = "a",
        };

    private static bool Passes(DueFilter filter, string due, TaskStatus status = TaskStatus.open,
        DayOfWeek first = DayOfWeek.Monday)
        => DueFilters.Matches(Task(due, status), filter, Today, first);

    // ---------------- The rules ----------------

    [Fact]
    public void All_Lets_Everything_Pass()
    {
        Assert.True(Passes(DueFilter.All, ""));
        Assert.True(Passes(DueFilter.All, "2020-01-01"));
    }

    [Fact]
    public void Overdue_Is_An_Open_Task_Before_Today_And_Never_A_Closed_One()
    {
        Assert.True(Passes(DueFilter.Overdue, "2026-03-10"));
        Assert.False(Passes(DueFilter.Overdue, "2026-03-11"));
        Assert.False(Passes(DueFilter.Overdue, "2026-03-12"));
        Assert.False(Passes(DueFilter.Overdue, ""));
        Assert.False(Passes(DueFilter.Overdue, "2020-01-01", TaskStatus.closed));
    }

    [Fact]
    public void Today_Is_The_Local_Calendar_Day()
    {
        Assert.True(Passes(DueFilter.Today, "2026-03-11"));
        Assert.False(Passes(DueFilter.Today, "2026-03-10"));
        Assert.False(Passes(DueFilter.Today, "2026-03-12"));
        Assert.False(Passes(DueFilter.Today, ""));
    }

    [Fact]
    public void This_Week_Follows_The_First_Day_Of_The_Week()
    {
        // Monday start: 2026-03-09 to 2026-03-15.
        Assert.True(Passes(DueFilter.ThisWeek, "2026-03-09"));
        Assert.True(Passes(DueFilter.ThisWeek, "2026-03-15"));
        Assert.False(Passes(DueFilter.ThisWeek, "2026-03-08"));
        Assert.False(Passes(DueFilter.ThisWeek, "2026-03-16"));

        // Sunday start (for example en-US): 2026-03-08 to 2026-03-14.
        Assert.True(Passes(DueFilter.ThisWeek, "2026-03-08", first: DayOfWeek.Sunday));
        Assert.False(Passes(DueFilter.ThisWeek, "2026-03-15", first: DayOfWeek.Sunday));

        Assert.False(Passes(DueFilter.ThisWeek, ""));
    }

    [Fact]
    public void With_And_Without_Date_Do_Not_Depend_On_The_Status()
    {
        Assert.True(Passes(DueFilter.WithDate, "2020-01-01", TaskStatus.closed));
        Assert.False(Passes(DueFilter.WithDate, ""));
        Assert.True(Passes(DueFilter.WithoutDate, ""));
        Assert.False(Passes(DueFilter.WithoutDate, "2026-03-11"));
    }

    // ---------------- The search on the dashboard ----------------

    private MainViewModel Shell() => new(new OloopSettings
    {
        FilePath = Path.Combine(_root, "settings.json"),
        DataRoot = _root,
        Actor = "claude",
    })
    {
        DataRoot = _root,
        Actor = "claude",
    };

    private static string Day(int offset)
        => DateTime.Today.AddDays(offset).ToString("yyyy-MM-dd");

    private async Task<MainViewModel> SeededShell()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");
        await store.CreateTaskAsync(project, TaskType.todo, "late report", "", [], "claude", dueDate: Day(-3));
        await store.CreateTaskAsync(project, TaskType.todo, "today report", "", [], "claude", dueDate: Day(0));
        await store.CreateTaskAsync(project, TaskType.todo, "far report", "", [], "claude", dueDate: Day(400));
        await store.CreateTaskAsync(project, TaskType.todo, "undated report", "", [], "claude");
        var closed = await store.CreateTaskAsync(project, TaskType.todo, "closed late", "", [], "claude", dueDate: Day(-9));
        await store.SetStatusAsync(closed, project, true, "claude");

        var shell = Shell();
        await shell.RefreshAsync();
        shell.ShowTaskSearchCommand.Execute(null);
        return shell;
    }

    private static void Pick(MainViewModel shell, DueFilter filter)
        => shell.Dashboard.SelectedDueFilter =
            shell.Dashboard.DueFilterOptions.Single(o => o.Filter == filter);

    private static string[] Titles(MainViewModel shell)
        => shell.Dashboard.TaskResults.Select(r => r.Title).Order().ToArray();

    [Fact]
    public async Task The_Filter_Starts_On_All_And_Shows_The_Hint_Without_Text()
    {
        var shell = await SeededShell();

        Assert.Equal(DueFilter.All, shell.Dashboard.SelectedDueFilter.Filter);
        Assert.True(shell.Dashboard.ShowTaskHint);
        Assert.Empty(shell.Dashboard.TaskResults);
    }

    [Fact]
    public async Task A_Filter_Alone_Lists_Tasks_Without_A_Search_Text()
    {
        var shell = await SeededShell();

        Pick(shell, DueFilter.Overdue);

        Assert.False(shell.Dashboard.ShowTaskHint);
        Assert.Equal(["late report"], Titles(shell));
    }

    [Fact]
    public async Task The_Filter_Combines_With_The_Search_Text_And_Show_Closed()
    {
        var shell = await SeededShell();

        Pick(shell, DueFilter.WithDate);
        shell.SearchText = "report";
        Assert.Equal(["far report", "late report", "today report"], Titles(shell));

        Pick(shell, DueFilter.WithoutDate);
        Assert.Equal(["undated report"], Titles(shell));

        // A closed task keeps its date but is never overdue, with or without
        // "Show closed".
        Pick(shell, DueFilter.Overdue);
        shell.SearchText = "";
        shell.Dashboard.ShowClosed = true;
        Assert.Equal(["late report"], Titles(shell));

        Pick(shell, DueFilter.WithDate);
        Assert.Contains("closed late", Titles(shell));
    }

    [Fact]
    public async Task The_Results_Can_Be_Sorted_By_Due_Date_With_Undated_Tasks_Last()
    {
        var shell = await SeededShell();
        shell.SearchText = "report";

        shell.Dashboard.SelectedSort = shell.Dashboard.SortOptions.Single(o => o.ByDueDate);

        Assert.Equal(
            ["late report", "today report", "far report", "undated report"],
            shell.Dashboard.TaskResults.Select(r => r.Title).ToArray());
    }

    [Fact]
    public async Task A_Result_Shows_The_Due_Date_And_The_Overdue_Flag()
    {
        var shell = await SeededShell();
        Pick(shell, DueFilter.WithDate);

        var late = shell.Dashboard.TaskResults.Single(r => r.Title == "late report");

        Assert.True(late.HasDueDate);
        Assert.True(late.IsOverdue);
    }
}
