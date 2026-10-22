using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The search of the dashboard looks for projects or for tasks of all
/// projects, never both. A task result opens the task in its project.
/// </summary>
public class DashboardTaskSearchTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-tasksearch-" + Guid.NewGuid().ToString("N"));

    public DashboardTaskSearchTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

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

    private async Task<(OloopStore store, OloopProject alpha, OloopProject beta)> Seed()
    {
        var store = new OloopStore(_root);
        var alpha = await store.CreateProjectAsync("Alpha");
        var beta = await store.CreateProjectAsync("Beta");
        await store.CreateTaskAsync(alpha, TaskType.todo, "Fix the parser", "", ["core"], "claude");
        await store.CreateTaskAsync(beta, TaskType.bug, "Crash", "The parser throws on start", [], "claude");
        await store.CreateTaskAsync(beta, TaskType.todo, "Docs", "", ["parser-docs"], "claude");
        return (store, alpha, beta);
    }

    [Fact]
    public async Task The_Search_Starts_On_Projects()
    {
        var shell = Shell();

        Assert.False(shell.Dashboard.SearchTasks);
        Assert.True(shell.Dashboard.SearchProjects);
        Assert.Equal("Find projects …", shell.SearchPlaceholder);
    }

    [Fact]
    public async Task The_Task_Search_Finds_Title_Description_And_Labels_Across_Projects()
    {
        await Seed();
        var shell = Shell();
        await shell.RefreshAsync();

        shell.ShowTaskSearchCommand.Execute(null);
        shell.SearchText = "parser";

        var found = shell.Dashboard.TaskResults
            .Select(r => $"{r.ProjectName}: {r.Title}")
            .Order()
            .ToArray();

        Assert.Equal(["Alpha: Fix the parser", "Beta: Crash", "Beta: Docs"], found);
        Assert.Equal("Find tasks …", shell.SearchPlaceholder);
    }

    [Fact]
    public async Task The_Project_Search_Does_Not_Show_Tasks_And_The_Task_Search_Does_Not_Show_Projects()
    {
        await Seed();
        var shell = Shell();
        await shell.RefreshAsync();

        shell.SearchText = "alpha";
        Assert.Single(shell.Dashboard.Projects);
        Assert.Empty(shell.Dashboard.TaskResults);
        Assert.True(shell.Dashboard.ShowProjects);

        shell.ShowTaskSearchCommand.Execute(null);
        Assert.False(shell.Dashboard.ShowProjects);
        Assert.False(shell.Dashboard.ShowEmptyState);

        shell.ShowProjectSearchCommand.Execute(null);
        Assert.True(shell.Dashboard.SearchProjects);
        Assert.False(shell.Dashboard.SearchTasks);
    }

    [Fact]
    public async Task Closed_Tasks_Are_Only_Found_With_Show_Closed()
    {
        var (store, alpha, _) = await Seed();
        var done = await store.CreateTaskAsync(alpha, TaskType.todo, "Old parser work", "", [], "claude");
        await store.SetStatusAsync(done, alpha, true, "claude");

        var shell = Shell();
        await shell.RefreshAsync();
        shell.ShowTaskSearchCommand.Execute(null);
        shell.SearchText = "old parser";

        Assert.Empty(shell.Dashboard.TaskResults);
        Assert.True(shell.Dashboard.HasNoTaskMatches);

        shell.Dashboard.ShowClosed = true;
        Assert.Equal("Old parser work", Assert.Single(shell.Dashboard.TaskResults).Title);
    }

    [Fact]
    public async Task Without_Text_There_Is_A_Hint_And_No_List()
    {
        await Seed();
        var shell = Shell();
        await shell.RefreshAsync();

        shell.ShowTaskSearchCommand.Execute(null);

        Assert.True(shell.Dashboard.ShowTaskHint);
        Assert.Empty(shell.Dashboard.TaskResults);
    }

    [Fact]
    public async Task A_Result_Opens_The_Task_In_Its_Project_And_Clears_The_Search()
    {
        await Seed();
        var shell = Shell();
        await shell.RefreshAsync();
        shell.ShowTaskSearchCommand.Execute(null);

        // Found through the description only: the list of the project filters
        // by title and labels, so the search text must be gone by then.
        shell.SearchText = "throws on start";
        var row = Assert.Single(shell.Dashboard.TaskResults);

        shell.OpenTaskCommand.Execute(row);

        Assert.Equal("", shell.SearchText);
        Assert.Equal("Beta", shell.Split!.Name);
        Assert.Equal(row.Task.Id, shell.Split.TaskList.Selected!.Task.Id);
    }

    [Fact]
    public async Task A_Closed_Result_Is_Shown_By_Switching_Show_Closed_On()
    {
        var (store, alpha, _) = await Seed();
        var done = await store.CreateTaskAsync(alpha, TaskType.todo, "Shipped thing", "", [], "claude");
        await store.SetStatusAsync(done, alpha, true, "claude");

        var shell = Shell();
        await shell.RefreshAsync();
        shell.ShowTaskSearchCommand.Execute(null);
        shell.Dashboard.ShowClosed = true;
        shell.SearchText = "shipped";

        shell.OpenTaskCommand.Execute(Assert.Single(shell.Dashboard.TaskResults));

        Assert.True(shell.Split!.TaskList.ShowClosed);
        Assert.Equal(done.Id, shell.Split.TaskList.Selected!.Task.Id);
    }

    [Fact]
    public async Task The_Result_List_Is_Limited()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Big");
        for (var i = 0; i < DashboardViewModel.MaxTaskResults + 5; i++)
            await store.CreateTaskAsync(project, TaskType.todo, $"Item {i}", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();
        shell.ShowTaskSearchCommand.Execute(null);
        shell.SearchText = "item";

        Assert.Equal(DashboardViewModel.MaxTaskResults, shell.Dashboard.TaskResults.Count);
        Assert.Contains("first 200 of 205", shell.Dashboard.TaskResultsNote);
    }
}
