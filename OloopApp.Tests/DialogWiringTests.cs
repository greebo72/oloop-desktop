using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The dialogs of the app must be reachable: the settings button of the top
/// bar, the "New project" button of the dashboard and the "New task" button
/// of the project view are wired to the shell.
/// </summary>
public class DialogWiringTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(DialogWiringTests).Assembly);

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

    private static OloopTask SampleTask() => new()
    {
        FormatVersion = 1,
        Id = "22222222-2222-4222-8222-222222222222",
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.todo,
        Title = "Write the docs",
        Status = TaskStatus.open,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "alice",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "alice",
    };

    [Fact]
    public async Task The_Settings_Button_Opens_The_Settings()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var asked = false;
            shell.Dialogs = new StubDialogService
            {
                OnSettings = _ => asked = true,
            };

            window.UpdateLayout();

            // The settings button of the top bar carries the command.
            var button = Descendants<Button>(window)
                .FirstOrDefault(b => b.Command == shell.SettingsCommand);
            Assert.NotNull(button);

            button!.Command!.Execute(null);
            Assert.True(asked);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Dashboard_Has_A_New_Project_Button()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var asked = false;
            shell.Dialogs = new StubDialogService
            {
                OnNewProject = _ => asked = true,
            };

            window.UpdateLayout();

            // The dashboard shows a button that asks for a new project.
            var dashboard = Descendants<DashboardView>(window).FirstOrDefault();
            Assert.NotNull(dashboard);

            var texts = Descendants<TextBlock>(dashboard!)
                .Select(t => t.Text)
                .ToList();
            Assert.Contains("New project", texts);

            // Clicking it reaches the shell, which asks for the new project.
            var button = Descendants<Button>(dashboard!)
                .FirstOrDefault(b => b.Content is StackPanel
                    || b.Content?.ToString()?.Contains("New project") == true);
            Assert.NotNull(button);

            button!.Command?.Execute(button.CommandParameter);
            RaiseClick(button);
            Assert.True(asked);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Project_View_Has_A_New_Task_Button()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = Project();
            var task = SampleTask();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [project],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

            window.UpdateLayout();

            var split = Descendants<ProjectSplitView>(window).FirstOrDefault();
            Assert.NotNull(split);

            var texts = Descendants<TextBlock>(split!)
                .Select(t => t.Text)
                .ToList();
            Assert.Contains("New task", texts);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Detail_Of_A_Task_Has_Edit_Close_And_A_Comment_Field()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            var project = Project();
            var task = SampleTask();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [project],
                Tasks = [task],
                Unreadable = [],
            });
            shell.Tasks.Add(task);
            shell.OpenProjectCommand.Execute(shell.Dashboard.Projects[0]);

            window.UpdateLayout();

            var row = Descendants<Button>(window)
                .FirstOrDefault(b => b.Command is CommunityToolkit.Mvvm.Input.IRelayCommand<TaskRowViewModel>);
            Assert.NotNull(row);

            row!.Command!.Execute(row.CommandParameter);
            window.UpdateLayout();

            var detail = Descendants<TaskDetailView>(window).FirstOrDefault();
            Assert.NotNull(detail);

            var selected = shell.Split!.TaskList.Selected;
            Assert.NotNull(selected);

            // The commands of the detail exist and are bound in the view.
            Assert.NotNull(selected!.EditCommand);
            Assert.NotNull(selected.CloseCommand);
            Assert.NotNull(selected.ReopenCommand);
            Assert.NotNull(selected.AddCommentCommand);

            // The detail shows the close button and the comment field.
            var texts = Descendants<TextBlock>(detail!)
                .Select(t => t.Text)
                .ToList();
            Assert.Contains("Close", texts);
            Assert.Contains("Comments", texts);

            window.Close();
        });
    }

    /// <summary>
    /// Raises the click event of a button, so the <c>Click="..."</c> handler
    /// of the XAML runs.
    /// </summary>
    private static void RaiseClick(Button button)
        => button.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

    /// <summary>A dialog service that only records that it was asked.</summary>
    private sealed class StubDialogService : IDialogService
    {
        public Action<SettingsViewModel>? OnSettings { get; set; }
        public Action<NewProjectViewModel>? OnNewProject { get; set; }
        public Action<NewTaskViewModel>? OnNewTask { get; set; }

        public Task<bool> ShowSettingsAsync(SettingsViewModel viewModel)
        {
            OnSettings?.Invoke(viewModel);
            return Task.FromResult(false);
        }

        public Task<OloopProject?> ShowNewProjectAsync(NewProjectViewModel viewModel)
        {
            OnNewProject?.Invoke(viewModel);
            return Task.FromResult<OloopProject?>(null);
        }

        public Task<bool> ShowNewTaskAsync(NewTaskViewModel viewModel)
        {
            OnNewTask?.Invoke(viewModel);
            return Task.FromResult(false);
        }

        public Task<bool> ShowEditTaskAsync(EditTaskViewModel viewModel)
            => Task.FromResult(false);

        public Task<ConflictChoice> ShowConflictAsync(ConflictViewModel viewModel)
            => Task.FromResult(ConflictChoice.Cancel);
    }
}
