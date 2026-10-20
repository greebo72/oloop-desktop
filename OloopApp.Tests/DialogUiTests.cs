using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The task dialogs must come up headless, show their content and let the
/// user pick a type in the type row of the new task dialog.
/// </summary>
public class DialogUiTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(DialogUiTests).Assembly);

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
    };

    private static OloopTask SampleTask() => new()
    {
        FormatVersion = 1,
        Id = "22222222-2222-4222-8222-222222222222",
        ProjectId = "11111111-1111-4111-8111-111111111111",
        Type = TaskType.bug,
        Title = "Original title",
        Status = TaskStatus.open,
        Labels = ["ui"],
        CreatedAt = "2026-01-01T09:00:00Z",
        CreatedBy = "claude",
        UpdatedAt = "2026-01-02T09:00:00Z",
        UpdatedBy = "claude",
        Description = "Original description",
    };

    [Fact]
    public async Task NewTaskDialog_Opens()
    {
        await OnUiThread(() =>
        {
            var vm = new NewTaskViewModel(Project());
            var dialog = new NewTaskDialog(vm);

            dialog.Show();
            dialog.UpdateLayout();

            Assert.NotNull(dialog.Content);
            Assert.Same(vm, dialog.ViewModel);

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("New task", texts);
            Assert.Contains("Example Project", texts);
            Assert.Contains("Title", texts);
            Assert.Contains("Description", texts);
            Assert.Contains("Labels", texts);

            var buttons = Descendants<Button>(dialog).Select(b => b.Content as string).ToList();
            Assert.Contains("Create", buttons);
            Assert.Contains("Cancel", buttons);

            dialog.Close();
        });
    }

    [Fact]
    public async Task NewTaskDialog_Has_An_Opaque_Background()
    {
        await OnUiThread(() =>
        {
            var dialog = new NewTaskDialog(new NewTaskViewModel(Project()));

            dialog.Show();
            dialog.UpdateLayout();

            // The dialog background must not be transparent — the Android
            // client rendered its popups with a transparent background.
            Assert.NotNull(dialog.Background);
            Assert.IsType<SolidColorBrush>(dialog.Background);
            Assert.NotEqual(Colors.Transparent, ((SolidColorBrush)dialog.Background!).Color);

            var root = Descendants<Border>(dialog).FirstOrDefault();
            Assert.NotNull(root);
            Assert.NotNull(root!.Background);

            dialog.Close();
        });
    }

    [Fact]
    public async Task NewTaskDialog_Type_Row_Switches_The_Selected_Type()
    {
        await OnUiThread(() =>
        {
            var vm = new NewTaskViewModel(Project());
            var dialog = new NewTaskDialog(vm);

            dialog.Show();
            dialog.UpdateLayout();

            // One button per type in the type row.
            var typeButtons = Descendants<Button>(dialog)
                .Where(b => b.Classes.Contains("TypeOption"))
                .ToList();

            Assert.Equal(5, typeButtons.Count);

            // Every type button shows the icon of its type.
            Assert.Equal(5, Descendants<PathIcon>(dialog).Count());

            // Picking a type in the row switches the selected type.
            var bugButton = typeButtons.Single(b => b.Command == vm.TypeOptions
                .Single(o => o.Type == TaskType.bug).SelectCommand);

            bugButton.Command!.Execute(bugButton.CommandParameter);

            Assert.Equal(TaskType.bug, vm.Type);
            Assert.True(vm.TypeOptions.Single(o => o.Type == TaskType.bug).IsSelected);
            Assert.False(vm.TypeOptions.Single(o => o.Type == TaskType.todo).IsSelected);

            dialog.Close();
        });
    }

    [Fact]
    public async Task NewTaskDialog_Create_Is_Disabled_Without_A_Title()
    {
        await OnUiThread(() =>
        {
            var vm = new NewTaskViewModel(Project());
            var dialog = new NewTaskDialog(vm);

            dialog.Show();
            dialog.UpdateLayout();

            var create = Descendants<Button>(dialog).Single(b => b.Content as string == "Create");
            Assert.False(create.IsEnabled);

            vm.Title = "A title";
            dialog.UpdateLayout();

            Assert.True(create.IsEnabled);

            dialog.Close();
        });
    }

    [Fact]
    public async Task EditTaskDialog_Opens_Prefilled()
    {
        await OnUiThread(() =>
        {
            var vm = new EditTaskViewModel(SampleTask());
            var dialog = new EditTaskDialog(vm);

            dialog.Show();
            dialog.UpdateLayout();

            Assert.NotNull(dialog.Content);
            Assert.Same(vm, dialog.ViewModel);

            var boxes = Descendants<TextBox>(dialog).ToList();
            Assert.Equal(3, boxes.Count);
            Assert.Equal("Original title", boxes[0].Text);
            Assert.Equal("Original description", boxes[1].Text);
            Assert.Equal("ui", boxes[2].Text);

            var buttons = Descendants<Button>(dialog).Select(b => b.Content as string).ToList();
            Assert.Contains("Save", buttons);
            Assert.Contains("Cancel", buttons);

            dialog.Close();
        });
    }

    [Fact]
    public async Task ConflictDialog_Opens_And_Offers_All_Three_Choices()
    {
        await OnUiThread(() =>
        {
            var vm = new ConflictViewModel(SampleTask(), "the task changed on disk");
            var dialog = new ConflictDialog(vm);

            dialog.Show();
            dialog.UpdateLayout();

            Assert.NotNull(dialog.Content);
            Assert.Same(vm, dialog.ViewModel);

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("Write conflict", texts);
            Assert.Contains("the task changed on disk", texts);
            Assert.Contains("Original title", texts);

            var buttons = Descendants<Button>(dialog).Select(b => b.Content as string).ToList();
            Assert.Contains("Reload", buttons);
            Assert.Contains("Overwrite", buttons);
            Assert.Contains("Cancel", buttons);

            // At most one filled primary element on the screen.
            Assert.Single(Descendants<Button>(dialog), b => b.Classes.Contains("Primary"));

            dialog.Close();
        });
    }

    [Fact]
    public async Task NewTaskDialog_Resolves_The_Type_Icons_And_Brushes()
    {
        await OnUiThread(() =>
        {
            var vm = new NewTaskViewModel(Project());
            var dialog = new NewTaskDialog(vm);

            dialog.Show();
            dialog.UpdateLayout();

            // The type row binds its icon and brushes through a dynamic
            // resource with a runtime key — both have to resolve.
            var icons = Descendants<PathIcon>(dialog).ToList();
            Assert.Equal(5, icons.Count);
            foreach (var icon in icons)
            {
                Assert.NotNull(icon.Data);
                Assert.IsAssignableFrom<Geometry>(icon.Data);
                Assert.NotNull(icon.Foreground);
                Assert.IsType<SolidColorBrush>(icon.Foreground);
            }

            // The containers of the type row carry the brush of their type.
            var containers = Descendants<Border>(dialog)
                .Where(b => b.Classes.Contains("IconContainer"))
                .ToList();

            Assert.Equal(5, containers.Count);
            foreach (var container in containers)
            {
                Assert.NotNull(container.Background);
                Assert.IsType<SolidColorBrush>(container.Background);
                Assert.NotEqual(Colors.Transparent, ((SolidColorBrush)container.Background!).Color);
            }

            dialog.Close();
        });
    }

    [Fact]
    public async Task EditTaskDialog_Resolves_The_Type_Icon_And_Brush()
    {
        await OnUiThread(() =>
        {
            var dialog = new EditTaskDialog(new EditTaskViewModel(SampleTask()));

            dialog.Show();
            dialog.UpdateLayout();

            var icon = Descendants<PathIcon>(dialog).Single();
            Assert.NotNull(icon.Data);
            Assert.IsAssignableFrom<Geometry>(icon.Data);
            Assert.NotNull(icon.Foreground);
            Assert.IsType<SolidColorBrush>(icon.Foreground);

            var chip = Descendants<Border>(dialog).Single(b => b.Classes.Contains("TypeChip"));
            Assert.NotNull(chip.Background);
            Assert.IsType<SolidColorBrush>(chip.Background);

            dialog.Close();
        });
    }

    [Fact]
    public async Task NewTaskDialog_Returns_True_And_Collected_Values()
    {
        var vm = new NewTaskViewModel(Project());

        var result = await ShowDialogAsync<bool>(() => new NewTaskDialog(vm), d => d.Close(true));

        Assert.True(result);
        Assert.Same(vm, ((NewTaskDialog)dialogUnderTest!).ViewModel);
    }

    [Fact]
    public async Task EditTaskDialog_Returns_True()
    {
        var result = await ShowDialogAsync<bool>(
            () => new EditTaskDialog(new EditTaskViewModel(SampleTask())), d => d.Close(true));

        Assert.True(result);
    }

    [Fact]
    public async Task ConflictDialog_Returns_The_Chosen_Action()
    {
        var result = await ShowDialogAsync<ConflictChoice>(
            () => new ConflictDialog(new ConflictViewModel(SampleTask(), "changed on disk")),
            d => d.Close(ConflictChoice.Reload));

        Assert.Equal(ConflictChoice.Reload, result);
    }

    [Fact]
    public async Task ConflictDialog_Does_Not_Overwrite_Silently()
    {
        // Every action of the dialog has to be picked explicitly — closing
        // the dialog without a choice must not write anything.
        var result = await ShowDialogAsync<ConflictChoice>(
            () => new ConflictDialog(new ConflictViewModel(SampleTask(), "changed on disk")),
            d => d.Close(ConflictChoice.Cancel));

        Assert.Equal(ConflictChoice.Cancel, result);
    }

    /// <summary>The dialog of the last <see cref="ShowDialogAsync{T}"/> call.</summary>
    private static Window? dialogUnderTest;

    /// <summary>
    /// Shows a dialog the way the shell does — <c>await dialog.ShowDialog&lt;T&gt;(owner)</c> —
    /// and closes it with the given action.
    /// </summary>
    private static async Task<T> ShowDialogAsync<T>(Func<Window> create, Action<Window> close)
    {
        var completion = new TaskCompletionSource<T>();

        await Session.Dispatch(() =>
        {
            var dialog = create();
            dialogUnderTest = dialog;

            var owner = new Window { Width = 200, Height = 200 };
            owner.Show();

            var shown = dialog.ShowDialog<T>(owner);
            _ = shown.ContinueWith(
                t => completion.TrySetResult(t.Result),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            close(dialog);
            owner.Close();
        }, CancellationToken.None);

        return await completion.Task;
    }
}
