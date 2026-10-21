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
/// A task can carry an image. The path in the file is relative to the project
/// folder, so the detail has to find the file there — and a task without an
/// image shows nothing at all.
/// </summary>
public class ImageTests : IDisposable
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ImageTests).Assembly);

    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "oloop-image-" + Guid.NewGuid().ToString("N"));

    private readonly string _root;

    public ImageTests()
    {
        Directory.CreateDirectory(_folder);
        _root = Path.Combine(_folder, "root");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

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
        Id = "11111111-1111-4111-8111-111111111111",
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = "example-project",
    };

    private static OloopTask Task(string? imagePath = null) => new()
    {
        FormatVersion = 1,
        Id = "22222222-2222-4222-8222-222222222222",
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.todo,
        Title = "Write the docs",
        Status = TaskStatus.open,
        ImagePath = imagePath,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "alice",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "alice",
    };

    /// <summary>Writes a real PNG into the images folder of the project.</summary>
    private string WriteImage(string name)
    {
        var folder = Path.Combine(_root, "example-project", "images");
        Directory.CreateDirectory(folder);

        var path = Path.Combine(folder, name);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "Assets", "oloop.png"),
            path,
            overwrite: true);
        return path;
    }

    private MainViewModel Shell()
        => new() { DataRoot = _root, Actor = "claude" };

    // ---------------- The path ----------------

    [Fact]
    public void The_Image_Path_Is_Relative_To_The_Project_Folder()
    {
        var store = new OloopStore(_root);
        Directory.CreateDirectory(Path.Combine(_root, "example-project"));

        var path = store.ResolveImagePath(Project(), "images/abc.png");

        Assert.Equal(
            Path.Combine(_root, "example-project", "images", "abc.png"),
            path);
    }

    [Fact]
    public void A_Path_That_Leaves_The_Project_Is_Refused()
    {
        var store = new OloopStore(_root);
        Directory.CreateDirectory(Path.Combine(_root, "example-project"));

        // The path comes from a file on disk — it must not reach the rest
        // of the disk.
        Assert.Null(store.ResolveImagePath(Project(), "../../../etc/passwd"));
        Assert.Null(store.ResolveImagePath(Project(), "/etc/passwd"));
    }

    [Fact]
    public void A_Task_Without_An_Image_Path_Has_No_Image()
    {
        var shell = Shell();

        var detail = new TaskDetailViewModel(Task(), Project(), shell);

        Assert.False(detail.HasImage);
        Assert.False(detail.HasImageError);
        Assert.Null(detail.Image);
    }

    // ---------------- The image ----------------

    [Fact]
    public async Task The_Image_Of_The_Task_Is_Shown()
    {
        // Reading a bitmap needs a running platform — the test runs on the
        // UI thread, like the app does.
        await OnUiThread(() =>
        {
            WriteImage("22222222-2222-4222-8222-222222222222.png");

            var shell = Shell();
            var detail = new TaskDetailViewModel(
                Task("images/22222222-2222-4222-8222-222222222222.png"),
                Project(), shell);

            Assert.True(detail.HasImage);
            Assert.False(detail.HasImageError);
            Assert.NotNull(detail.Image);
            Assert.True(detail.Image!.PixelSize.Width > 0);
        });
    }

    [Fact]
    public void A_Missing_Image_Is_Reported()
    {
        var shell = Shell();
        var detail = new TaskDetailViewModel(
            Task("images/gone.png"), Project(), shell);

        Assert.False(detail.HasImage);
        Assert.True(detail.HasImageError);
        Assert.Equal("The image of the task is missing.", detail.ImageError);
    }

    [Fact]
    public void A_Path_Outside_The_Project_Is_Reported()
    {
        var shell = Shell();
        var detail = new TaskDetailViewModel(
            Task("../../../etc/passwd"), Project(), shell);

        Assert.False(detail.HasImage);
        Assert.True(detail.HasImageError);
        Assert.Equal("The image path of the task is not valid.", detail.ImageError);
    }

    // ---------------- The view ----------------

    [Fact]
    public async Task The_Image_Of_The_Task_Is_Shown_In_The_View()
    {
        await OnUiThread(() =>
        {
            WriteImage("22222222-2222-4222-8222-222222222222.png");

            var shell = Shell();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = Project();
            var task = Task("images/22222222-2222-4222-8222-222222222222.png");

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [project],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);
            shell.Split!.TaskList.SelectRow(shell.Split.TaskList.Groups[0].Rows[0]);

            window.UpdateLayout();

            var detail = Descendants<TaskDetailView>(window).FirstOrDefault();
            Assert.NotNull(detail);

            // The image is really in the tree, not just in the view model.
            var image = Descendants<Image>(detail!).FirstOrDefault();
            Assert.NotNull(image);
            Assert.NotNull(image!.Source);

            window.Close();
        });
    }

    [Fact]
    public async Task A_Task_Without_An_Image_Shows_No_Image()
    {
        await OnUiThread(() =>
        {
            var shell = Shell();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = Project();
            var task = Task();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [project],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);
            shell.Split!.TaskList.SelectRow(shell.Split.TaskList.Groups[0].Rows[0]);

            window.UpdateLayout();

            var detail = Descendants<TaskDetailView>(window).FirstOrDefault();
            Assert.NotNull(detail);

            // The image control is in the tree, but it is hidden — a hidden
            // control keeps its content, so the visibility is what counts.
            var image = Descendants<Image>(detail!).FirstOrDefault();
            Assert.NotNull(image);
            Assert.False(image!.IsEffectivelyVisible);

            window.Close();
        });
    }
}
