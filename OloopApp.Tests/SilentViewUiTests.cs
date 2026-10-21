using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The "silent since" view comes up in the window, lists the tasks of all
/// projects and opens the detail of a row.
/// </summary>
public class SilentViewUiTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SilentViewUiTests).Assembly);

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

    private static OloopProject Project(string name, string id) => new()
    {
        FormatVersion = 1,
        Id = id,
        Name = name,
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = name.ToLowerInvariant(),
    };

    private static OloopTask Task(string title, string projectId, int daysAgo) => new()
    {
        FormatVersion = 1,
        Id = Guid.NewGuid().ToString("D"),
        ProjectId = projectId,
        Type = TaskType.todo,
        Title = title,
        Status = TaskStatus.open,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "alice",
        UpdatedAt = DateTimeOffset.UtcNow.AddDays(-daysAgo)
            .ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        UpdatedBy = "alice",
    };

    [Fact]
    public async Task The_View_Comes_Up_And_Lists_The_Tasks()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var alpha = Project("Alpha", "11111111-1111-4111-8111-111111111111");
            var beta = Project("Beta", "22222222-2222-4222-8222-222222222222");

            shell.Projects.Add(alpha);
            shell.Projects.Add(beta);
            shell.Tasks.Add(Task("Quiet task", alpha.Id, 100));
            shell.Tasks.Add(Task("Fresh task", beta.Id, 1));

            shell.OpenSilentCommand.Execute(null);
            window.UpdateLayout();

            // The view is on the screen.
            Assert.NotEmpty(Descendants<SilentTasksView>(window));

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("Silent since", texts);

            // Both tasks are listed, each with the project it belongs to.
            Assert.Contains("Quiet task", texts);
            Assert.Contains("Fresh task", texts);
            Assert.Contains("Alpha", texts);
            Assert.Contains("Beta", texts);

            window.Close();
        });
    }

    [Fact]
    public async Task A_Row_Opens_The_Detail()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var alpha = Project("Alpha", "11111111-1111-4111-8111-111111111111");
            shell.Projects.Add(alpha);
            shell.Tasks.Add(Task("Quiet task", alpha.Id, 100));

            shell.OpenSilentCommand.Execute(null);
            window.UpdateLayout();

            // The row carries the command that opens the detail.
            var row = Descendants<Button>(window)
                .FirstOrDefault(b => b.Command is IRelayCommand<SilentTaskRowViewModel>);
            Assert.NotNull(row);

            row!.Command!.Execute(row.CommandParameter);
            window.UpdateLayout();

            Assert.NotNull(shell.Silent!.Selected);
            Assert.NotEmpty(Descendants<TaskDetailView>(window));

            window.Close();
        });
    }

    [Fact]
    public async Task The_View_Shows_The_Empty_State_When_Nothing_Is_Quiet()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.OpenSilentCommand.Execute(null);
            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("Nothing is silent", texts);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Button_In_The_Top_Bar_Toggles_The_View()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var toggle = Descendants<Button>(window)
                .FirstOrDefault(b => b.Command == shell.ToggleSilentCommand);
            Assert.NotNull(toggle);

            toggle!.Command!.Execute(toggle.CommandParameter);
            window.UpdateLayout();

            Assert.NotNull(shell.Silent);
            Assert.NotEmpty(Descendants<SilentTasksView>(window));

            toggle.Command.Execute(toggle.CommandParameter);
            window.UpdateLayout();

            Assert.Null(shell.Silent);

            window.Close();
        });
    }
}
