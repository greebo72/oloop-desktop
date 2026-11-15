using OloopApp.Core.Models;
using OloopApp.Core.Server;
using Xunit;

namespace OloopApp.Tests;

public class RevisionLedgerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oloop-ledger-" + Guid.NewGuid().ToString("N"));
    private readonly FakeOloopServer _server = new();

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    // ---------------- The file ----------------

    [Fact]
    public void What_Was_Saved_Is_Read_Back_And_A_Missing_File_Is_An_Empty_Ledger()
    {
        var ledger = FileRevisionLedger.For(_dir, "https://oloop.example", Guid.NewGuid());
        Assert.Empty(ledger.Load());

        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        ledger.Save(new Dictionary<Guid, int> { [a] = 3, [b] = 11 });

        var read = ledger.Load();
        Assert.Equal(3, read[a]);
        Assert.Equal(11, read[b]);
        Assert.Equal(2, read.Count);
    }

    [Fact]
    public void A_Save_Leaves_No_Temporary_File_And_Replaces_The_Earlier_Ledger()
    {
        var ledger = FileRevisionLedger.For(_dir, "https://oloop.example", Guid.NewGuid());
        var id = Guid.NewGuid();
        ledger.Save(new Dictionary<Guid, int> { [id] = 1 });
        ledger.Save(new Dictionary<Guid, int> { [id] = 2 });

        Assert.Equal(2, ledger.Load()[id]);
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "revisions"), "*.tmp"));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "revisions")));
    }

    [Fact]
    public void Each_Account_On_Each_Server_Has_A_Ledger_Of_Its_Own()
    {
        var user = Guid.NewGuid();
        var one = FileRevisionLedger.For(_dir, "https://one.example", user);
        var other = FileRevisionLedger.For(_dir, "https://two.example", user);
        var otherUser = FileRevisionLedger.For(_dir, "https://one.example", Guid.NewGuid());
        var id = Guid.NewGuid();

        one.Save(new Dictionary<Guid, int> { [id] = 5 });

        Assert.Empty(other.Load());
        Assert.Empty(otherUser.Load());
        Assert.Equal(5, FileRevisionLedger.For(_dir, "https://one.example", user).Load()[id]);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{\"v\":2,\"docs\":{}}")]
    [InlineData("{\"v\":1}")]
    [InlineData("")]
    public void A_File_That_Cannot_Be_Read_Is_An_Empty_Ledger_And_The_Next_Save_Replaces_It(string content)
    {
        var ledger = FileRevisionLedger.For(_dir, "https://oloop.example", Guid.NewGuid());
        ledger.Save(new Dictionary<Guid, int>());
        var path = Directory.GetFiles(Path.Combine(_dir, "revisions")).Single();
        File.WriteAllText(path, content);

        Assert.Empty(ledger.Load());

        var id = Guid.NewGuid();
        ledger.Save(new Dictionary<Guid, int> { [id] = 4 });
        Assert.Equal(4, ledger.Load()[id]);
    }

    [Fact]
    public void Entries_That_Are_No_Revisions_Are_Dropped_And_Clear_Deletes_The_File()
    {
        var ledger = FileRevisionLedger.For(_dir, "https://oloop.example", Guid.NewGuid());
        var id = Guid.NewGuid();
        ledger.Save(new Dictionary<Guid, int> { [id] = 2 });
        var path = Directory.GetFiles(Path.Combine(_dir, "revisions")).Single();
        File.WriteAllText(path,
            "{\"v\":1,\"docs\":{" +
            $"\"{id}\":2,\"not-a-guid\":9,\"{Guid.NewGuid()}\":0,\"{Guid.NewGuid()}\":\"x\"" +
            "}}");

        Assert.Equal(new[] { id }, ledger.Load().Keys.ToArray());

        ledger.Clear();
        Assert.Empty(ledger.Load());
        ledger.Clear(); // not there any more: no error
    }

    // ---------------- In the store ----------------

    private async Task<(ServerApi Api, UnlockedUser User)> SignInAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        return (api, new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey));
    }

    private async Task<ServerOloopStore> OpenAsync(IRevisionLedger ledger)
    {
        var (api, user) = await SignInAsync();
        return new ServerOloopStore(api, user, ledger);
    }

    /// <summary>A task that has been written twice, so that the server can be made to show revision 1 again.</summary>
    private async Task<FakeOloopServer.StoredDocument> TaskWithTwoRevisionsAsync(ServerOloopStore store)
    {
        var project = await store.CreateProjectAsync("Garden");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");
        var first = _server.Stored.Single(d => d.Kind == "task");
        await store.EditTaskAsync(task, project, "Two", null, null, "timo");
        await store.LoadAsync();
        return first;
    }

    [Fact]
    public async Task A_Rollback_Is_Found_After_A_Restart_Because_The_Ledger_Remembers()
    {
        var ledger = new MemoryRevisionLedger();
        var first = await TaskWithTwoRevisionsAsync(await OpenAsync(ledger));

        // The app is closed and opened again: a new store, which has seen nothing in this run.
        var restarted = await OpenAsync(ledger);
        _server.Shown[first.Id] = first;
        var snapshot = await restarted.LoadAsync();

        Assert.Empty(snapshot.Tasks);
        Assert.Contains("older state", Assert.Single(snapshot.Unreadable).Reason);
    }

    [Fact]
    public async Task Without_A_Ledger_A_Restart_Forgets_And_Accepts_The_Old_State()
    {
        var first = await TaskWithTwoRevisionsAsync(await OpenAsync(new MemoryRevisionLedger()));

        var restarted = await OpenAsync(new MemoryRevisionLedger()); // nothing kept
        _server.Shown[first.Id] = first;
        var snapshot = await restarted.LoadAsync();

        Assert.Single(snapshot.Tasks); // this is what the ledger is for
        Assert.Equal("One", snapshot.Tasks[0].Title);
    }

    [Fact]
    public async Task Loads_And_Writes_Keep_The_Highest_Revision_In_The_Ledger()
    {
        var ledger = new MemoryRevisionLedger();
        var store = await OpenAsync(ledger);
        var project = await store.CreateProjectAsync("Garden");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");

        var kept = ledger.Load();
        Assert.Equal(1, kept[Guid.Parse(project.Id)]);
        Assert.Equal(1, kept[Guid.Parse(task.Id)]);

        await store.EditTaskAsync(task, project, "Two", null, null, "timo");
        Assert.Equal(2, ledger.Load()[Guid.Parse(task.Id)]);
    }

    [Fact]
    public async Task A_Ledger_Never_Goes_Down()
    {
        var ledger = new MemoryRevisionLedger();
        var first = await TaskWithTwoRevisionsAsync(await OpenAsync(ledger));
        var id = first.Id;
        Assert.Equal(2, ledger.Load()[id]);

        var store = await OpenAsync(ledger);
        _server.Shown[id] = first;
        await store.LoadAsync();

        Assert.Equal(2, ledger.Load()[id]);
    }

    [Fact]
    public async Task Forgetting_Accepts_The_Older_State_Of_A_Restored_Server()
    {
        var ledger = new MemoryRevisionLedger();
        var first = await TaskWithTwoRevisionsAsync(await OpenAsync(ledger));

        var store = await OpenAsync(ledger);
        _server.Shown[first.Id] = first;
        Assert.Empty((await store.LoadAsync()).Tasks);

        store.ForgetRevisions();

        Assert.Empty(ledger.Load());
        var snapshot = await store.LoadAsync();
        Assert.Equal("One", Assert.Single(snapshot.Tasks).Title);
        Assert.Equal(1, ledger.Load()[first.Id]); // and from here on the older state is the one that counts
    }

    [Fact]
    public async Task A_Ledger_That_Cannot_Be_Written_Does_Not_Stop_A_Load_Or_A_Write()
    {
        var store = await OpenAsync(new FailingLedger());

        var project = await store.CreateProjectAsync("Garden");
        await store.CreateTaskAsync(project, TaskType.todo, "One", "", null, "timo");
        var snapshot = await store.LoadAsync();

        Assert.Single(snapshot.Tasks);
    }

    private sealed class FailingLedger : IRevisionLedger
    {
        public IReadOnlyDictionary<Guid, int> Load() => new Dictionary<Guid, int>();

        public void Save(IReadOnlyDictionary<Guid, int> revisions) => throw new IOException("disk full");

        public void Clear() => throw new IOException("disk full");
    }
}
