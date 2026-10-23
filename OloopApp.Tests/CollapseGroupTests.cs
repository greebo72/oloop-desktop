using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Grouping;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// Groups of the task list and of the "silent since" view can be collapsed.
/// The state lives in the view model, is remembered by the title of the group
/// and is not part of the settings.
/// </summary>
public class CollapseGroupTests
{
    private const string ProjectId = "11111111-1111-4111-8111-111111111111";

    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(CollapseGroupTests).Assembly);

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T typed)
                yield return typed;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    private static OloopProject Project() => new()
    {
        FormatVersion = 1,
        Id = ProjectId,
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = "example-project",
    };

    private static OloopTask Task(string title, TaskType type, int daysAgo = 1)
    {
        var stamp = DateTimeOffset.UtcNow.AddDays(-daysAgo)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

        return new OloopTask
        {
            FormatVersion = 1,
            Id = Guid.NewGuid().ToString("D"),
            ProjectId = ProjectId,
            ProjectName = "Example Project",
            Type = type,
            Title = title,
            Status = TaskStatus.open,
            CreatedAt = stamp,
            CreatedBy = "alice",
            UpdatedAt = stamp,
            UpdatedBy = "alice",
            Labels = [],
        };
    }

    private static OloopSettings Settings(string grouping = "Type") => new()
    {
        FilePath = Path.Combine(Path.GetTempPath(), "oloop-collapse-settings.json"),
        ShowClosed = true,
        Grouping = grouping,
    };

    /// <summary>A list with two todos and one bug, grouped by type.</summary>
    private static (MainViewModel Shell, TaskListViewModel List) TypeList()
    {
        var shell = new MainViewModel(Settings()) { DataRoot = "" };
        shell.Tasks.Add(Task("Todo one", TaskType.todo));
        shell.Tasks.Add(Task("Todo two", TaskType.todo));
        shell.Tasks.Add(Task("A bug", TaskType.bug));

        var list = new TaskListViewModel(Project(), shell, Settings());
        return (shell, list);
    }

    private static TaskGroupViewModel Group(TaskListViewModel list, string title)
        => list.Groups.Single(g => g.Title == title);

    // ---------------- The task list ----------------

    [Fact]
    public void Every_Group_Starts_Expanded()
    {
        var (_, list) = TypeList();

        Assert.Equal(2, list.Groups.Count);
        Assert.All(list.Groups, g => Assert.True(g.IsExpanded));
        Assert.Equal(3, list.Groups.Sum(g => g.Rows.Count));
    }

    [Fact]
    public void A_Collapsed_Group_Builds_No_Rows_But_Keeps_Its_Count()
    {
        var (_, list) = TypeList();
        var todos = list.Groups.First(g => g.Count == 2);
        var title = todos.Title!;

        todos.ToggleCommand.Execute(null);

        Assert.False(todos.IsExpanded);
        Assert.Empty(todos.Rows);
        Assert.Equal(2, todos.Count);
        Assert.Equal($"{title} (2)", todos.HeaderText);
        Assert.Contains("collapsed", todos.AutomationName);

        todos.ToggleCommand.Execute(null);

        Assert.True(todos.IsExpanded);
        Assert.Equal(2, todos.Rows.Count);
        Assert.Contains("expanded", todos.AutomationName);
    }

    [Fact]
    public void The_State_Survives_A_Reload_And_A_New_Task_Only_Changes_The_Count()
    {
        var (shell, list) = TypeList();
        var title = list.Groups.First(g => g.Count == 2).Title!;
        Group(list, title).IsExpanded = false;

        // What the watcher does: a task appears, the list is built again.
        shell.Tasks.Add(Task("Todo three", TaskType.todo));
        list.Refresh();

        var todos = Group(list, title);
        Assert.False(todos.IsExpanded);
        Assert.Empty(todos.Rows);
        Assert.Equal(3, todos.Count);
    }

    [Fact]
    public void The_State_Survives_A_Change_Of_The_Search()
    {
        var (shell, list) = TypeList();
        var title = list.Groups.First(g => g.Count == 2).Title!;
        Group(list, title).IsExpanded = false;

        shell.SearchText = "Todo";
        shell.SearchText = "";

        Assert.False(Group(list, title).IsExpanded);
    }

    [Fact]
    public void Another_Grouping_Resets_The_State()
    {
        var (_, list) = TypeList();
        var title = list.Groups.First(g => g.Count == 2).Title!;
        Group(list, title).IsExpanded = false;

        list.Grouping = GroupingMode.Status;
        list.Grouping = GroupingMode.Type;

        Assert.All(list.Groups, g => Assert.True(g.IsExpanded));
    }

    [Fact]
    public void Collapse_All_And_Expand_All_Work_On_Every_Group()
    {
        var (_, list) = TypeList();

        list.CollapseAllCommand.Execute(null);
        Assert.All(list.Groups, g => Assert.False(g.IsExpanded));
        Assert.Equal(0, list.Groups.Sum(g => g.Rows.Count));

        list.ExpandAllCommand.Execute(null);
        Assert.All(list.Groups, g => Assert.True(g.IsExpanded));
        Assert.Equal(3, list.Groups.Sum(g => g.Rows.Count));
    }

    [Fact]
    public void Without_A_Grouping_The_Group_Cannot_Be_Collapsed()
    {
        var shell = new MainViewModel(Settings("None")) { DataRoot = "" };
        shell.Tasks.Add(Task("Todo one", TaskType.todo));

        var list = new TaskListViewModel(Project(), shell, Settings("None"));
        var group = Assert.Single(list.Groups);

        Assert.False(list.IsGrouped);

        group.ToggleCommand.Execute(null);
        list.CollapseAllCommand.Execute(null);

        Assert.True(group.IsExpanded);
        Assert.Single(group.Rows);
    }

    [Fact]
    public void Selecting_A_Task_In_A_Collapsed_Group_Expands_The_Group()
    {
        var (shell, list) = TypeList();
        var bug = shell.Tasks.First(t => t.Type == TaskType.bug);
        list.CollapseAllCommand.Execute(null);

        var found = list.SelectTask(bug.Id);

        Assert.True(found);
        Assert.Equal(bug.Id, list.Selected!.Task.Id);
        var group = list.Groups.Single(g => g.Tasks.Any(t => t.Id == bug.Id));
        Assert.True(group.IsExpanded);
        Assert.Contains(group.Rows, r => r.Task.Id == bug.Id);

        // The other group stays collapsed.
        Assert.Contains(list.Groups, g => !g.IsExpanded);
    }

    [Fact]
    public void The_Selection_Stays_When_Its_Group_Is_Collapsed_And_On_A_Reload()
    {
        var (shell, list) = TypeList();
        var bug = shell.Tasks.First(t => t.Type == TaskType.bug);
        list.SelectTask(bug.Id);

        list.CollapseAllCommand.Execute(null);
        list.Refresh();

        Assert.NotNull(list.Selected);
        Assert.Equal(bug.Id, list.Selected!.Task.Id);
        Assert.All(list.Groups, g => Assert.False(g.IsExpanded));
    }

    [Fact]
    public void The_First_Task_Is_Selected_Even_When_The_First_Group_Is_Collapsed()
    {
        var (_, list) = TypeList();
        list.CollapseAllCommand.Execute(null);

        list.SelectFirst();

        Assert.NotNull(list.Selected);
        Assert.True(list.Groups.First().IsExpanded);
    }

    // ---------------- The silent view ----------------

    private static (MainViewModel Shell, SilentTasksViewModel View) SilentView()
    {
        var shell = new MainViewModel(Settings()) { DataRoot = "" };
        shell.Projects.Add(Project());
        shell.Tasks.Add(Task("Quiet task", TaskType.todo, daysAgo: 100));
        shell.Tasks.Add(Task("Fresh task", TaskType.todo, daysAgo: 1));
        shell.Tasks.Add(Task("Fresh bug", TaskType.bug, daysAgo: 1));

        return (shell, new SilentTasksViewModel(shell));
    }

    [Fact]
    public void A_Collapsed_Silent_Group_Keeps_Its_Count_And_Survives_A_Reload()
    {
        var (shell, view) = SilentView();
        Assert.True(view.Groups.Count >= 2);

        var biggest = view.Groups.OrderByDescending(g => g.Count).First();
        var title = biggest.Title;
        var count = biggest.Count;

        biggest.ToggleCommand.Execute(null);
        Assert.Empty(biggest.Rows);
        Assert.Equal(count, biggest.Count);

        shell.Tasks.Add(Task("Another fresh task", TaskType.todo, daysAgo: 1));
        view.Refresh();

        var again = view.Groups.Single(g => g.Title == title);
        Assert.False(again.IsExpanded);
        Assert.Empty(again.Rows);
        Assert.Equal(count + 1, again.Count);
    }

    [Fact]
    public void The_Summary_Of_The_Silent_View_Counts_Collapsed_Groups_Too()
    {
        var (_, view) = SilentView();
        var before = view.Summary;

        view.CollapseAllCommand.Execute(null);
        view.Refresh();

        Assert.Equal(before, view.Summary);
        Assert.All(view.Groups, g => Assert.False(g.IsExpanded));

        view.ExpandAllCommand.Execute(null);
        Assert.All(view.Groups, g => Assert.True(g.IsExpanded));
    }

    // ---------------- The rendered list ----------------

    [Fact]
    public async Task A_Click_On_The_Header_Hides_The_Rows_Of_The_Group()
    {
        await OnUiThread(() =>
        {
            var (_, list) = TypeList();
            var window = new Window { Content = new TaskListView { DataContext = list } };
            window.Show();

            var todos = list.Groups.First(g => g.Count == 2);

            var headers = Descendants<Button>(window)
                .Where(b => b.Classes.Contains("GroupHeader"))
                .ToList();
            Assert.Equal(2, headers.Count);

            bool RowVisible(string title) => Descendants<TextBlock>(window)
                .Any(t => t.IsVisible && t.Text == title);

            Assert.True(RowVisible("Todo one"));

            var header = headers.Single(b => ReferenceEquals(b.DataContext, todos));
            header.Command!.Execute(null);

            Assert.False(RowVisible("Todo one"));
            Assert.True(RowVisible("A bug"));

            // The header shows the count of a collapsed group.
            Assert.Contains(Descendants<TextBlock>(window), t => t.Text == todos.HeaderText);

            window.Close();
        });
    }
}
