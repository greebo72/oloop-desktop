using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The files that violate the format are not silently swallowed: they come
/// back with the reason why, and the dashboard shows them.
///
/// A file that cannot be read is never written to either.
/// </summary>
public class UnreadableFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-unreadable-" + Guid.NewGuid().ToString("N"));

    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UnreadableFileTests).Assembly);

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

    public UnreadableFileTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    /// <summary>
    /// Writes a project and a task file that breaks the format: the title is
    /// missing.
    /// </summary>
    private async Task<string> WriteBrokenTaskAsync()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Broken");
        var id = "33333333-3333-4333-8333-333333333333";

        var folder = store.ProjectFolder(project);
        var tasksFolder = Path.Combine(folder, "tasks");
        Directory.CreateDirectory(tasksFolder);

        var path = Path.Combine(tasksFolder, id + ".md");
        await File.WriteAllTextAsync(path, $"""
            ---
            formatVersion: 1
            id: {id}
            projectId: {project.Id}
            type: todo
            status: open
            labels: []
            createdAt: '2026-01-01T09:00:00Z'
            createdBy: timo
            updatedAt: '2026-01-01T09:00:00Z'
            updatedBy: timo
            comments: []
            ---

            no title at all
            """);

        return path;
    }

    [Fact]
    public async Task A_Broken_File_Comes_Back_With_The_Reason()
    {
        var path = await WriteBrokenTaskAsync();

        var store = new OloopStore(_root);
        var snapshot = await store.LoadAsync();

        var unreadable = Assert.Single(snapshot.Unreadable);
        Assert.Equal(path, unreadable.Path);
        Assert.Contains("title", unreadable.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_Broken_File_Is_Never_Written_To()
    {
        var path = await WriteBrokenTaskAsync();
        var before = await File.ReadAllTextAsync(path);

        var store = new OloopStore(_root);
        var snapshot = await store.LoadAsync();

        Assert.NotEmpty(snapshot.Unreadable);

        // The scan does not touch the file.
        var after = await File.ReadAllTextAsync(path);
        Assert.Equal(before, after);

        // And there is no backup of it either — nothing was written.
        var backup = Path.Combine(Path.GetDirectoryName(path)!, ".backup", Path.GetFileName(path) + ".bak");
        Assert.False(File.Exists(backup));
    }

    [Fact]
    public async Task The_Dashboard_Counts_The_Unreadable_Files()
    {
        await WriteBrokenTaskAsync();

        var store = new OloopStore(_root);
        var snapshot = await store.LoadAsync();

        var shell = new MainViewModel { DataRoot = _root, Actor = "timo" };
        await shell.RefreshAsync();

        Assert.Equal(1, shell.Dashboard.UnreadableCount);
        Assert.True(shell.Dashboard.HasUnreadable);
        Assert.Equal("1 file cannot be read", shell.Dashboard.UnreadableSummary);
        Assert.Single(shell.Dashboard.Unreadable);
    }

    [Fact]
    public void The_Summary_Counts_Plural()
    {
        var shell = new MainViewModel();
        shell.Dashboard.Update(new OloopSnapshot
        {
            Projects = [],
            Tasks = [],
            Unreadable =
            [
                new UnreadableFile("/a.md", "broken"),
                new UnreadableFile("/b.md", "broken"),
            ],
        });

        Assert.Equal(2, shell.Dashboard.UnreadableCount);
        Assert.Equal("2 files cannot be read", shell.Dashboard.UnreadableSummary);
    }

    [Fact]
    public void Without_Unreadable_Files_There_Is_No_Section()
    {
        var shell = new MainViewModel();
        shell.Dashboard.Update(OloopSnapshot.Empty);

        Assert.False(shell.Dashboard.HasUnreadable);
        Assert.Equal("", shell.Dashboard.UnreadableSummary);
    }

    [Fact]
    public async Task The_Unreadable_Section_Shows_The_Path_And_The_Reason()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [],
                Tasks = [],
                Unreadable = [new UnreadableFile("/data/broken/tasks/x.md", "title: required property missing")],
            });

            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("1 file cannot be read", texts);
            Assert.Contains("/data/broken/tasks/x.md", texts);
            Assert.Contains("title: required property missing", texts);

            window.Close();
        });
    }

    [Fact]
    public async Task Without_Unreadable_Files_The_Section_Is_Not_Shown()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.Update(OloopSnapshot.Empty);
            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.DoesNotContain("file cannot be read", texts);

            window.Close();
        });
    }

    // ---------------- The task list of a project ----------------

    [Fact]
    public async Task The_Task_List_Shows_The_Unreadable_Files_Of_Its_Project()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { DataRoot = "/data" };
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = new OloopProject
            {
                FormatVersion = 1,
                Id = "11111111-1111-4111-8111-111111111111",
                Name = "Broken",
                CreatedAt = "2026-01-01T09:00:00Z",
                FolderName = "broken",
            };

            var list = new TaskListViewModel(project, shell, new OloopSettings { Grouping = "None" });

            shell.Unreadable.Add(new UnreadableFile("/data/broken/tasks/x.md", "title: required property missing"));

            list.Refresh();

            Assert.True(list.HasUnreadable);
            Assert.Equal("1 file cannot be read", list.UnreadableSummary);
            Assert.Single(list.Unreadable);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Task_List_Ignores_The_Files_Of_Another_Project()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { DataRoot = "/data" };
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = new OloopProject
            {
                FormatVersion = 1,
                Id = "11111111-1111-4111-8111-111111111111",
                Name = "Mine",
                CreatedAt = "2026-01-01T09:00:00Z",
                FolderName = "mine",
            };

            var list = new TaskListViewModel(project, shell, new OloopSettings { Grouping = "None" });

            // A file of another project, and one whose folder merely starts
            // with the same letters.
            shell.Unreadable.Add(new UnreadableFile("/data/other/tasks/x.md", "broken"));
            shell.Unreadable.Add(new UnreadableFile("/data/mine-2/tasks/y.md", "broken"));

            list.Refresh();

            Assert.False(list.HasUnreadable);
            Assert.Empty(list.Unreadable);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Unreadable_Section_Of_The_Task_List_Is_Shown()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel { DataRoot = "/data" };
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = new OloopProject
            {
                FormatVersion = 1,
                Id = "11111111-1111-4111-8111-111111111111",
                Name = "Broken",
                CreatedAt = "2026-01-01T09:00:00Z",
                FolderName = "broken",
            };

            shell.Unreadable.Add(new UnreadableFile("/data/broken/tasks/x.md", "title: required property missing"));

            // The project has to be open, otherwise the task list is not on
            // the screen and there is nothing to look at.
            shell.OpenProjectCommand.Execute(new ProjectCardViewModel { Project = project });

            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("1 file cannot be read", texts);
            Assert.Contains("/data/broken/tasks/x.md", texts);

            window.Close();
        });
    }
}
