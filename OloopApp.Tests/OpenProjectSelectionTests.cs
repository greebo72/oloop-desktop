using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// A project that is opened from the project list starts with its first task
/// selected, so the detail pane is not empty.
/// </summary>
public class OpenProjectSelectionTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-open-" + Guid.NewGuid().ToString("N"));

    public OpenProjectSelectionTests()
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

    [Fact]
    public async Task Opening_A_Project_Selects_The_First_Task_Of_The_List()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("My Project");
        await store.CreateTaskAsync(project, TaskType.todo, "Older", "", [], "claude");
        await Task.Delay(1100);
        await store.CreateTaskAsync(project, TaskType.todo, "Newer", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());

        var list = shell.Split!.TaskList;
        var top = list.Groups.SelectMany(g => g.Rows).First().Task;

        Assert.NotNull(list.Selected);
        Assert.Equal(top.Id, list.Selected!.Task.Id);
        Assert.True(list.HasSelection);
        Assert.True(shell.Split.HasSelection);
    }

    [Fact]
    public async Task Opening_A_Project_Without_Tasks_Selects_Nothing()
    {
        var store = new OloopStore(_root);
        await store.CreateProjectAsync("Empty Project");

        var shell = Shell();
        await shell.RefreshAsync();

        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());

        Assert.Null(shell.Split!.TaskList.Selected);
        Assert.False(shell.Split.HasSelection);
    }

    [Fact]
    public async Task A_Project_With_Only_Closed_Tasks_Selects_Nothing()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Done", "", [], "claude");
        await store.SetStatusAsync(task, project, closed: true, "claude");

        var shell = Shell();
        await shell.RefreshAsync();

        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());

        // Closed tasks are hidden by default, so the list is empty.
        Assert.Null(shell.Split!.TaskList.Selected);
    }

    [Fact]
    public async Task A_Refresh_Keeps_The_Choice_Of_The_User()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("My Project");
        await store.CreateTaskAsync(project, TaskType.todo, "One", "", [], "claude");
        await store.CreateTaskAsync(project, TaskType.todo, "Two", "", [], "claude");

        var shell = Shell();
        await shell.RefreshAsync();
        shell.OpenProjectCommand.Execute(shell.Dashboard.Projects.Single());

        var list = shell.Split!.TaskList;
        var other = list.Groups.SelectMany(g => g.Rows).Last();
        list.SelectRow(other);

        await shell.RefreshAsync();

        Assert.Equal(other.Task.Id, list.Selected!.Task.Id);
    }
}
