using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// A closed task in the list is struck through, in normal weight and in a
/// muted colour — the same look as the Android list. An open task keeps the
/// normal title.
/// </summary>
public class ClosedTaskStyleTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ClosedTaskStyleTests).Assembly);

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

    private const string ProjectId = "11111111-1111-4111-8111-111111111111";

    private static OloopTask Task(string id, string title, TaskStatus status) => new()
    {
        FormatVersion = 1,
        Id = id,
        ProjectId = ProjectId,
        Type = TaskType.todo,
        Title = title,
        Status = status,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "alice",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "alice",
        Labels = [],
    };

    [Fact]
    public void The_Icon_Of_A_Closed_Task_Is_Grey_Whatever_Its_Type()
    {
        var open = new TaskRowViewModel(Task("22222222-2222-4222-8222-222222222222", "Open", TaskStatus.open));
        var closed = new TaskRowViewModel(Task("33333333-3333-4333-8333-333333333333", "Done", TaskStatus.closed));

        Assert.Equal("TypeTodoBrush", open.ForegroundKey);
        Assert.Equal("TypeTodoBgBrush", open.BackgroundKey);
        Assert.Equal("TypeClosedBrush", closed.ForegroundKey);
        Assert.Equal("TypeClosedBgBrush", closed.BackgroundKey);
    }

    [Fact]
    public async Task A_Closed_Task_Is_Struck_Through_And_An_Open_Task_Is_Not()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            shell.Tasks.Add(Task("22222222-2222-4222-8222-222222222222", "Still open", TaskStatus.open));
            shell.Tasks.Add(Task("33333333-3333-4333-8333-333333333333", "Already done", TaskStatus.closed));

            var project = new OloopProject
            {
                FormatVersion = 1,
                Id = ProjectId,
                Name = "Example Project",
                CreatedAt = "2026-01-01T09:00:00Z",
                FolderName = "example-project",
            };

            var list = new TaskListViewModel(
                project, shell, new OloopSettings { ShowClosed = true, Grouping = "None" });

            var window = new Window { Content = new TaskListView { DataContext = list } };
            window.Show();

            var titles = Descendants<TextBlock>(window)
                .Where(t => t.Classes.Contains("TitleMedium"))
                .ToDictionary(t => t.Text ?? "");

            var open = titles["Still open"];
            var closed = titles["Already done"];

            Assert.Null(open.TextDecorations);
            Assert.Equal(FontWeight.Medium, open.FontWeight);

            var decoration = Assert.Single(closed.TextDecorations!);
            Assert.Equal(TextDecorationLocation.Strikethrough, decoration.Location);
            Assert.True(decoration.StrokeThickness >= 2);
            Assert.Equal(FontWeight.Normal, closed.FontWeight);

            window.Close();
        });
    }
}
