using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Controls;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The split view of a project opens, lists the tasks of the project and
/// shows the detail of the selected task.
/// </summary>
public class SplitViewUiTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SplitViewUiTests).Assembly);

    private static void OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

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
        TaskCount = 1,
        OpenTaskCount = 1,
    };

    private static OloopTask Task() => new()
    {
        FormatVersion = 1,
        Id = "22222222-2222-4222-8222-222222222222",
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.todo,
        Title = "Write the docs",
        Status = TaskStatus.open,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "timo",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "timo",
        Labels = ["docs"],
        Description = "# Heading\n\nSome **bold** text.\n\n```csharp\nvar x = 1;\n```\n",
        Comments =
        [
            new TaskComment { Body = "First!", CreatedAt = "2026-01-01T10:00:00Z" },
        ],
    };

    [Fact]
    public void The_Split_View_Opens_The_Task_List_And_The_Detail()
    {
        OnUiThread(() =>
        {
            var shell = new MainViewModel();
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
            Assert.NotNull(shell.Split);

            window.UpdateLayout();

            // The list is there and shows the task of the project ...
            Assert.NotEmpty(Descendants<TaskListView>(window));

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("Example Project", texts);
            Assert.Contains("Write the docs", texts);

            // ... and the row carries the command that opens the detail.
            var row = Descendants<Button>(window)
                .FirstOrDefault(b => b.Command is IRelayCommand<TaskRowViewModel>);
            Assert.NotNull(row);

            row!.Command!.Execute(row.CommandParameter);
            Assert.NotNull(shell.Split!.TaskList.Selected);

            window.UpdateLayout();

            // The detail of the task is realized.
            Assert.NotEmpty(Descendants<TaskDetailView>(window));

            texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("Todo", texts);
            Assert.Contains("Open", texts);
            Assert.Contains("First!", texts);

            // The Markdown of the description is rendered, the code block included.
            Assert.NotEmpty(Descendants<CodeBlockView>(window));

            window.Close();
        });
    }
}
