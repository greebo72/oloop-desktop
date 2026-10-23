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
/// The project card of the dashboard: the open tasks by type, the overdue
/// ones, the progress and the reference. The whole card is the button that
/// opens the project.
/// </summary>
public class ProjectCardTests
{
    private const string ProjectId = "11111111-1111-4111-8111-111111111111";
    private static readonly DateTime Today = new(2026, 10, 23);

    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ProjectCardTests).Assembly);

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

    private static OloopProject Project(string? path = null, string? host = null, int total = 0, int open = 0) => new()
    {
        FormatVersion = 1,
        Id = ProjectId,
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
        FolderName = "example-project",
        ReferencePath = path,
        ReferenceHost = host,
        TaskCount = total,
        OpenTaskCount = open,
    };

    private static OloopTask Task(
        TaskType type,
        string? due = null,
        TaskStatus status = TaskStatus.open,
        string projectId = ProjectId) => new()
    {
        FormatVersion = 1,
        Id = Guid.NewGuid().ToString("D"),
        ProjectId = projectId,
        ProjectName = "Example Project",
        Type = type,
        Title = "A " + type,
        Status = status,
        DueDate = due,
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "alice",
        UpdatedAt = "2026-01-01T09:00:00Z",
        UpdatedBy = "alice",
        Labels = [],
    };

    [Fact]
    public void The_Open_Tasks_Are_Counted_By_Type_In_The_Order_Of_The_Types()
    {
        var card = new ProjectCardViewModel { Project = Project() };

        card.ApplyTasks(
        [
            Task(TaskType.bug),
            Task(TaskType.todo),
            Task(TaskType.bug),
            Task(TaskType.todo),
            Task(TaskType.todo),
        ], Today);

        Assert.Equal(["3 Todo", "2 Bug"], card.OpenByType.Select(c => c.Text));
        Assert.True(card.HasOpenByType);
    }

    [Fact]
    public void A_Closed_Task_Is_Not_Counted_As_Open()
    {
        var card = new ProjectCardViewModel { Project = Project() };

        card.ApplyTasks([Task(TaskType.bug, status: TaskStatus.closed)], Today);

        Assert.Empty(card.OpenByType);
        Assert.False(card.HasOpenByType);
    }

    [Fact]
    public void Only_Open_Tasks_Past_Their_Due_Date_Are_Overdue()
    {
        var card = new ProjectCardViewModel { Project = Project() };

        card.ApplyTasks(
        [
            Task(TaskType.todo, due: "2026-10-22"),                              // overdue
            Task(TaskType.todo, due: "2026-10-01"),                              // overdue
            Task(TaskType.todo, due: "2026-10-23"),                              // due today
            Task(TaskType.todo, due: "2026-11-01"),                              // later
            Task(TaskType.todo),                                                 // no date
            Task(TaskType.todo, due: "2026-09-01", status: TaskStatus.closed),   // closed
        ], Today);

        Assert.Equal(2, card.OverdueCount);
        Assert.True(card.HasOverdue);
        Assert.Equal("2 overdue", card.OverdueText);
    }

    [Fact]
    public void A_Project_Without_Tasks_Shows_No_Chips()
    {
        var card = new ProjectCardViewModel { Project = Project() };

        card.ApplyTasks([], Today);

        Assert.False(card.HasOpenByType);
        Assert.False(card.HasOverdue);
        Assert.False(card.HasTasks);
        Assert.Equal(0, card.DoneRatio);
    }

    [Fact]
    public void The_Progress_Is_The_Share_Of_Closed_Tasks()
    {
        var card = new ProjectCardViewModel { Project = Project(total: 4, open: 1) };

        Assert.True(card.HasTasks);
        Assert.Equal(0.75, card.DoneRatio, 3);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "host", null)]
    [InlineData("/dev/repo", null, "/dev/repo")]
    [InlineData("/dev/repo", "", "/dev/repo")]
    [InlineData("/dev/repo", "Mac", "Mac:/dev/repo")]
    public void The_Reference_Is_Written_Like_The_Cli_Prints_It(string? path, string? host, string? expected)
    {
        var card = new ProjectCardViewModel { Project = Project(path, host) };

        Assert.Equal(expected, card.ReferenceText);
        Assert.Equal(expected is not null, card.HasReference);
    }

    [Fact]
    public void The_Dashboard_Hands_Each_Card_The_Tasks_Of_Its_Own_Project()
    {
        const string otherId = "22222222-2222-4222-8222-222222222222";
        var other = Project() is var p
            ? new OloopProject
            {
                FormatVersion = 1, Id = otherId, Name = "Other", CreatedAt = p.CreatedAt, FolderName = "other",
            }
            : throw new InvalidOperationException();

        var shell = new MainViewModel();
        shell.Dashboard.Update(new OloopSnapshot
        {
            Projects = [Project(), other],
            Tasks =
            [
                Task(TaskType.bug),
                Task(TaskType.bug),
                Task(TaskType.question, projectId: otherId),
            ],
            Unreadable = [],
        });

        var first = shell.Dashboard.Projects.Single(c => c.Name == "Example Project");
        var second = shell.Dashboard.Projects.Single(c => c.Name == "Other");

        Assert.Equal(["2 Bug"], first.OpenByType.Select(c => c.Text));
        Assert.Equal(["1 Question"], second.OpenByType.Select(c => c.Text));
    }

    [Fact]
    public async Task The_Whole_Card_Is_Clickable_And_The_Chips_Are_Rendered()
    {
        await OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell, Width = 1180, Height = 640 };
            window.Show();

            shell.Dashboard.HasNoDataRoot = false;
            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects = [Project("/dev/example", "Mac", total: 3, open: 2)],
                Tasks =
                [
                    Task(TaskType.bug, due: "2000-01-01"),
                    Task(TaskType.feature),
                    Task(TaskType.todo, status: TaskStatus.closed),
                ],
                Unreadable = [],
            });
            window.UpdateLayout();

            var card = Descendants<Border>(window)
                .First(b => b.Classes.Contains("Card") && b.DataContext is ProjectCardViewModel);
            var button = Descendants<Button>(card).First();

            // The button fills the card — before, only its content was
            // clickable and the hover covered the front of the card.
            Assert.True(button.Bounds.Width >= card.Bounds.Width - 1,
                $"button {button.Bounds.Width} px, card {card.Bounds.Width} px");

            // The progress bar stays inside the padding of the card.
            var bar = Descendants<ProgressBar>(card).Single();
            var right = bar.TranslatePoint(new Point(bar.Bounds.Width, 0), card)!.Value.X;
            Assert.True(right <= card.Bounds.Width - 12 + 1,
                $"the bar ends at {right} px, the card is {card.Bounds.Width} px wide");

            var texts = Descendants<TextBlock>(card).Where(t => t.IsVisible).Select(t => t.Text).ToList();
            Assert.Contains("1 Bug", texts);
            Assert.Contains("1 Feature", texts);
            Assert.Contains("1 overdue", texts);
            Assert.Contains("2 open of 3", texts);
            Assert.Contains("Mac:/dev/example", texts);

            window.Close();
        });
    }
}
