using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>The dialog of the import: its pages, its buttons and what it writes.</summary>
public sealed class ImportFolderViewModelTests : IDisposable
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ImportFolderViewModelTests).Assembly);

    private readonly FakeOloopServer _server = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "oloop-importvm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeUi _ui = new();

    public ImportFolderViewModelTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private sealed class FakeUi : IImportUi
    {
        public string? Folder { get; set; }
        public string? SaveTo { get; set; }
        public Task<string?> PickFolderAsync(string title) => Task.FromResult(Folder);
        public Task<string?> PickSaveFileAsync(string title, string suggestedName) => Task.FromResult(SaveTo);
    }

    private async Task<ServerOloopStore> OpenStoreAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        return new ServerOloopStore(api,
            new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey));
    }

    private ImportFolderViewModel NewViewModel(ServerOloopStore store, string? folder)
    {
        _ui.Folder = folder;
        return new ImportFolderViewModel(store) { Ui = _ui };
    }

    private string WriteProject(string name, int tasks)
    {
        var project = new OloopProject
        {
            FormatVersion = 1, Id = Guid.NewGuid().ToString("D"), Name = name, CreatedAt = "2026-10-16T19:18:14Z",
        };
        var dir = Path.Combine(_folder, name, "tasks");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(_folder, name, "project.md"), ProjectIo.Write(project));
        for (var i = 0; i < tasks; i++)
        {
            var task = new OloopTask
            {
                FormatVersion = 1, Id = Guid.NewGuid().ToString("D"), ProjectId = project.Id, Type = TaskType.todo,
                Title = $"{name} {i}", Status = TaskStatus.open, CreatedAt = "2026-10-16T19:20:00Z",
                CreatedBy = "claude", UpdatedAt = "2026-10-16T19:20:00Z", UpdatedBy = "claude",
            };
            File.WriteAllText(Path.Combine(dir, task.Id + ".md"), TaskIo.Write(task));
        }

        return _folder;
    }

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

    [Fact]
    public async Task The_Dialog_Starts_On_The_First_Page_And_Cannot_Import()
    {
        var vm = NewViewModel(await OpenStoreAsync(), null);

        Assert.True(vm.IsStart);
        Assert.False(vm.CanImport);
        Assert.True(vm.CanClose);

        await vm.ChooseFolderCommand.ExecuteAsync(null); // the chooser was cancelled

        Assert.True(vm.IsStart);
    }

    [Fact]
    public async Task Choosing_A_Folder_Shows_The_Plan_And_Writes_Nothing()
    {
        WriteProject("alpha", 2);
        WriteProject("beta", 3);
        var vm = NewViewModel(await OpenStoreAsync(), _folder);
        var puts = _server.DocumentPuts;

        await vm.ChooseFolderCommand.ExecuteAsync(null);

        Assert.True(vm.IsPlanned);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("Import 2 projects, 5 tasks", vm.Summary);
        Assert.True(vm.CanImport);
        Assert.Equal(puts, _server.DocumentPuts);
    }

    [Fact]
    public async Task Unticking_A_Project_Changes_The_Summary_And_Nothing_Chosen_Cannot_Import()
    {
        WriteProject("alpha", 2);
        WriteProject("beta", 3);
        var vm = NewViewModel(await OpenStoreAsync(), _folder);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        vm.Rows.Single(r => r.Name == "beta").Selected = false;
        Assert.Equal("Import 1 project, 2 tasks", vm.Summary);
        Assert.True(vm.CanImport);

        vm.Rows.Single(r => r.Name == "alpha").Selected = false;
        Assert.Equal("Nothing to import", vm.Summary);
        Assert.False(vm.CanImport);
    }

    [Fact]
    public async Task A_Project_Without_Work_Cannot_Be_Ticked()
    {
        var store = await OpenStoreAsync();
        WriteProject("alpha", 1);
        await store.CreateProjectAsync(new OloopProject
        {
            FormatVersion = 1, Id = Guid.NewGuid().ToString("D"), Name = "ALPHA", CreatedAt = "2026-10-16T19:18:14Z",
        });
        var vm = NewViewModel(store, _folder);

        await vm.ChooseFolderCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Rows);
        Assert.False(row.CanSelect);
        row.Selected = true;
        Assert.False(row.Selected);
        Assert.Contains("Conflict", row.ActionText);
        Assert.False(vm.CanImport);
    }

    [Fact]
    public async Task Import_Writes_Shows_The_Report_And_Keeps_The_Folder()
    {
        WriteProject("alpha", 2);
        var vm = NewViewModel(await OpenStoreAsync(), _folder);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.True(vm.IsDone);
        Assert.True(vm.CanClose);
        Assert.Contains("2 tasks written", vm.ReportText);
        Assert.Contains("The folder was not changed.", vm.ReportText);
        Assert.Equal(2, (await (await OpenStoreAsync()).LoadAsync()).Tasks.Count);
        Assert.Equal(2, Directory.GetFiles(Path.Combine(_folder, "alpha", "tasks")).Length);
    }

    [Fact]
    public async Task Another_Folder_Goes_Back_To_The_First_Page()
    {
        WriteProject("alpha", 1);
        var vm = NewViewModel(await OpenStoreAsync(), _folder);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        vm.RestartCommand.Execute(null);

        Assert.True(vm.IsStart);
        Assert.Empty(vm.Rows);
        Assert.Null(vm.Folder);
    }

    [Fact]
    public async Task A_Folder_That_Does_Not_Exist_Is_An_Error_Not_A_Crash()
    {
        var vm = NewViewModel(await OpenStoreAsync(), Path.Combine(_folder, "missing"));

        await vm.ChooseFolderCommand.ExecuteAsync(null);

        Assert.True(vm.IsStart);
        Assert.NotNull(vm.Error);
    }

    [Fact]
    public async Task The_Plan_And_The_Report_Can_Be_Saved_As_Text()
    {
        WriteProject("alpha", 1);
        var vm = NewViewModel(await OpenStoreAsync(), _folder);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        var planFile = Path.Combine(_folder, "plan.txt");
        _ui.SaveTo = planFile;
        await vm.SaveTextCommand.ExecuteAsync(null);
        Assert.Contains("Import plan for", File.ReadAllText(planFile));

        await vm.ImportCommand.ExecuteAsync(null);
        var reportFile = Path.Combine(_folder, "report.txt");
        _ui.SaveTo = reportFile;
        await vm.SaveTextCommand.ExecuteAsync(null);
        Assert.Contains("1 tasks written", File.ReadAllText(reportFile));
    }

    [Fact]
    public async Task The_Dialog_Shows_The_Plan_Headless()
    {
        WriteProject("alpha", 2);
        var vm = NewViewModel(await OpenStoreAsync(), _folder);
        await vm.ChooseFolderCommand.ExecuteAsync(null);

        await Session.Dispatch(() =>
        {
            var dialog = new ImportFolderDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("Import folder", texts);
            Assert.Contains("alpha", texts);
            var buttons = Descendants<Button>(dialog).Where(b => b.IsVisible).Select(b => b.Content?.ToString()).ToList();
            Assert.Contains("Import 1 project, 2 tasks", buttons);
            Assert.Contains("Another folder…", buttons);
            Assert.DoesNotContain("Choose folder…", buttons);
            dialog.Close();
        }, CancellationToken.None);
    }
}
