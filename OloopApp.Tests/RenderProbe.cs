using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// Renders the detail of a task into a PNG, so the result can be looked at.
/// Not a test of the behaviour — the picture is the evidence.
///
/// The files land in /tmp: /tmp/detail-render.png shows a task with an image,
/// /tmp/detail-render-missing.png a task whose image is not on disk.
///
public class RenderProbe
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RenderProbe).Assembly);

    [Fact]
    public async Task Render_The_Detail_Of_A_Task()
    {
        await Session.Dispatch(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "oloop-render");
            var images = Path.Combine(root, "example-project", "images");
            Directory.CreateDirectory(images);
            Directory.CreateDirectory(Path.Combine(root, "example-project", "tasks"));

            File.Copy(
                Path.Combine(AppContext.BaseDirectory, "Assets", "oloop.png"),
                Path.Combine(images, "shot.png"),
                overwrite: true);

            var project = new OloopProject
            {
                FormatVersion = 1,
                Id = "11111111-1111-4111-8111-111111111111",
                Name = "Example Project",
                CreatedAt = "2026-01-01T09:00:00Z",
                FolderName = "example-project",
            };

            OloopTask MakeTask(string? imagePath) => new()
            {
                FormatVersion = 1,
                Id = "22222222-2222-4222-8222-222222222222",
                ProjectId = project.Id,
                Type = TaskType.bug,
                Title = "Crash on start",
                Status = TaskStatus.open,
                ImagePath = imagePath,
                CreatedAt = "2026-01-01T09:00:00Z",
                CreatedBy = "alice",
                UpdatedAt = "2026-01-01T09:00:00Z",
                UpdatedBy = "alice",
                Labels = ["ui"],
                Description = "The screenshot shows the crash.",
            };

            Render(MakeTask("images/shot.png"), "/tmp/detail-render.png");
            Render(MakeTask("images/gone.png"), "/tmp/detail-render-missing.png");
        }, CancellationToken.None);
    }

    /// <summary>Opens the task in a window and writes the window to a PNG.</summary>
    private static void Render(OloopTask task, string path)
    {
        var shell = new MainViewModel
        {
            DataRoot = Path.Combine(Path.GetTempPath(), "oloop-render"),
            Actor = "alice",
        };

        var window = new MainWindow { DataContext = shell };
        window.Width = 900;
        window.Height = 700;
        window.Show();

        var project = new OloopProject
        {
            FormatVersion = 1,
            Id = task.ProjectId,
            Name = "Example Project",
            CreatedAt = "2026-01-01T09:00:00Z",
            FolderName = "example-project",
        };

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

        var size = new PixelSize(900, 700);
        using var frame = new RenderTargetBitmap(size, new Vector(96, 96));
        frame.Render(window);
        frame.Save(path, new PngBitmapEncoderOptions());

        window.Close();
    }
}
