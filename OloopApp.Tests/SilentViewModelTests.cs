using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The "silent since" view of the shell: it shows the tasks of all
/// projects, the quietest first, and the navigation between the views.
/// </summary>
public class SilentViewModelTests
{
    private static OloopProject Project(string name, string id) => new()
    {
        FormatVersion = 1,
        Id = id,
        Name = name,
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = name.ToLowerInvariant(),
    };

    private static OloopTask Task(
        string title,
        string projectId,
        int daysAgo,
        TaskStatus status = TaskStatus.open)
    {
        var stamp = DateTimeOffset.UtcNow.AddDays(-daysAgo)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        return new OloopTask
        {
            FormatVersion = 1,
            Id = Guid.NewGuid().ToString("D"),
            ProjectId = projectId,
            Type = TaskType.todo,
            Title = title,
            Status = status,
            CreatedAt = stamp,
            CreatedBy = "alice",
            UpdatedAt = stamp,
            UpdatedBy = "alice",
        };
    }

    /// <summary>
    /// A shell with two projects and one task in each: one quiet, one
    /// fresh.
    /// </summary>
    private static MainViewModel Shell()
    {
        var shell = new MainViewModel(new OloopSettings
        {
            FilePath = Path.Combine(Path.GetTempPath(), "oloop-silent-settings.json"),
        })
        {
            DataRoot = "",
        };

        var alpha = Project("Alpha", "11111111-1111-4111-8111-111111111111");
        var beta = Project("Beta", "22222222-2222-4222-8222-222222222222");

        shell.Projects.Add(alpha);
        shell.Projects.Add(beta);

        shell.Tasks.Add(Task("Quiet task", alpha.Id, 100));
        shell.Tasks.Add(Task("Fresh task", beta.Id, 1));

        return shell;
    }

    // ---------------- The view ----------------

    [Fact]
    public void The_View_Shows_The_Tasks_Of_All_Projects()
    {
        var shell = Shell();

        var view = new SilentTasksViewModel(shell);

        var rows = view.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ProjectName == "Alpha");
        Assert.Contains(rows, r => r.ProjectName == "Beta");
    }

    [Fact]
    public void The_View_Lists_The_Quietest_First()
    {
        var shell = Shell();

        var view = new SilentTasksViewModel(shell);

        var rows = view.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal("Quiet task", rows[0].Title);
        Assert.Equal("Fresh task", rows[1].Title);
    }

    [Fact]
    public void A_Lower_Bound_Hides_The_Tasks_That_Are_Too_Fresh()
    {
        var shell = Shell();

        var view = new SilentTasksViewModel(shell) { MinimumAgeDays = 30 };
        view.Refresh();

        var rows = view.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Single(rows);
        Assert.Equal("Quiet task", rows[0].Title);
    }

    [Fact]
    public void Closed_Tasks_Are_Not_Shown_By_Default()
    {
        var shell = Shell();
        shell.Tasks.Add(Task("Done task", shell.Projects[0].Id, 100, TaskStatus.closed));

        var view = new SilentTasksViewModel(shell);

        var rows = view.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => r.Title == "Done task");
    }

    [Fact]
    public void Closed_Tasks_Can_Be_Shown()
    {
        var shell = Shell();
        shell.Tasks.Add(Task("Done task", shell.Projects[0].Id, 100, TaskStatus.closed));

        var view = new SilentTasksViewModel(shell) { ShowClosed = true };
        view.Refresh();

        var rows = view.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Equal(3, rows.Count);
        Assert.Contains(rows, r => r.Title == "Done task");
    }

    [Fact]
    public void The_Search_Filters_The_View()
    {
        var shell = Shell();

        var view = new SilentTasksViewModel(shell);
        shell.SearchText = "Quiet";

        var rows = view.Groups.SelectMany(g => g.Rows).ToList();
        Assert.Single(rows);
        Assert.Equal("Quiet task", rows[0].Title);
    }

    [Fact]
    public void The_View_Is_Empty_When_Nothing_Is_Quiet()
    {
        var shell = Shell();

        var view = new SilentTasksViewModel(shell) { MinimumAgeDays = 365 };
        view.Refresh();

        Assert.True(view.IsEmpty);
        Assert.Empty(view.Groups);
    }

    [Fact]
    public void The_Summary_Says_How_Many_Tasks_And_Projects_It_Shows()
    {
        var shell = Shell();

        var view = new SilentTasksViewModel(shell);

        Assert.Equal("2 tasks in 2 projects", view.Summary);
    }

    [Fact]
    public void A_Selected_Task_Shows_Its_Detail()
    {
        var shell = Shell();

        var view = new SilentTasksViewModel(shell);
        var row = view.Groups.SelectMany(g => g.Rows).First();

        view.SelectRow(row);

        Assert.NotNull(view.Selected);
        Assert.Equal(row.Task.Id, view.Selected!.Task.Id);
        Assert.Equal("Alpha", view.Selected.Project.Name);
    }

    // ---------------- The navigation ----------------

    [Fact]
    public void The_Command_Opens_The_View()
    {
        var shell = Shell();

        shell.OpenSilentCommand.Execute(null);

        Assert.NotNull(shell.Silent);
        Assert.True(shell.IsSilent);
        Assert.False(shell.IsDashboard);
        Assert.True(shell.HasSubView);
    }

    [Fact]
    public void The_Command_Closes_The_View_When_It_Is_Open()
    {
        var shell = Shell();

        shell.ToggleSilentCommand.Execute(null);
        Assert.NotNull(shell.Silent);

        shell.ToggleSilentCommand.Execute(null);
        Assert.Null(shell.Silent);
        Assert.True(shell.IsDashboard);
    }

    [Fact]
    public void Opening_A_Project_Closes_The_View()
    {
        var shell = Shell();
        shell.OpenSilentCommand.Execute(null);

        shell.Dashboard.Update(new OloopSnapshot
        {
            Projects = [shell.Projects[0]],
            Tasks = [],
            Unreadable = [],
        });

        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

        Assert.Null(shell.Silent);
        Assert.NotNull(shell.Split);
        Assert.False(shell.IsSilent);
        Assert.True(shell.IsDashboard == false);
    }

    [Fact]
    public void The_Back_Button_Closes_Both_Views()
    {
        var shell = Shell();
        shell.OpenSilentCommand.Execute(null);

        shell.BackToDashboardCommand.Execute(null);

        Assert.Null(shell.Silent);
        Assert.Null(shell.Split);
        Assert.True(shell.IsDashboard);
        Assert.False(shell.HasSubView);
    }
}
