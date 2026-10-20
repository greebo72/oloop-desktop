using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The pure logic of the task dialogs: title validation, dirty detection and
/// the normalization of the label text.
/// </summary>
public class DialogViewModelTests
{
    private static OloopProject Project() => new()
    {
        FormatVersion = 1,
        Id = "11111111-1111-4111-8111-111111111111",
        Name = "Example Project",
        CreatedAt = "2026-01-01T09:00:00Z",
    };

    private static OloopTask Task(
        string title = "Original title",
        string description = "Original description",
        IReadOnlyList<string>? labels = null) => new()
        {
            FormatVersion = 1,
            Id = "22222222-2222-4222-8222-222222222222",
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = TaskType.bug,
            Title = title,
            Status = TaskStatus.open,
            Labels = labels ?? ["ui", "backend"],
            CreatedAt = "2026-01-01T09:00:00Z",
            CreatedBy = "claude",
            UpdatedAt = "2026-01-02T09:00:00Z",
            UpdatedBy = "claude",
            Description = description,
        };

    // ---------------- New task ----------------

    [Fact]
    public void NewTask_CanSave_Is_False_For_An_Empty_Title()
    {
        var vm = new NewTaskViewModel(Project());

        Assert.False(vm.CanSave);

        vm.Title = "   ";
        Assert.False(vm.CanSave);

        vm.Title = "A real title";
        Assert.True(vm.CanSave);
    }

    [Fact]
    public void NewTask_CanSave_Is_False_For_A_Title_Longer_Than_300_Characters()
    {
        var vm = new NewTaskViewModel(Project());

        vm.Title = new string('a', 300);
        Assert.True(vm.CanSave);

        vm.Title = new string('a', 301);
        Assert.False(vm.CanSave);
        Assert.NotNull(vm.TitleError);
    }

    [Fact]
    public void NewTask_Title_Error_Is_Null_For_A_Valid_Title()
    {
        var vm = new NewTaskViewModel(Project()) { Title = "Something to do" };

        Assert.Null(vm.TitleError);
    }

    [Fact]
    public void NewTask_Has_All_Five_Types()
    {
        var vm = new NewTaskViewModel(Project());

        Assert.Equal(
            [TaskType.todo, TaskType.question, TaskType.decision, TaskType.feature, TaskType.bug],
            vm.Types);
        Assert.Equal(5, vm.TypeOptions.Count);
    }

    [Fact]
    public void NewTask_Type_Can_Be_Switched()
    {
        var vm = new NewTaskViewModel(Project());

        Assert.Equal(TaskType.todo, vm.Type);
        Assert.True(vm.TypeOptions.Single(o => o.Type == TaskType.todo).IsSelected);

        vm.TypeOptions.Single(o => o.Type == TaskType.bug).SelectCommand.Execute(null);

        Assert.Equal(TaskType.bug, vm.Type);
        Assert.True(vm.TypeOptions.Single(o => o.Type == TaskType.bug).IsSelected);
        Assert.False(vm.TypeOptions.Single(o => o.Type == TaskType.todo).IsSelected);
    }

    [Fact]
    public void NewTask_Labels_Are_Normalized_Like_The_Store_Does()
    {
        var vm = new NewTaskViewModel(Project())
        {
            LabelsText = "  ui , backend ,, ui ,  ",
        };

        Assert.Equal(["ui", "backend"], vm.Labels);
    }

    [Fact]
    public void NewTask_Empty_Labels_Yield_An_Empty_List()
    {
        var vm = new NewTaskViewModel(Project()) { LabelsText = "   " };

        Assert.Empty(vm.Labels);
    }

    [Fact]
    public void NewTask_Trimmed_Values_Are_What_The_Store_Would_Write()
    {
        var vm = new NewTaskViewModel(Project())
        {
            Title = "  Padded title  ",
            Description = "  Body  ",
        };

        Assert.Equal("Padded title", vm.TrimmedTitle);
        Assert.Equal("Body", vm.TrimmedDescription);
    }

    // ---------------- Edit task ----------------

    [Fact]
    public void EditTask_Is_Prefilled_With_The_Task()
    {
        var vm = new EditTaskViewModel(Task());

        Assert.Equal("Original title", vm.Title);
        Assert.Equal("Original description", vm.Description);
        Assert.Equal("ui, backend", vm.LabelsText);
        Assert.Equal(TaskType.bug, vm.Type);
    }

    [Fact]
    public void EditTask_IsNot_Dirty_For_The_Untouched_Task()
    {
        var vm = new EditTaskViewModel(Task());

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void EditTask_Is_Dirty_When_The_Title_Changes()
    {
        var vm = new EditTaskViewModel(Task());

        vm.Title = "Another title";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void EditTask_Is_Dirty_When_The_Description_Changes()
    {
        var vm = new EditTaskViewModel(Task());

        vm.Description = "Another description";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void EditTask_Is_Dirty_When_The_Labels_Change()
    {
        var vm = new EditTaskViewModel(Task());

        vm.LabelsText = "ui";

        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void EditTask_Is_Not_Dirty_For_Whitespace_Only_Differences()
    {
        var vm = new EditTaskViewModel(Task());

        vm.Title = "  Original title  ";
        vm.Description = "  Original description  ";
        vm.LabelsText = "ui ,  backend";

        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void EditTask_CanSave_Needs_A_Title()
    {
        var vm = new EditTaskViewModel(Task());

        Assert.True(vm.CanSave);

        vm.Title = "";
        Assert.False(vm.CanSave);

        vm.Title = "Back again";
        Assert.True(vm.CanSave);
    }

    // ---------------- Conflict ----------------

    [Fact]
    public void Conflict_Carries_The_Task_And_The_Reason()
    {
        var task = Task();
        var vm = new ConflictViewModel(task, "the task changed on disk");

        Assert.Same(task, vm.Task);
        Assert.Equal("the task changed on disk", vm.Reason);
        Assert.Equal("Original title", vm.TaskTitle);
        Assert.NotEmpty(vm.Message);
    }
}
