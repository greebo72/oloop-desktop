using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using Xunit;

namespace OloopApp.Tests;

internal sealed class FakeAgentUi : IAgentUi
{
    public string? SavePath;
    public string? OpenPath;
    public bool Confirm = true;
    public Func<NewAgentViewModel, Task>? OnNewAgent;
    public readonly List<string> Questions = [];
    public string? SuggestedName;

    public async Task<bool> ShowNewAgentAsync(NewAgentViewModel viewModel)
    {
        if (OnNewAgent is not null)
            await OnNewAgent(viewModel);
        return viewModel.IsDone;
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText)
    {
        Questions.Add(title + "|" + confirmText);
        return Task.FromResult(Confirm);
    }

    public Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        SuggestedName = suggestedName;
        return Task.FromResult(SavePath);
    }

    public Task<string?> PickOpenFileAsync(string title) => Task.FromResult(OpenPath);
}

public sealed class AgentViewModelTests : IDisposable
{
    private readonly FakeOloopServer _server = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "oloop-agentvm-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAgentUi _ui = new();
    private ServerOloopStore _store = null!;
    private AgentService _service = null!;
    private IReadOnlyList<OloopProject> _projects = [];

    public AgentViewModelTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string PathOf(string name) => Path.Combine(_folder, name);

    private async Task OpenAsync(params string[] projectNames)
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        var user = new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey);
        _store = new ServerOloopStore(api, user);
        _service = new AgentService(api, user, FakeOloopServer.BaseUrl);
        var projects = new List<OloopProject>();
        foreach (var name in projectNames)
            projects.Add(await _store.CreateProjectAsync(name));
        _projects = projects;
    }

    private NewAgentViewModel NewForm() => new(_service, _projects, _ui);

    private AgentsViewModel Agents() => new(_service, () => _projects) { Ui = _ui };

    // ---------------- New agent ----------------

    [Fact]
    public async Task The_Button_Needs_A_Name_And_A_Project()
    {
        await OpenAsync("Beta", "Alpha");
        var form = NewForm();

        Assert.Equal(["Alpha", "Beta"], form.Projects.Select(p => p.Name));
        Assert.False(form.CanCreate);

        form.Name = "   ";
        form.Projects[0].IsSelected = true;
        Assert.False(form.CanCreate);

        form.Name = "claude";
        Assert.True(form.CanCreate);

        form.Projects[0].IsSelected = false;
        Assert.False(form.CanCreate);

        form.Projects[1].IsSelected = true;
        form.Name = new string('x', 101);
        Assert.False(form.CanCreate);
    }

    [Fact]
    public async Task Creating_Writes_The_File_Where_The_User_Chose_And_Shows_The_Result()
    {
        await OpenAsync("One");
        _ui.SavePath = PathOf("claude.json");
        var form = NewForm();
        form.Name = "Claude";
        form.Projects[0].IsSelected = true;

        Assert.True(await form.CreateAsync());

        Assert.True(form.IsDone);
        Assert.False(form.IsForm);
        Assert.Equal(PathOf("claude.json"), form.FilePath);
        Assert.Equal("export OLOOP_AGENT_CREDENTIALS='" + PathOf("claude.json") + "'", form.UseCommand);
        Assert.Equal("Claude.json", _ui.SuggestedName);

        var node = JsonNode.Parse(File.ReadAllText(PathOf("claude.json")))!;
        var agent = Assert.Single(_server.Agents);
        Assert.Equal(agent.Id.ToString("D"), (string?)node["agentId"]);
        Assert.Equal("Claude", (string?)node["name"]);
        Assert.Equal(agent.Token, (string?)node["token"]);
        Assert.Equal("write", Assert.Single(agent.Members.Values).Role);
    }

    [Fact]
    public async Task The_Times_Follow_The_Choices_And_Not_Renewable_Ends_With_The_Token()
    {
        Assert.Equal([30, 90, 365, 730], NewAgentViewModel.ValidityChoices.Select(c => c.Days));
        Assert.Equal([0, 365, 730], NewAgentViewModel.RenewalChoices.Select(c => c.Days));
        Assert.Equal("Not renewable", NewAgentViewModel.RenewalChoices[0].Label);

        await OpenAsync("One");
        _ui.SavePath = PathOf("a.json");
        var form = NewForm();
        form.Name = "a";
        form.ValidityIndex = 0;
        form.RenewalIndex = 0;
        form.ReadOnly = true;
        form.Projects[0].IsSelected = true;

        Assert.True(await form.CreateAsync());

        var agent = Assert.Single(_server.Agents);
        Assert.Equal(agent.ExpiresAt, agent.RenewUntil);
        Assert.InRange((agent.ExpiresAt - DateTimeOffset.UtcNow).TotalDays, 29.9, 30.1);
        Assert.Equal("read", Assert.Single(agent.Members.Values).Role);
    }

    [Fact]
    public async Task A_Cancelled_File_Chooser_Makes_Nothing()
    {
        await OpenAsync("One");
        _ui.SavePath = null;
        var form = NewForm();
        form.Name = "claude";
        form.Projects[0].IsSelected = true;

        Assert.False(await form.CreateAsync());

        Assert.Empty(_server.Agents);
        Assert.False(form.IsDone);
        Assert.Null(form.Error);
    }

    [Fact]
    public async Task When_The_File_Cannot_Be_Written_The_Agent_Is_Revoked_Again()
    {
        await OpenAsync("One");
        Directory.CreateDirectory(PathOf("folder")); // a file cannot replace a folder
        _ui.SavePath = PathOf("folder");
        var form = NewForm();
        form.Name = "claude";
        form.Projects[0].IsSelected = true;

        Assert.False(await form.CreateAsync());

        Assert.False(form.IsDone);
        Assert.Contains("revoked again", form.Error);
        var agent = Assert.Single(_server.Agents);
        Assert.True(agent.Revoked);
        Assert.Empty(agent.Members);
    }

    [Fact]
    public async Task A_Name_That_Is_Taken_Shows_The_Error_And_Keeps_The_Form()
    {
        await OpenAsync("One");
        _ui.SavePath = PathOf("a.json");
        var first = NewForm();
        first.Name = "claude";
        first.Projects[0].IsSelected = true;
        Assert.True(await first.CreateAsync());

        _ui.SavePath = PathOf("b.json");
        var second = NewForm();
        second.Name = "CLAUDE";
        second.Projects[0].IsSelected = true;
        Assert.False(await second.CreateAsync());

        Assert.Contains("exists already", second.Error);
        Assert.True(second.IsForm);
        Assert.False(File.Exists(PathOf("b.json")));
        Assert.Single(_server.Agents);
    }

    [Theory]
    [InlineData("Claude Code", "Claude-Code.json")]
    [InlineData("  a/b\\c  ", "a-b-c.json")]
    [InlineData("###", "agent.json")]
    [InlineData("büro_1", "büro_1.json")]
    public void The_File_Name_Is_Made_From_The_Agent_Name(string name, string expected)
        => Assert.Equal(expected, NewAgentViewModel.SuggestFileName(name));

    // ---------------- List, new token, revoke ----------------

    private async Task<AgentCredentials> MakeAsync(string name, string file)
    {
        var form = NewForm();
        _ui.SavePath = PathOf(file);
        form.Name = name;
        form.Projects[0].IsSelected = true;
        Assert.True(await form.CreateAsync());
        return (await _service.ListAsync()).Count > 0 ? AgentCredentialsLoad(PathOf(file)) : throw new InvalidOperationException();
    }

    private static AgentCredentials AgentCredentialsLoad(string path)
    {
        var node = JsonNode.Parse(File.ReadAllText(path))!;
        return new AgentCredentials(
            (string)node["server"]!, Guid.Parse((string)node["agentId"]!), (string)node["name"]!, (string)node["token"]!,
            Base64Url.Decode((string)node["publicKey"]!), Base64Url.Decode((string)node["secretKey"]!),
            Guid.Parse((string)node["owner"]!["id"]!), Base64Url.Decode((string)node["owner"]!["publicKey"]!),
            DateTimeOffset.Parse((string)node["expiresAt"]!, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task The_List_Shows_Active_Agents_First_And_Revoked_Ones_Below()
    {
        await OpenAsync("One");
        var zed = await MakeAsync("zed", "z.json");
        await MakeAsync("alpha", "a.json");
        await _service.RevokeAsync(zed.AgentId);
        var agents = Agents();

        await agents.LoadAsync();

        Assert.Equal(["alpha", "zed"], agents.Rows.Select(r => r.Name));
        Assert.Equal(["active", "revoked"], agents.Rows.Select(r => r.Status));
        Assert.Equal([true, false], agents.Rows.Select(r => r.CanRevoke));
        Assert.Equal([true, false], agents.Rows.Select(r => r.CanIssueToken));
        Assert.Equal("never", agents.Rows[0].LastUsedText);
        Assert.False(agents.IsEmpty);
    }

    [Fact]
    public async Task An_Empty_List_Says_So_And_An_Unreachable_Server_Is_An_Error()
    {
        await OpenAsync("One");
        var agents = Agents();

        await agents.LoadAsync();
        Assert.True(agents.IsEmpty);
        Assert.Null(agents.Error);

        _server.Down = true;
        await agents.LoadAsync();
        Assert.NotNull(agents.Error);
    }

    [Fact]
    public async Task New_Agent_Opens_The_Dialog_And_Reloads_The_List()
    {
        await OpenAsync("One");
        var agents = Agents();
        _ui.OnNewAgent = async form =>
        {
            _ui.SavePath = PathOf("made.json");
            form.Name = "made";
            form.Projects[0].IsSelected = true;
            await form.CreateAsync();
        };

        await agents.NewAgentCommand.ExecuteAsync(null);

        Assert.Equal(["made"], agents.Rows.Select(r => r.Name));
    }

    [Fact]
    public async Task A_New_Token_Is_Put_Into_The_Chosen_File_Of_That_Agent_Only_After_The_Question()
    {
        await OpenAsync("One");
        var made = await MakeAsync("claude", "c.json");
        var agents = Agents();
        await agents.LoadAsync();
        _ui.OpenPath = PathOf("c.json");

        await agents.NewTokenCommand.ExecuteAsync(agents.Rows[0]);

        Assert.Equal(["New token for claude|Choose file"], _ui.Questions);
        var node = JsonNode.Parse(File.ReadAllText(PathOf("c.json")))!;
        Assert.NotEqual(made.Token, (string?)node["token"]);
        Assert.Equal(_server.Agents[0].Token, (string?)node["token"]);
        Assert.Equal("claude", (string?)node["name"]);
        Assert.Contains("New token for claude", agents.Notice);
        Assert.Null(agents.Error);
    }

    [Fact]
    public async Task No_Token_Is_Issued_When_The_Question_Is_Declined_Or_The_File_Is_Not_The_Agents()
    {
        await OpenAsync("One");
        var one = await MakeAsync("one", "one.json");
        await MakeAsync("two", "two.json");
        var agents = Agents();
        await agents.LoadAsync();
        var row = agents.Rows.Single(r => r.Name == "one");
        var before = _server.Agents.Single(a => a.Id == one.AgentId).Token;

        _ui.Confirm = false;
        await agents.NewTokenCommand.ExecuteAsync(row);
        Assert.Equal(before, _server.Agents.Single(a => a.Id == one.AgentId).Token);

        _ui.Confirm = true;
        _ui.OpenPath = null; // the file chooser is cancelled
        await agents.NewTokenCommand.ExecuteAsync(row);
        Assert.Equal(before, _server.Agents.Single(a => a.Id == one.AgentId).Token);

        _ui.OpenPath = PathOf("two.json"); // the file of another agent
        await agents.NewTokenCommand.ExecuteAsync(row);
        Assert.Equal(before, _server.Agents.Single(a => a.Id == one.AgentId).Token);
        Assert.Contains("another agent", agents.Error);
    }

    [Fact]
    public async Task Revoking_Asks_First_And_Then_Revokes_And_Keeps_The_Row()
    {
        await OpenAsync("One");
        await MakeAsync("claude", "c.json");
        var agents = Agents();
        await agents.LoadAsync();

        _ui.Confirm = false;
        await agents.RevokeCommand.ExecuteAsync(agents.Rows[0]);
        Assert.False(_server.Agents[0].Revoked);

        _ui.Confirm = true;
        await agents.RevokeCommand.ExecuteAsync(agents.Rows[0]);

        Assert.True(_server.Agents[0].Revoked);
        Assert.Equal(["Revoke claude|Revoke", "Revoke claude|Revoke"], _ui.Questions);
        Assert.Equal("revoked", Assert.Single(agents.Rows).Status);
        Assert.Contains("was revoked", agents.Notice);
    }
}
