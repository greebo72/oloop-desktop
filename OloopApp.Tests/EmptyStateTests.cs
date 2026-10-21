using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The empty states of the app: no data folder, no project, no project that
/// matches the search, no task, no task that matches the search and no
/// comment.
///
/// Every empty state has to say what happened and what to do about it — a
/// blank pane is not an explanation.
/// </summary>
public class EmptyStateTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(EmptyStateTests).Assembly);

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

    /// <summary>
    /// The texts of the elements that are actually visible. A hidden element
    /// is still part of the visual tree and still has its text — so a test
    /// that only looks for a text does not tell whether the user sees it.
    /// </summary>
    private static List<string> VisibleTexts(Visual root)
        => Descendants<TextBlock>(root)
            .Where(t => t.IsVisible)
            .Select(t => t.Text ?? "")
            .ToList();

    /// <summary>
    /// Finds a control by its name in the visual tree. The name of a control
    /// inside a user control is not in the name scope of the window, so
    /// <c>FindControl</c> does not find it.
    /// </summary>
    private static T? FindByName<T>(Visual root, string name) where T : Visual
        => Descendants<T>(root).FirstOrDefault(c => c.Name == name);

    private static OloopProject Project(string name = "Example Project") => new()
    {
        FormatVersion = 1,
        Id = "11111111-1111-4111-8111-111111111111",
        Name = name,
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = "example-project",
    };

    private static OloopTask Task(string title = "Write the docs") => new()
    {
        FormatVersion = 1,
        Id = "22222222-2222-4222-8222-222222222222",
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.todo,
        Title = title,
        Status = TaskStatus.open,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "timo",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "timo",
        Labels = [],
    };

    // ---------------- The dashboard ----------------

    [Fact]
    public void Without_A_Data_Folder_The_Dashboard_Points_At_The_Settings()
    {
        var shell = new MainViewModel { DataRoot = "", Actor = "timo" };

        Assert.True(shell.Dashboard.HasNoDataRoot);
        Assert.Equal("No data folder", shell.Dashboard.EmptyTitle);
        Assert.Contains("settings", shell.Dashboard.EmptyText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_Missing_Data_Folder_Is_An_Empty_State_And_Not_A_Crash()
    {
        await OnUiThread(() =>
        {
            var missing = Path.Combine(Path.GetTempPath(), "oloop-missing-" + Guid.NewGuid().ToString("N"));
            var shell = new MainViewModel { DataRoot = missing, Actor = "timo" };

            shell.RefreshAsync().GetAwaiter().GetResult();

            Assert.True(shell.Dashboard.HasNoDataRoot);
            Assert.True(shell.Dashboard.ShowEmptyState);
            Assert.Equal("No data folder", shell.Dashboard.EmptyTitle);
            Assert.Empty(shell.Dashboard.Projects);
        });
    }

    [Fact]
    public void An_Empty_Data_Folder_Says_That_There_Is_No_Project_Yet()
    {
        var shell = new MainViewModel();
        shell.Dashboard.HasNoDataRoot = false;
        shell.Dashboard.Update(OloopSnapshot.Empty);

        Assert.True(shell.Dashboard.IsEmpty);
        Assert.True(shell.Dashboard.ShowEmptyState);
        Assert.False(shell.Dashboard.HasNoMatches);
        Assert.Equal("No projects yet", shell.Dashboard.EmptyTitle);
        Assert.Contains("Create a project", shell.Dashboard.EmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Search_That_Hides_Every_Project_Says_So()
    {
        var shell = new MainViewModel();
        shell.Dashboard.HasNoDataRoot = false;
        shell.Dashboard.Update(new OloopSnapshot
        {
            Projects = [Project()],
            Tasks = [],
            Unreadable = [],
        });

        Assert.False(shell.Dashboard.ShowEmptyState);

        shell.Dashboard.SearchText = "nothing matches this";

        // The project list is not empty — the search hides it.
        Assert.False(shell.Dashboard.IsEmpty);
        Assert.True(shell.Dashboard.HasNoMatches);
        Assert.True(shell.Dashboard.ShowEmptyState);
        Assert.Equal("Nothing matches", shell.Dashboard.EmptyTitle);
        Assert.Contains("Clear the search", shell.Dashboard.EmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_Search_Filters_The_Project_Cards()
    {
        var shell = new MainViewModel();
        shell.Dashboard.HasNoDataRoot = false;
        shell.Dashboard.Update(new OloopSnapshot
        {
            Projects = [Project("Alpha"), Project("Beta")],
            Tasks = [],
            Unreadable = [],
        });

        Assert.Equal(2, shell.Dashboard.Projects.Count);

        shell.Dashboard.SearchText = "bet";

        Assert.Single(shell.Dashboard.Projects);
        Assert.Equal("Beta", shell.Dashboard.Projects[0].Name);
    }

    [Fact]
    public async Task The_Empty_State_Of_The_Dashboard_Is_Rendered()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.HasNoDataRoot = false;
            shell.Dashboard.Update(OloopSnapshot.Empty);

            window.UpdateLayout();

            var texts = VisibleTexts(window);
            Assert.Contains("No projects yet", texts);
            Assert.Contains("Create a project to get a folder in the data folder. Tasks live in that folder.", texts);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Empty_Card_Of_The_Dashboard_Is_Visible()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.HasNoDataRoot = false;
            shell.Dashboard.Update(OloopSnapshot.Empty);

            window.UpdateLayout();

            var card = FindByName<Border>(window, "EmptyStateCard");
            Assert.NotNull(card);
            Assert.True(card!.IsVisible, "the empty card is not shown");

            window.Close();
        });
    }

    [Fact]
    public async Task The_Empty_Card_Of_The_Dashboard_Hides_As_Soon_As_There_Is_A_Project()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.HasNoDataRoot = false;
            shell.Dashboard.Update(OloopSnapshot.Empty);
            window.UpdateLayout();

            var card = FindByName<Border>(window, "EmptyStateCard");
            Assert.True(card!.IsVisible);

            // A project arrives — the empty card has to go.
            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [],
                Unreadable = [],
            });
            window.UpdateLayout();

            Assert.False(card.IsVisible, "the empty card is still shown next to the project");

            window.Close();
        });
    }

    [Fact]
    public async Task The_Empty_Card_Of_The_Dashboard_Appears_When_The_Search_Hides_Every_Project()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.HasNoDataRoot = false;
            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [],
                Unreadable = [],
            });
            window.UpdateLayout();

            var card = FindByName<Border>(window, "EmptyStateCard");
            Assert.False(card!.IsVisible);

            shell.Dashboard.SearchText = "nothing matches this";
            window.UpdateLayout();

            Assert.True(card.IsVisible, "the empty card does not appear");
            Assert.Contains("Nothing matches", VisibleTexts(window));

            window.Close();
        });
    }

    [Fact]
    public async Task The_Empty_Card_Says_No_Data_Folder_When_There_Is_None()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { DataRoot = "", Actor = "timo" };
            var window = new MainWindow { DataContext = shell };
            window.Show();

            window.UpdateLayout();

            var card = FindByName<Border>(window, "EmptyStateCard");
            Assert.True(card!.IsVisible, "the empty card is not shown");

            var texts = VisibleTexts(window);
            Assert.Contains("No data folder", texts);
            Assert.Contains("Open the settings and choose the folder that holds your oloop projects.", texts);

            // The status line would say the same thing a second time.
            Assert.Equal("", shell.StatusMessage);

            window.Close();
        });
    }

    [Fact]
    public async Task Without_A_Data_Folder_There_Is_No_New_Project_Button()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { DataRoot = "", Actor = "timo" };
            var window = new MainWindow { DataContext = shell };
            window.Show();

            window.UpdateLayout();

            // There is no folder to put a project in yet — the button would
            // only lead to an error.
            var labels = Descendants<Button>(window)
                .Where(b => b.IsVisible)
                .SelectMany(b => Descendants<TextBlock>(b))
                .Select(t => t.Text ?? "")
                .ToList();

            Assert.DoesNotContain("New project", labels);
            Assert.Contains("Open settings", labels);

            window.Close();
        });
    }

    // ---------------- The task list ----------------

    [Fact]
    public void An_Empty_Project_Says_That_It_Has_No_Tasks()
    {
        var shell = new MainViewModel();
        var list = new TaskListViewModel(
            Project(), shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

        Assert.True(list.IsEmpty);
        Assert.True(list.HasNoTasks);
        Assert.Equal("No tasks yet", list.EmptyTitle);
        Assert.Contains("Create one", list.EmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Filter_That_Hides_Every_Task_Says_So()
    {
        var shell = new MainViewModel();
        shell.Tasks.Add(Task("Write the docs"));

        var list = new TaskListViewModel(
            Project(), shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

        Assert.False(list.IsEmpty);

        // The search hides the only task of the project.
        shell.SearchText = "nothing matches this";

        Assert.True(list.IsEmpty);
        Assert.False(list.HasNoTasks);
        Assert.Equal("Nothing matches", list.EmptyTitle);
        Assert.Contains("hide every task", list.EmptyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_Empty_State_Of_The_Task_List_Is_Rendered()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [],
                Unreadable = [],
            });

            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);
            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("No tasks yet", texts);
            Assert.Contains("This project has no tasks yet. Create one to get started.", texts);

            window.Close();
        });
    }

    // ---------------- The comments ----------------

    [Fact]
    public void A_Task_Without_Comments_Says_So()
    {
        var shell = new MainViewModel();
        var task = Task();

        var detail = new TaskDetailViewModel(task, Project(), shell);

        Assert.False(detail.HasComments);
    }

    [Fact]
    public async Task The_Empty_State_Of_The_Comments_Is_Rendered()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var task = Task();
            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);

            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);
            shell.Split!.TaskList.SelectRow(shell.Split.TaskList.Groups[0].Rows[0]);
            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("No comments yet", texts);
            Assert.Contains("Write the first comment to note a decision or a next step.", texts);

            window.Close();
        });
    }

    // ---------------- The selection ----------------

    [Fact]
    public async Task The_Detail_Pane_Says_That_A_Task_Has_To_Be_Picked()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project()],
                Tasks = [],
                Unreadable = [],
            });

            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);
            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("Select a task", texts);
            Assert.Contains("Pick a task from the list to see its details.", texts);

            window.Close();
        });
    }
}
