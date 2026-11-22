using System.Buffers.Binary;
using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using Sodium;
using Xunit;

namespace OloopApp.Tests;

/// <summary>Giving an existing agent more projects (docs/SPEC-agent-grant-projects.md).</summary>
public sealed class GrantProjectsTests
{
    private readonly FakeOloopServer _server = new();
    private ServerOloopStore _store = null!;
    private AgentService _service = null!;
    private AgentCredentials _file = null!;
    private OloopProject _first = null!;

    private async Task OpenAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        var user = new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey);
        _store = new ServerOloopStore(api, user);
        _service = new AgentService(api, user, FakeOloopServer.BaseUrl);
        _first = await _store.CreateProjectAsync("First");
        _file = await _service.CreateAsync(new NewAgent(
            "claude", [Guid.Parse(_first.Id)], false, DateTimeOffset.UtcNow.AddDays(90), DateTimeOffset.UtcNow.AddYears(1)));
    }

    private async Task<AgentInfo> AgentAsync() => Assert.Single(await _service.ListAsync());

    private FakeOloopServer.AgentRecord Record => Assert.Single(_server.Agents);

    private async Task<OloopProject> ProjectAsync(string name) => await _store.CreateProjectAsync(name);

    /// <summary>A rotation of the agent's key pair as the agent would make it (AGENTS.md): signed with the old key, for the user.</summary>
    private byte[] Statement(byte[] oldSecretKey, byte[] newPublicKey, Guid agentId, int number)
    {
        var message = new byte[32 + 16 + 4];
        newPublicKey.CopyTo(message, 0);
        GuidBytes.ToBytes(agentId).CopyTo(message, 32);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(48), (uint)number);
        var nonce = SodiumCore.GetRandomBytes(24);
        var box = PublicKeyBox.Create(message, nonce, oldSecretKey, _server.UserKeys.PublicKey);
        return [.. nonce, .. box];
    }

    private KeyPair Rotate(KeyPair previous, Guid agentId, int number, Guid? statedId = null, byte[]? listedKey = null)
    {
        var next = PublicKeyBox.GenerateKeyPair();
        var statement = Statement(previous.PrivateKey, next.PublicKey, statedId ?? agentId, number);
        Record.KeyChain.Add(new JsonObject
        {
            ["publicKey"] = Base64Url.Encode(listedKey ?? next.PublicKey),
            ["statement"] = Base64Url.Encode(statement),
        });
        Record.PublicKey = next.PublicKey;
        return next;
    }

    private KeyPair FirstKeys => new(_file.PublicKey, _file.SecretKey);

    // ---------------- The hand-over ----------------

    [Fact]
    public async Task Two_Projects_Are_Handed_Over_With_A_Key_The_Agent_Can_Open()
    {
        await OpenAsync();
        var two = await ProjectAsync("Two");
        var three = await ProjectAsync("Three");

        var result = await _service.GrantProjectsAsync(
            await AgentAsync(), [Guid.Parse(two.Id), Guid.Parse(three.Id)], readOnly: true);

        Assert.Equal(2, result.Granted.Count);
        Assert.Empty(result.Failed);
        foreach (var project in new[] { two, three })
        {
            var id = Guid.Parse(project.Id);
            var (role, keys) = Record.Members[id];
            Assert.Equal("read", role);
            var handed = Assert.Single(keys);
            var key = ProjectKeyBox.Unwrap(
                Base64Url.Decode(handed!["wrapped"]!.GetValue<string>()),
                _file.SecretKey, _server.UserKeys.PublicKey, id, handed["keyVersion"]!.GetValue<int>());
            Assert.Equal(_server.ProjectKey(id), key);
        }

        // The project the agent had stays as it was.
        Assert.Equal("write", Record.Members[Guid.Parse(_first.Id)].Role);
    }

    [Fact]
    public async Task The_Projects_The_Agent_Has_Are_Shown_With_Their_Role_And_Cannot_Be_Ticked()
    {
        await OpenAsync();
        var two = await ProjectAsync("Two");
        var vm = new GrantProjectsViewModel(_service, await AgentAsync(), [_first, two]);

        await vm.LoadAsync();

        var has = vm.Projects.Single(p => p.Name == "First");
        Assert.Equal("write", has.ExistingRole);
        Assert.False(has.CanSelect);
        Assert.Equal("has it (write)", has.RoleText);
        Assert.True(vm.Projects.Single(p => p.Name == "Two").CanSelect);
        Assert.False(vm.NothingToGrant);
        Assert.False(vm.CanGrant);
    }

    [Fact]
    public async Task When_The_Agent_Has_Every_Project_The_Dialog_Says_So()
    {
        await OpenAsync();
        var vm = new GrantProjectsViewModel(_service, await AgentAsync(), [_first]);

        await vm.LoadAsync();

        Assert.True(vm.NothingToGrant);
        Assert.False(vm.CanGrant);
    }

    [Fact]
    public async Task The_View_Model_Enables_The_Button_Only_For_A_Ticked_Project_And_Marks_What_Was_Handed_Over()
    {
        await OpenAsync();
        var two = await ProjectAsync("Two");
        var vm = new GrantProjectsViewModel(_service, await AgentAsync(), [_first, two]);
        Assert.False(vm.CanGrant); // not loaded yet

        await vm.LoadAsync();
        Assert.False(vm.CanGrant);
        vm.Projects.Single(p => p.Name == "Two").IsSelected = true;
        Assert.True(vm.CanGrant);

        await vm.GrantAsync();

        var granted = vm.Projects.Single(p => p.Name == "Two");
        Assert.Equal("write", granted.ExistingRole);
        Assert.False(granted.IsSelected);
        Assert.False(vm.CanGrant);
        Assert.Contains("1 project handed over to claude", vm.Result);
        Assert.Null(vm.Error);
        Assert.True(vm.NothingToGrant);
    }

    [Fact]
    public async Task A_Failed_Project_Stays_Ticked_And_A_Second_Try_Hands_Over_Only_That_One()
    {
        await OpenAsync();
        var two = await ProjectAsync("Two");
        var three = await ProjectAsync("Three");
        var vm = new GrantProjectsViewModel(_service, await AgentAsync(), [_first, two, three]);
        await vm.LoadAsync();
        foreach (var project in vm.Projects.Where(p => p.CanSelect))
            project.IsSelected = true;

        // The agent was handed one project when it was made (put 1), so the next hand-over is the second.
        _server.FailMemberPutNumber = 3;
        await vm.GrantAsync();

        Assert.Equal(2, Record.Members.Count);
        var failed = Assert.Single(vm.Projects, p => p.IsSelected);
        Assert.NotNull(vm.Error);
        Assert.Contains(failed.Name, vm.Error);
        Assert.Contains("1 project handed over", vm.Result);

        _server.FailMemberPutNumber = 0;
        await vm.GrantAsync();

        Assert.Equal(3, Record.Members.Count);
        Assert.Null(vm.Error);
        Assert.True(vm.NothingToGrant);
    }

    [Fact]
    public async Task A_Revoked_Agent_Gets_Nothing()
    {
        await OpenAsync();
        var two = await ProjectAsync("Two");
        await _service.RevokeAsync(_file.AgentId);
        var agent = await AgentAsync();

        Assert.True(agent.Revoked);
        Assert.False(new AgentRowViewModel(agent, DateTimeOffset.UtcNow).CanGrant);
        await Assert.ThrowsAsync<OloopFormatException>(
            () => _service.GrantProjectsAsync(agent, [Guid.Parse(two.Id)], false));
        Assert.Empty(Record.Members);
    }

    [Fact]
    public async Task A_Project_That_Cannot_Be_Handed_Over_Stops_The_Run_Before_Any_Hand_Over()
    {
        await OpenAsync();
        var two = await ProjectAsync("Two");
        var before = Record.Members.Count;

        var agent = await AgentAsync();
        await Assert.ThrowsAsync<OloopFormatException>(() => _service.GrantProjectsAsync(
            agent, [Guid.Parse(two.Id), Guid.NewGuid()], false));

        Assert.Equal(before, Record.Members.Count);
    }

    [Fact]
    public async Task The_Dialog_Of_The_List_Opens_With_The_Agent_And_The_Projects_Of_The_User()
    {
        await OpenAsync();
        var two = await ProjectAsync("Two");
        var ui = new FakeAgentUi();
        GrantProjectsViewModel? shown = null;
        ui.OnGrantProjects = vm => { shown = vm; return Task.CompletedTask; };
        var list = new AgentsViewModel(_service, () => [_first, two]) { Ui = ui };
        await list.LoadAsync();

        await list.GrantProjectsCommand.ExecuteAsync(list.Rows.Single());

        Assert.NotNull(shown);
        Assert.Equal("claude", shown.AgentName);
        Assert.Equal(2, shown.Projects.Count);
    }

    // ---------------------------------------------------------------- The key chain

    [Fact]
    public async Task A_Registration_Alone_Is_A_Valid_Chain()
    {
        await OpenAsync();

        Assert.Equal(_file.PublicKey, _service.VerifyKeyChain(await AgentAsync()));
    }

    [Fact]
    public async Task A_Chain_With_Two_Rotations_Is_Valid_And_The_Key_Is_Wrapped_For_Its_End()
    {
        await OpenAsync();
        var second = Rotate(FirstKeys, _file.AgentId, 2);
        var third = Rotate(second, _file.AgentId, 3);
        var two = await ProjectAsync("Two");
        var agent = await AgentAsync();

        Assert.Equal(third.PublicKey, _service.VerifyKeyChain(agent));
        await _service.GrantProjectsAsync(agent, [Guid.Parse(two.Id)], false);

        // Only the current key of the agent can open what was handed over.
        var id = Guid.Parse(two.Id);
        var handed = Assert.Single(Record.Members[id].Keys);
        var key = ProjectKeyBox.Unwrap(
            Base64Url.Decode(handed!["wrapped"]!.GetValue<string>()),
            third.PrivateKey, _server.UserKeys.PublicKey, id, handed["keyVersion"]!.GetValue<int>());
        Assert.Equal(_server.ProjectKey(id), key);
    }

    private async Task AssertNothingIsHandedOverAsync()
    {
        var two = await ProjectAsync("Two");
        var before = Record.Members.Count;
        var agent = await AgentAsync();
        var vm = new GrantProjectsViewModel(_service, agent, [_first, two]);
        await vm.LoadAsync();
        vm.Projects.Single(p => p.Name == "Two").IsSelected = true;

        await Assert.ThrowsAsync<OloopCryptoException>(
            () => _service.GrantProjectsAsync(agent, [Guid.Parse(two.Id)], false));
        await vm.GrantAsync();

        Assert.Equal(before, Record.Members.Count);
        Assert.True(vm.ChainInvalid);
        Assert.False(vm.CanGrant);
        Assert.Contains("Revoke the agent", vm.Error);
    }

    [Fact]
    public async Task The_Server_Showing_Another_Key_Than_The_Chain_Ends_With_Hands_Over_Nothing()
    {
        await OpenAsync();
        Record.PublicKey = PublicKeyBox.GenerateKeyPair().PublicKey;

        await AssertNothingIsHandedOverAsync();
    }

    [Fact]
    public async Task A_Statement_For_Another_Agent_Hands_Over_Nothing()
    {
        await OpenAsync();
        Rotate(FirstKeys, _file.AgentId, 2, statedId: Guid.NewGuid());

        await AssertNothingIsHandedOverAsync();
    }

    [Fact]
    public async Task A_Chain_That_Skips_A_Key_Number_Hands_Over_Nothing()
    {
        await OpenAsync();
        Rotate(FirstKeys, _file.AgentId, 3);

        await AssertNothingIsHandedOverAsync();
    }

    [Fact]
    public async Task A_Statement_For_Another_Key_Than_The_One_Listed_Hands_Over_Nothing()
    {
        await OpenAsync();
        Rotate(FirstKeys, _file.AgentId, 2, listedKey: PublicKeyBox.GenerateKeyPair().PublicKey);

        await AssertNothingIsHandedOverAsync();
    }

    [Fact]
    public async Task A_Registration_Made_With_Another_Users_Key_Hands_Over_Nothing()
    {
        await OpenAsync();
        var stranger = PublicKeyBox.GenerateKeyPair();
        var strangerPeer = PublicKeyBox.GenerateKeyPair();
        Record.Registration = AgentCrypto.CreateRegistration(
            Record.PublicKey, Record.Id, stranger.PrivateKey, strangerPeer.PublicKey);

        await AssertNothingIsHandedOverAsync();
    }

    [Fact]
    public async Task A_Registration_That_Names_Another_Agent_Hands_Over_Nothing()
    {
        await OpenAsync();
        Record.Registration = AgentCrypto.CreateRegistration(
            Record.PublicKey, Guid.NewGuid(), _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey);

        await AssertNothingIsHandedOverAsync();
    }
}
