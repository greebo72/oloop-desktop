using OloopApp.Core.Filters;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The label filter of the task search: the list of labels is built from the
/// loaded tasks, labels are compared without regard to case, and a task
/// matches when it carries at least one picked label.
/// </summary>
public class LabelFilterTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-labelfilter-" + Guid.NewGuid().ToString("N"));

    public LabelFilterTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static OloopTask Task(string id, string[] labels, TaskStatus status = TaskStatus.open)
        => new()
        {
            FormatVersion = 1,
            Id = id,
            ProjectId = "p",
            Type = TaskType.todo,
            Title = id,
            Status = status,
            Labels = labels,
            CreatedAt = "2026-01-01T00:00:00Z",
            CreatedBy = "a",
            UpdatedAt = "2026-01-01T00:00:00Z",
            UpdatedBy = "a",
        };

    // ---------------- The index ----------------

    [Fact]
    public void Spellings_Are_One_Label_With_The_Most_Common_Spelling()
    {
        var index = LabelIndex.Build(
        [
            Task("1", ["bug"]),
            Task("2", ["Bug"]),
            Task("3", ["bug"]),
            Task("4", ["BUG", "ui"]),
        ]);

        Assert.Equal([new LabelEntry("bug", 4), new LabelEntry("ui", 1)], index);
    }

    [Fact]
    public void On_A_Tie_The_Alphabetically_First_Spelling_Is_Shown()
    {
        var index = LabelIndex.Build([Task("1", ["bug"]), Task("2", ["Bug"])]);

        // Ordinal: "Bug" sorts before "bug".
        Assert.Equal([new LabelEntry("Bug", 2)], index);
    }

    [Fact]
    public void A_Task_Counts_Once_Per_Label_Even_With_Two_Spellings()
    {
        var index = LabelIndex.Build([Task("1", ["bug", "Bug"])]);

        Assert.Equal(1, Assert.Single(index).Count);
    }

    [Fact]
    public void The_List_Is_Sorted_Alphabetically_Without_Regard_To_Case()
    {
        var index = LabelIndex.Build([Task("1", ["zeta", "Alpha", "beta"])]);

        Assert.Equal(["Alpha", "beta", "zeta"], index.Select(e => e.Name).ToArray());
    }

    [Fact]
    public void The_Comparison_Does_Not_Depend_On_The_Culture()
    {
        // In a Turkish culture "I".ToLower() is a dotless i. The ordinal
        // comparison must not care.
        var old = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("tr-TR");
            var index = LabelIndex.Build([Task("1", ["ISSUE"]), Task("2", ["issue"])]);

            Assert.Equal(2, Assert.Single(index).Count);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void The_Filter_Is_Or_And_Ignores_Case()
    {
        var tasks = new[]
        {
            Task("a", ["Bug"]),
            Task("b", ["ui"]),
            Task("c", ["docs"]),
            Task("d", []),
        };

        var found = new TaskFilter { AnyLabel = ["bug", "UI"] }.Apply(tasks).Select(t => t.Id).ToArray();

        Assert.Equal(["a", "b"], found);
    }

    // ---------------- On the dashboard ----------------

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

    private async Task<(MainViewModel shell, OloopStore store, OloopProject project)> Seeded()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Alpha");
        await store.CreateTaskAsync(project, TaskType.bug, "one", "", ["bug", "ui"], "claude");
        await store.CreateTaskAsync(project, TaskType.todo, "two", "", ["Bug"], "claude");
        await store.CreateTaskAsync(project, TaskType.todo, "three", "", ["docs"], "claude");
        await store.CreateTaskAsync(project, TaskType.todo, "four", "", [], "claude");
        var closed = await store.CreateTaskAsync(project, TaskType.todo, "five", "", ["legacy"], "claude");
        await store.SetStatusAsync(closed, project, true, "claude");

        var shell = Shell();
        await shell.RefreshAsync();
        shell.ShowTaskSearchCommand.Execute(null);
        return (shell, store, project);
    }

    private static void Pick(MainViewModel shell, string name, bool on = true)
        => shell.Dashboard.LabelOptions.Single(o => string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)).IsSelected = on;

    private static string[] Titles(MainViewModel shell)
        => shell.Dashboard.TaskResults.Select(r => r.Title).Order().ToArray();

    [Fact]
    public async Task The_List_Comes_From_The_Loaded_Tasks_With_Counts_And_Hides_Closed_Ones()
    {
        var (shell, _, _) = await Seeded();

        var options = shell.Dashboard.LabelOptions;
        // "bug" and "Bug" occur once each: the alphabetically first spelling wins.
        Assert.Equal(["Bug", "docs", "ui"], options.Select(o => o.Name).ToArray());
        Assert.Equal(2, options.Single(o => o.Name == "Bug").Count);

        shell.Dashboard.ShowClosed = true;
        Assert.Contains("legacy", shell.Dashboard.LabelOptions.Select(o => o.Name));
    }

    [Fact]
    public async Task A_Picked_Label_Lists_Tasks_Without_Search_Text_And_Several_Labels_Are_Or()
    {
        var (shell, _, _) = await Seeded();
        Assert.True(shell.Dashboard.ShowTaskHint);

        Pick(shell, "bug");
        Assert.False(shell.Dashboard.ShowTaskHint);
        Assert.Equal(["one", "two"], Titles(shell));

        Pick(shell, "docs");
        Assert.Equal(["one", "three", "two"], Titles(shell));
        Assert.Equal("Labels (2)", shell.Dashboard.LabelFilterText);
    }

    [Fact]
    public async Task The_Labels_Combine_With_Search_Text_And_Due_Filter()
    {
        var (shell, _, _) = await Seeded();

        Pick(shell, "bug");
        shell.SearchText = "tw";
        Assert.Equal(["two"], Titles(shell));

        shell.SearchText = "";
        shell.Dashboard.SelectedDueFilter =
            shell.Dashboard.DueFilterOptions.Single(o => o.Filter == DueFilter.WithDate);
        Assert.Empty(Titles(shell));
    }

    [Fact]
    public async Task A_Label_That_Disappears_Leaves_The_Filter()
    {
        var (shell, store, project) = await Seeded();
        shell.Dashboard.ShowClosed = true;
        Pick(shell, "legacy");
        Pick(shell, "docs");
        Assert.Equal(2, shell.Dashboard.SelectedLabels.Count);

        // "Show closed" off: the closed task is out of the scope, so is its label.
        shell.Dashboard.ShowClosed = false;
        Assert.Equal(["docs"], shell.Dashboard.SelectedLabels.Select(l => l.Name).ToArray());

        // The last task with a label is gone (here: closed) after a reload.
        var three = (await store.LoadAsync()).Tasks.Single(t => t.Title == "three");
        await store.SetStatusAsync(three, project, true, "claude");
        await shell.RefreshAsync();
        Assert.Empty(shell.Dashboard.SelectedLabels);
        Assert.DoesNotContain("docs", shell.Dashboard.LabelOptions.Select(o => o.Name));
    }

    [Fact]
    public async Task A_New_Label_Shows_Up_After_A_Reload()
    {
        var (shell, store, project) = await Seeded();

        await store.CreateTaskAsync(project, TaskType.todo, "six", "", ["brand-new"], "claude");
        await shell.RefreshAsync();

        Assert.Contains("brand-new", shell.Dashboard.LabelOptions.Select(o => o.Name));
    }

    [Fact]
    public async Task Labels_Can_Be_Removed_One_By_One_Or_All_At_Once()
    {
        var (shell, _, _) = await Seeded();
        Pick(shell, "bug");
        Pick(shell, "docs");

        shell.Dashboard.RemoveLabelCommand.Execute(shell.Dashboard.SelectedLabels.First());
        Assert.Equal(["docs"], shell.Dashboard.SelectedLabels.Select(l => l.Name).ToArray());

        shell.Dashboard.ClearLabelsCommand.Execute(null);
        Assert.False(shell.Dashboard.HasSelectedLabels);
        Assert.Equal("Labels", shell.Dashboard.LabelFilterText);
        Assert.True(shell.Dashboard.ShowTaskHint);
    }

    [Fact]
    public async Task The_Filter_Starts_Empty_On_Every_Launch()
    {
        var (shell, _, _) = await Seeded();
        Pick(shell, "bug");

        var fresh = Shell();
        await fresh.RefreshAsync();

        Assert.False(fresh.Dashboard.HasSelectedLabels);
    }
}
