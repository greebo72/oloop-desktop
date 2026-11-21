using System.Net;
using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Server;
using Sodium;
using Xunit;

namespace OloopApp.Tests;

public class AgentServiceTests
{
    private readonly FakeOloopServer _server = new();
    private ServerApi _api = null!;
    private ServerOloopStore _store = null!;
    private AgentService _agents = null!;

    private async Task OpenAsync()
    {
        _api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await _api.LoginAsync("timo", "secret", "test device");
        var user = new UnlockedUser(_api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey);
        _store = new ServerOloopStore(_api, user);
        _agents = new AgentService(_api, user, FakeOloopServer.BaseUrl + "/");
    }

    private static NewAgent Request(string name, IEnumerable<Guid> projects, bool readOnly = false) => new(
        name, [.. projects], readOnly, DateTimeOffset.UtcNow.AddDays(90), DateTimeOffset.UtcNow.AddYears(1));

    private async Task<Guid> ProjectAsync(string name)
        => Guid.Parse((await _store.CreateProjectAsync(name)).Id);

    [Fact]
    public async Task Creating_An_Agent_Registers_It_And_Hands_Over_Each_Project_With_A_Key_It_Can_Open()
    {
        await OpenAsync();
        var one = await ProjectAsync("One");
        var two = await ProjectAsync("Two");

        var file = await _agents.CreateAsync(Request("  Claude ", [one, two]));

        var agent = Assert.Single(_server.Agents);
        Assert.Equal("Claude", agent.Name);
        Assert.Equal(file.AgentId, agent.Id);
        Assert.Equal(file.PublicKey, agent.PublicKey);
        Assert.Equal(file.Token, agent.Token);
        Assert.Equal(88, agent.Registration.Length);

        // The registration is the user's: it opens with the user's keys and names this agent.
        var (publicKey, id) = AgentCrypto.OpenRegistration(
            agent.Registration, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey);
        Assert.Equal(file.PublicKey, publicKey);
        Assert.Equal(file.AgentId, id);

        Assert.Equal([one, two], agent.Members.Keys.OrderBy(k => k == one ? 0 : 1));
        foreach (var (projectId, (role, keys)) in agent.Members)
        {
            Assert.Equal("write", role);
            var handed = Assert.Single(keys);
            var projectKey = ProjectKeyBox.Unwrap(
                Base64Url.Decode(handed!["wrapped"]!.GetValue<string>()),
                file.SecretKey, _server.UserKeys.PublicKey, projectId, handed["keyVersion"]!.GetValue<int>());
            Assert.Equal(_server.ProjectKey(projectId), projectKey);
        }
    }

    [Fact]
    public async Task The_Credentials_Hold_The_Owner_From_The_Own_Keys_And_A_Matching_Key_Pair()
    {
        await OpenAsync();
        var file = await _agents.CreateAsync(Request("claude", [await ProjectAsync("One")]));

        Assert.Equal(FakeOloopServer.BaseUrl, file.Server);
        Assert.Equal("claude", file.Name);
        Assert.Equal(_server.UserId, file.OwnerId);
        Assert.Equal(_server.UserKeys.PublicKey, file.OwnerPublicKey);
        Assert.Equal(file.PublicKey, PublicKeyBox.GenerateKeyPair(file.SecretKey).PublicKey);
    }

    [Fact]
    public async Task A_Read_Only_Agent_Gets_The_Role_Read()
    {
        await OpenAsync();
        await _agents.CreateAsync(Request("reader", [await ProjectAsync("One")], readOnly: true));

        Assert.Equal("read", Assert.Single(Assert.Single(_server.Agents).Members.Values).Role);
    }

    [Fact]
    public async Task An_Id_That_Is_Taken_Is_Retried_With_A_New_Id_And_A_New_Registration()
    {
        await OpenAsync();
        _server.RejectAgentIds = 2;

        var file = await _agents.CreateAsync(Request("claude", [await ProjectAsync("One")]));

        var agent = Assert.Single(_server.Agents);
        Assert.Equal(file.AgentId, agent.Id);
        var (_, id) = AgentCrypto.OpenRegistration(
            agent.Registration, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey);
        Assert.Equal(file.AgentId, id);
    }

    [Fact]
    public async Task An_Id_That_Stays_Taken_Gives_Up_After_Three_Tries_And_Registers_Nothing()
    {
        await OpenAsync();
        var project = await ProjectAsync("One");
        _server.RejectAgentIds = 10;

        var error = await Assert.ThrowsAsync<ServerApiException>(() => _agents.CreateAsync(Request("claude", [project])));

        Assert.True(error.IsConflict);
        Assert.Equal(7, _server.RejectAgentIds); // three requests were made
        Assert.Empty(_server.Agents);
    }

    [Fact]
    public async Task A_Name_That_Is_Taken_Is_Refused_Before_Anything_Is_Registered_And_Is_Free_After_A_Revoke()
    {
        await OpenAsync();
        var project = await ProjectAsync("One");
        var first = await _agents.CreateAsync(Request("Claude", [project]));

        var error = await Assert.ThrowsAsync<OloopFormatException>(
            () => _agents.CreateAsync(Request(" claude", [project])));
        Assert.Contains("exists already", error.Message);
        Assert.Single(_server.Agents);

        await _agents.RevokeAsync(first.AgentId);
        await _agents.CreateAsync(Request("claude", [project]));
        Assert.Equal(2, _server.Agents.Count);
    }

    [Fact]
    public async Task When_A_Project_Fails_The_Agent_Is_Revoked_Again_And_Has_No_Project()
    {
        await OpenAsync();
        var one = await ProjectAsync("One");
        var two = await ProjectAsync("Two");
        _server.FailMemberPutNumber = 2;

        var error = await Assert.ThrowsAsync<AgentCreateException>(() => _agents.CreateAsync(Request("claude", [one, two])));

        Assert.True(error.RolledBack);
        var agent = Assert.Single(_server.Agents);
        Assert.True(agent.Revoked);
        Assert.Empty(agent.Members);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_Empty_Name_Is_Refused(string name)
    {
        await OpenAsync();
        var project = await ProjectAsync("One");
        await Assert.ThrowsAsync<OloopFormatException>(() => _agents.CreateAsync(Request(name, [project])));
        Assert.Empty(_server.Agents);
    }

    [Fact]
    public async Task A_Name_Of_101_Characters_Is_Refused_And_100_Is_Taken()
    {
        await OpenAsync();
        var project = await ProjectAsync("One");

        await Assert.ThrowsAsync<OloopFormatException>(() => _agents.CreateAsync(Request(new string('x', 101), [project])));
        await _agents.CreateAsync(Request(new string('x', 100), [project]));
    }

    [Fact]
    public async Task No_Project_Or_An_Unknown_One_Is_Refused()
    {
        await OpenAsync();
        await Assert.ThrowsAsync<OloopFormatException>(() => _agents.CreateAsync(Request("a", [])));
        await Assert.ThrowsAsync<OloopFormatException>(() => _agents.CreateAsync(Request("b", [Guid.NewGuid()])));
        Assert.Empty(_server.Agents);
    }

    [Fact]
    public async Task The_Times_Are_Checked()
    {
        await OpenAsync();
        var project = await ProjectAsync("One");
        var now = DateTimeOffset.UtcNow;

        foreach (var (expires, renew) in new[]
        {
            (now.AddDays(-1), now.AddYears(1)),             // in the past
            (now.AddYears(11), now.AddYears(11)),           // beyond ten years
            (now.AddDays(30), now.AddDays(10)),             // renewal before the token ends
            (now.AddDays(30), now.AddYears(11)),            // renewal beyond ten years
        })
        {
            await Assert.ThrowsAsync<OloopFormatException>(
                () => _agents.CreateAsync(new NewAgent("t", [project], false, expires, renew)));
        }

        Assert.Empty(_server.Agents);
    }

    [Fact]
    public async Task The_List_Shows_Status_And_Dates_And_A_Revoked_Agent_Stays()
    {
        await OpenAsync();
        var project = await ProjectAsync("One");
        var first = await _agents.CreateAsync(Request("one", [project]));
        await _agents.CreateAsync(Request("two", [project]));
        await _agents.RevokeAsync(first.AgentId);
        await _agents.RevokeAsync(first.AgentId); // can be repeated

        var list = await _agents.ListAsync();

        Assert.Equal(["one", "two"], list.Select(a => a.Name).Order());
        var revoked = list.Single(a => a.Name == "one");
        Assert.True(revoked.Revoked);
        Assert.Equal("revoked", revoked.StatusAt(DateTimeOffset.UtcNow));
        var active = list.Single(a => a.Name == "two");
        Assert.Equal("active", active.StatusAt(DateTimeOffset.UtcNow));
        Assert.Equal("expired", active.StatusAt(DateTimeOffset.UtcNow.AddYears(2)));
        Assert.NotNull(active.ExpiresAt);
        Assert.Null(active.LastUsedAt);
    }

    [Fact]
    public async Task A_New_Token_Is_Issued_And_A_Revoked_Agent_Answers_Conflict()
    {
        await OpenAsync();
        var file = await _agents.CreateAsync(Request("claude", [await ProjectAsync("One")]));

        var (token, expiresAt) = await _agents.NewTokenAsync(file.AgentId);
        Assert.NotEqual(file.Token, token);
        Assert.True(expiresAt > file.ExpiresAt);

        await _agents.RevokeAsync(file.AgentId);
        var error = await Assert.ThrowsAsync<ServerApiException>(() => _agents.NewTokenAsync(file.AgentId));
        Assert.Equal((int)HttpStatusCode.Conflict, error.Status);
    }
}
