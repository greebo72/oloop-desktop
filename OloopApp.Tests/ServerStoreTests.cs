using System.Text;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;
using Xunit;

namespace OloopApp.Tests;

public class ServerStoreTests
{
    private readonly FakeOloopServer _server = new();

    private async Task<ServerApi> SignInAsync(string password = "secret")
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", password, "test device");
        return api;
    }

    /// <summary>A device that is signed in and has opened its key bundle: the same user on each call.</summary>
    private async Task<ServerOloopStore> OpenStoreAsync()
    {
        var api = await SignInAsync();
        return new ServerOloopStore(api,
            new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey));
    }

    // ---------------- Sign in ----------------

    [Fact]
    public async Task Login_Gives_The_User_Id_And_A_Wrong_Password_Is_Refused()
    {
        var api = await SignInAsync();
        Assert.True(api.SignedIn);
        Assert.Equal(_server.UserId, api.UserId);

        var error = await Assert.ThrowsAsync<ServerApiException>(() => SignInAsync("wrong"));
        Assert.Equal(401, error.Status);
        Assert.Equal("unauthorized", error.Code);
    }

    [Fact]
    public async Task An_Expired_Access_Token_Is_Renewed_Once_And_The_Request_Repeated()
    {
        var api = await SignInAsync();
        var rotated = new List<string>();
        api.RefreshTokenChanged += rotated.Add;
        var before = api.RefreshToken;

        _server.ExpireAccessTokens();
        var response = await api.SendAsync(HttpMethod.Get, "v1/head");

        Assert.Equal(200, response.Status);
        Assert.Equal(1, _server.Refreshes);
        // The new refresh token has to reach the secret store: the old one is used up.
        Assert.Single(rotated);
        Assert.NotEqual(before, api.RefreshToken);
        Assert.Equal(rotated[0], api.RefreshToken);
    }

    [Fact]
    public async Task A_Refresh_That_Fails_Signs_The_Device_Out_With_A_Clear_Message()
    {
        var api = await SignInAsync();
        _server.ExpireAccessTokens();
        // The server has forgotten this device: a second client used its refresh token.
        await api.ResumeAsync(api.RefreshToken!);
        var stale = api.RefreshToken!;
        await api.ResumeAsync(stale);
        await Assert.ThrowsAsync<ServerApiException>(() => api.ResumeAsync(stale));

        Assert.False(api.SignedIn);
        var error = await Assert.ThrowsAsync<ServerApiException>(() => api.SendAsync(HttpMethod.Get, "v1/head"));
        Assert.Equal(401, error.Status);
    }

    [Fact]
    public async Task An_Unreachable_Server_Is_An_IOException()
    {
        var api = await SignInAsync();
        _server.Down = true;

        var error = await Assert.ThrowsAnyAsync<IOException>(() => api.SendAsync(HttpMethod.Get, "v1/head"));
        Assert.Contains("cannot be reached", error.Message);
    }

    // ---------------- Round trip ----------------

    [Fact]
    public async Task What_One_Device_Writes_The_Other_Reads()
    {
        var first = await OpenStoreAsync();
        var project = await first.CreateProjectAsync("Garden", referencePath: "/home/timo/garden");
        var task = await first.CreateTaskAsync(project, TaskType.todo, "Water the plants",
            "Twice a week.\n\nWith *rain water*.", ["home", "plants"], "timo", dueDate: "2026-12-01");
        await first.AddCommentAsync(task, project, "Done on Monday", "timo");

        var second = await OpenStoreAsync();
        var snapshot = await second.LoadAsync();

        var loadedProject = Assert.Single(snapshot.Projects);
        Assert.Equal("Garden", loadedProject.Name);
        Assert.Equal("/home/timo/garden", loadedProject.ReferencePath);
        Assert.Equal(1, loadedProject.TaskCount);

        var loadedTask = Assert.Single(snapshot.Tasks);
        Assert.Equal("Water the plants", loadedTask.Title);
        Assert.Equal("Twice a week.\n\nWith *rain water*.", loadedTask.Description);
        Assert.Equal(["home", "plants"], loadedTask.Labels);
        Assert.Equal("2026-12-01", loadedTask.DueDate);
        Assert.Equal("Done on Monday", Assert.Single(loadedTask.Comments).Body);
        Assert.Equal("Garden", loadedTask.ProjectName);
        Assert.Empty(snapshot.Unreadable);
    }

    [Fact]
    public async Task The_Server_Sees_Only_Ciphertext()
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Secret Project Name");
        await store.CreateTaskAsync(project, TaskType.bug, "Very private title", "Very private text", null, "timo");

        Assert.Equal(2, _server.Stored.Count);
        foreach (var doc in _server.Stored)
        {
            var text = Encoding.UTF8.GetString(doc.Envelope);
            Assert.DoesNotContain("private", text);
            Assert.DoesNotContain("Secret", text);
        }
    }

    [Fact]
    public async Task A_Change_Is_Written_As_The_Next_Revision()
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Garden");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");
        var edited = await store.EditTaskAsync(task, project, "Two", null, null, "timo");
        await store.SetStatusAsync(edited, project, closed: true, "timo");

        var stored = _server.Stored.Single(d => d.Kind == "task");
        Assert.Equal(3, stored.Rev);

        var snapshot = await (await OpenStoreAsync()).LoadAsync();
        var loaded = Assert.Single(snapshot.Tasks);
        Assert.Equal("Two", loaded.Title);
        Assert.True(loaded.IsClosed);
    }

    [Fact]
    public async Task A_Project_Name_Is_Unique_Without_Regard_To_Case()
    {
        var store = await OpenStoreAsync();
        await store.CreateProjectAsync("Garden");

        await Assert.ThrowsAsync<OloopFormatException>(() => store.CreateProjectAsync("garden"));
    }

    // ---------------- Conflicts ----------------

    [Fact]
    public async Task A_Change_Of_Another_Device_Is_A_Conflict_That_Names_Its_Time()
    {
        var mine = await OpenStoreAsync();
        var project = await mine.CreateProjectAsync("Garden");
        var task = await mine.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");

        var other = await OpenStoreAsync();
        var otherSnapshot = await other.LoadAsync();
        var otherTask = otherSnapshot.Tasks.Single();
        var theirs = await other.EditTaskAsync(otherTask, otherSnapshot.Projects.Single(), "Theirs", null, null, "agent");

        var conflict = await Assert.ThrowsAsync<OloopConflictException>(
            () => mine.EditTaskAsync(task, project, "Mine", null, null, "timo"));
        Assert.Equal(theirs.UpdatedAt, conflict.CurrentUpdatedAt);
        Assert.Equal("Theirs", (await (await OpenStoreAsync()).LoadAsync()).Tasks.Single().Title);
    }

    [Fact]
    public async Task Overwrite_Writes_On_Top_Of_The_Current_Revision()
    {
        var mine = await OpenStoreAsync();
        var project = await mine.CreateProjectAsync("Garden");
        var task = await mine.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");

        var other = await OpenStoreAsync();
        var otherSnapshot = await other.LoadAsync();
        await other.EditTaskAsync(otherSnapshot.Tasks.Single(), otherSnapshot.Projects.Single(),
            "Theirs", null, null, "agent");

        var conflict = await Assert.ThrowsAsync<OloopConflictException>(
            () => mine.EditTaskAsync(task, project, "Mine", null, null, "timo"));

        // The user chose overwrite: the same change, expecting what is there now.
        var mineAgain = task.With(title: "Mine", updatedAt: OloopStoreBase.NowUtc(), updatedBy: "timo");
        await mine.WriteTaskAsync(project, mineAgain, conflict.CurrentUpdatedAt);

        Assert.Equal(3, _server.Stored.Single(d => d.Kind == "task").Rev);
        Assert.Equal("Mine", (await (await OpenStoreAsync()).LoadAsync()).Tasks.Single().Title);
    }

    // ---------------- Trust ----------------

    [Fact]
    public async Task A_Changed_Document_Is_Unreadable_And_The_Rest_Still_Loads()
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Garden");
        var good = await store.CreateTaskAsync(project, TaskType.todo, "Good", "", null, "timo");
        var bad = await store.CreateTaskAsync(project, TaskType.todo, "Bad", "", null, "timo");
        _server.Tamper(Guid.Parse(bad.Id));

        var snapshot = await (await OpenStoreAsync()).LoadAsync();

        Assert.Equal(good.Id, Assert.Single(snapshot.Tasks).Id);
        var unreadable = Assert.Single(snapshot.Unreadable);
        Assert.Contains(bad.Id, unreadable.Path);
        Assert.Contains("cannot be decrypted", unreadable.Reason);
    }

    [Fact]
    public async Task A_Key_From_Somebody_Else_Is_Not_Used()
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Garden");
        await store.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");
        _server.SetGrantedBy(Guid.Parse(project.Id), Guid.NewGuid());

        var snapshot = await (await OpenStoreAsync()).LoadAsync();

        Assert.Empty(snapshot.Projects);
        Assert.Empty(snapshot.Tasks);
        Assert.Equal(2, snapshot.Unreadable.Count);
    }

    [Fact]
    public async Task An_Older_Revision_Than_One_Already_Seen_Is_Refused()
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Garden");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");
        var old = _server.Stored.Single(d => d.Kind == "task");
        await store.EditTaskAsync(task, project, "Two", null, null, "timo");
        await store.LoadAsync();

        // The server now shows revision 1 again, a state that was consistent once.
        _server.Shown[old.Id] = old;
        var snapshot = await store.LoadAsync();

        Assert.Empty(snapshot.Tasks);
        Assert.Contains("older state", Assert.Single(snapshot.Unreadable).Reason);
    }

    // ---------------- Change detection ----------------

    [Fact]
    public async Task The_Fingerprint_Follows_The_Server_And_A_Write_Of_Another_Device_Changes_It()
    {
        var mine = await OpenStoreAsync();
        var project = await mine.CreateProjectAsync("Garden");
        await mine.LoadAsync();
        var before = mine.Fingerprint();
        Assert.NotEqual("", before);

        var other = await OpenStoreAsync();
        await other.LoadAsync();
        await other.CreateTaskAsync(project, TaskType.todo, "From elsewhere", "", null, "agent");

        using var source = new ServerChangeSource(mine, TimeSpan.FromMilliseconds(50));
        var changed = new TaskCompletionSource();
        source.Changed += () => changed.TrySetResult();

        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotEqual(before, mine.Fingerprint());
    }

    [Fact]
    public async Task A_Down_Server_Is_Reported_Once_And_A_Return_Reloads()
    {
        var store = await OpenStoreAsync();
        await store.LoadAsync();

        using var source = new ServerChangeSource(store, TimeSpan.FromMilliseconds(50));
        var failures = 0;
        var failed = new TaskCompletionSource();
        var changed = new TaskCompletionSource();
        source.Failed += _ =>
        {
            Interlocked.Increment(ref failures);
            failed.TrySetResult();
        };
        source.Changed += () => changed.TrySetResult();

        _server.Down = true;
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Delay(300);
        Assert.Equal(1, failures);

        _server.Down = false;
        await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }
}
