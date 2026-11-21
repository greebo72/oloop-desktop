using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Models;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

/// <summary>The agent dialogs must come up headless and show what the view models hold.</summary>
public sealed class AgentDialogUiTests : IDisposable
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(AgentDialogUiTests).Assembly);

    private readonly FakeOloopServer _server = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "oloop-agentui-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAgentUi _ui = new();

    public AgentDialogUiTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

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

    private async Task<(AgentService Service, IReadOnlyList<OloopProject> Projects)> OpenAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        var user = new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey);
        var store = new ServerOloopStore(api, user);
        return (new AgentService(api, user, FakeOloopServer.BaseUrl), [await store.CreateProjectAsync("Alpha")]);
    }

    [Fact]
    public async Task The_New_Agent_Dialog_Shows_The_Form_And_Enables_The_Button_When_It_Is_Filled()
    {
        var (service, projects) = await OpenAsync();
        var vm = new NewAgentViewModel(service, projects, _ui);

        await Session.Dispatch(() =>
        {
            var dialog = new NewAgentDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("New agent", texts);
            Assert.Contains("Projects", texts);
            Assert.Contains("Token valid for", texts);
            Assert.Single(Descendants<CheckBox>(dialog), c => c.Content as string == "Alpha");

            var create = Descendants<Button>(dialog).Single(b => b.Content as string == "Create and save file…");
            Assert.False(create.IsEnabled);
            vm.Name = "claude";
            vm.Projects[0].IsSelected = true;
            Assert.True(create.IsEnabled);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_New_Agent_Dialog_Shows_The_File_And_The_Command_When_The_Agent_Is_Made()
    {
        var (service, projects) = await OpenAsync();
        _ui.SavePath = Path.Combine(_folder, "claude.json");
        var vm = new NewAgentViewModel(service, projects, _ui) { Name = "claude" };
        vm.Projects[0].IsSelected = true;
        Assert.True(await vm.CreateAsync());

        await Session.Dispatch(() =>
        {
            var dialog = new NewAgentDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();

            var texts = Descendants<TextBlock>(dialog).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Agent created", texts);
            var boxes = Descendants<TextBox>(dialog).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains(vm.FilePath, boxes);
            Assert.Contains(vm.UseCommand, boxes);
            Assert.DoesNotContain("Name", texts); // the form is gone
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Agents_Dialog_Lists_The_Agents_With_Their_Buttons()
    {
        var (service, projects) = await OpenAsync();
        _ui.SavePath = Path.Combine(_folder, "claude.json");
        var form = new NewAgentViewModel(service, projects, _ui) { Name = "claude" };
        form.Projects[0].IsSelected = true;
        await form.CreateAsync();
        var vm = new AgentsViewModel(service, () => projects);
        await vm.LoadAsync();

        await Session.Dispatch(() =>
        {
            var dialog = new AgentsDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("claude", texts);
            Assert.Contains("active", texts);
            var buttons = Descendants<Button>(dialog).Select(b => b.Content as string).ToList();
            Assert.Contains("New token", buttons);
            Assert.Contains("Revoke", buttons);
            Assert.Contains("New agent…", buttons);
            Assert.DoesNotContain(Descendants<TextBlock>(dialog), t => t.Text == "No agents yet." && t.IsEffectivelyVisible);
            Assert.Same(dialog, vm.Ui);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Agents_Dialog_Says_So_When_There_Are_None_And_A_Revoked_Agent_Has_No_Buttons_To_Press()
    {
        var (service, projects) = await OpenAsync();
        var vm = new AgentsViewModel(service, () => projects);
        await vm.LoadAsync();

        await Session.Dispatch(() =>
        {
            var dialog = new AgentsDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();
            Assert.Contains(Descendants<TextBlock>(dialog), t => t.Text == "No agents yet." && t.IsEffectivelyVisible);
            dialog.Close();
        }, CancellationToken.None);

        _ui.SavePath = Path.Combine(_folder, "x.json");
        var form = new NewAgentViewModel(service, projects, _ui) { Name = "x" };
        form.Projects[0].IsSelected = true;
        await form.CreateAsync();
        await service.RevokeAsync(_server.Agents[0].Id);
        await vm.LoadAsync();

        await Session.Dispatch(() =>
        {
            var dialog = new AgentsDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();
            var row = Descendants<Button>(dialog).Where(b => b.Content as string is "New token" or "Revoke").ToList();
            Assert.Equal(2, row.Count);
            Assert.All(row, b => Assert.False(b.IsEnabled));
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Confirm_Dialog_Shows_The_Question_And_Names_The_Action()
    {
        await Session.Dispatch(() =>
        {
            var dialog = new ConfirmDialog("Revoke claude", "It ends at once.", "Revoke");
            dialog.Show();
            dialog.UpdateLayout();

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("Revoke claude", texts);
            Assert.Contains("It ends at once.", texts);
            var buttons = Descendants<Button>(dialog).Select(b => b.Content as string).ToList();
            Assert.Contains("Revoke", buttons);
            Assert.Contains("Cancel", buttons);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Settings_Dialog_Offers_The_Agents_Only_While_The_Keys_Are_Open()
    {
        var control = new AgentControl();
        var vm = new SettingsViewModel(new OloopApp.Core.Io.OloopSettings
        {
            Backend = "server", ServerUrl = "https://oloop.example", ServerUser = "timo", Actor = "x",
        })
        {
            ServerBackendAvailable = true,
            ServerControl = control,
        };

        await Session.Dispatch(() =>
        {
            var dialog = new SettingsDialog(vm);
            dialog.Show();
            // The button is on the tab "Security", with the other things of the session.
            dialog.FindControl<TabControl>("Tabs")!.SelectedItem = dialog.FindControl<TabItem>("SecurityTab");
            dialog.UpdateLayout();

            var button = Descendants<Button>(dialog).Single(b => b.Content as string == "Manage agents…");
            Assert.True(button.IsEffectivelyEnabled);
            vm.LockKeysCommand.Execute(null);
            Assert.False(button.IsEffectivelyEnabled);
            dialog.Close();
        }, CancellationToken.None);
    }

    private sealed class AgentControl : IServerSessionControl
    {
        public ServerSessionState State { get; set; } = ServerSessionState.OpenKeyKept;
        public Task LockAsync() { State = ServerSessionState.Locked; return Task.CompletedTask; }
        public Task SignOutAsync() => Task.CompletedTask;
        public Task ForgetRevisionsAsync() => Task.CompletedTask;
    }
}
