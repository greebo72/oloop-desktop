using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;
using Xunit;

namespace OloopApp.Tests;

/// <summary>The images of tasks on the server (SERVER.md, "Images"): shown, and kept when a task is written again.</summary>
public class ServerImageTests
{
    private readonly FakeOloopServer _server = new();

    // A PNG's first bytes are enough for the kind check; the view test uses a real picture.
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5];

    private async Task<ServerOloopStore> OpenStoreAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        return new ServerOloopStore(api,
            new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey));
    }

    /// <summary>
    /// A task that has an image, as an agent would leave it: the blob is on the server, uploaded
    /// for the task, and the task says <c>blob:&lt;id&gt;</c>. The desktop only reads such images.
    /// </summary>
    private async Task<(ServerOloopStore Store, OloopProject Project, OloopTask Task, Guid BlobId)> TaskWithImageAsync(
        byte[]? image = null, int keyVersion = 1, Guid? aadTask = null)
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Garden");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Look at this", "", null, "timo");

        var blobId = Guid.NewGuid();
        var envelope = Envelope.Seal(
            _server.ProjectKey(Guid.Parse(project.Id)), image ?? Png,
            AssociatedData.Blob(blobId, aadTask ?? Guid.Parse(task.Id), keyVersion));
        _server.AddBlob(blobId, Guid.Parse(task.Id), envelope);

        // The agent writes the task with the reference to the blob.
        var written = TaskIo.Read(task.Id,
            TaskIo.Write(task).Replace("imagePath: null", "imagePath: blob:" + blobId.ToString("D")), project.Id);
        await store.WriteTaskAsync(project, written, task.UpdatedAt);
        written.ProjectName = project.Name;
        return (store, project, written, blobId);
    }

    // ---------------- Showing ----------------

    [Fact]
    public async Task The_Image_Of_A_Task_Is_Fetched_And_Decrypted()
    {
        var (store, project, task, _) = await TaskWithImageAsync();

        var opened = await store.OpenImageAsync(project, task);

        Assert.Null(opened.Error);
        using var stream = opened.Stream!;
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        Assert.Equal(Png, copy.ToArray());
    }

    [Fact]
    public async Task The_Same_Image_Is_Not_Fetched_Again_At_Every_Reload()
    {
        var (store, project, task, _) = await TaskWithImageAsync();

        for (var i = 0; i < 4; i++)
        {
            var opened = await store.OpenImageAsync(project, task);
            Assert.NotNull(opened.Stream);
            opened.Stream!.Dispose();
        }

        Assert.Equal(1, _server.BlobFetches);
    }

    [Fact]
    public async Task A_Blob_That_Is_Gone_Is_Reported_As_Missing_And_Is_No_Error()
    {
        var (store, project, task, blobId) = await TaskWithImageAsync();
        _server.RemoveBlob(blobId);

        var opened = await store.OpenImageAsync(project, task);

        Assert.Null(opened.Stream);
        Assert.Contains("missing", opened.Error);
    }

    [Fact]
    public async Task A_Task_Without_A_Blob_Path_Or_With_Another_Kind_Of_Path_Has_No_Image_To_Fetch()
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Garden");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "No picture", "", null, "timo");

        var none = await store.OpenImageAsync(project, task);
        Assert.Contains("not valid", none.Error);

        var folderStyle = TaskIo.Read(task.Id,
            TaskIo.Write(task).Replace("imagePath: null", "imagePath: images/" + task.Id + ".png"), project.Id);
        var other = await store.OpenImageAsync(project, folderStyle);
        Assert.Contains("not valid", other.Error);
        Assert.Equal(0, _server.BlobFetches); // a path that is no blob never reaches the network
    }

    // ---------------- What must not be shown ----------------

    [Fact]
    public async Task An_Image_That_Was_Changed_On_The_Server_Is_Refused()
    {
        var (store, project, task, blobId) = await TaskWithImageAsync();
        var envelope = Envelope.Seal(_server.ProjectKey(Guid.Parse(project.Id)), Png,
            AssociatedData.Blob(blobId, Guid.Parse(task.Id), 1));
        envelope[^1] ^= 0xFF;
        _server.AddBlob(blobId, Guid.Parse(task.Id), envelope);

        var opened = await store.OpenImageAsync(project, task);

        Assert.Null(opened.Stream);
        Assert.Contains("could not be decrypted", opened.Error);
    }

    [Fact]
    public async Task A_Blob_Of_Another_Task_Does_Not_Open_Under_This_One()
    {
        // The server hands out the blob of one task as the image of another: the associated data
        // binds it to its own task, so the tag fails.
        var (store, project, task, _) = await TaskWithImageAsync(aadTask: Guid.NewGuid());

        var opened = await store.OpenImageAsync(project, task);

        Assert.Null(opened.Stream);
        Assert.Contains("could not be decrypted", opened.Error);
    }

    [Fact]
    public async Task A_Blob_That_Is_Not_A_Picture_Is_Not_Handed_To_The_Decoder()
    {
        var (store, project, task, _) = await TaskWithImageAsync(image: "#!/bin/sh\necho hi"u8.ToArray());

        var opened = await store.OpenImageAsync(project, task);

        Assert.Null(opened.Stream);
        Assert.Contains("PNG, JPEG, GIF or WebP", opened.Error);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0 }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0 }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0 }, true)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x38, 0x61, 0 }, false)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x41, 0x56, 0x45 }, false)]
    [InlineData(new byte[] { 0x42, 0x4D, 0, 0 }, false)]
    [InlineData(new byte[0], false)]
    public void The_Kinds_Of_Picture_Are_Told_By_Their_First_Bytes(byte[] bytes, bool known)
        => Assert.Equal(known, ImageKind.IsKnown(bytes));

    // ---------------- Writing a task that has an image ----------------

    [Fact]
    public async Task Writing_A_Task_Again_Keeps_Its_Image_For_The_Server()
    {
        var (store, project, task, blobId) = await TaskWithImageAsync();
        var stored = _server.Stored.Single(d => d.Kind == "task");
        Assert.Equal(blobId, stored.BlobRef); // the write that added the image named the blob

        // The desktop changes the title, closes the task and adds a comment: each is a new revision.
        var edited = await store.EditTaskAsync(task, project, "A new title", null, null, "timo");
        var closed = await store.SetStatusAsync(edited, project, closed: true, "timo");
        await store.AddCommentAsync(closed, project, "Done", "timo");

        var after = _server.Stored.Single(d => d.Kind == "task");
        Assert.True(after.Rev > stored.Rev);
        Assert.Equal(blobId, after.BlobRef); // without the reference the server would drop the blob after 30 days
    }

    [Fact]
    public async Task A_Task_Without_An_Image_Is_Written_Without_A_Reference()
    {
        var store = await OpenStoreAsync();
        var project = await store.CreateProjectAsync("Garden");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Plain", "", null, "timo");
        await store.EditTaskAsync(task, project, "Plainer", null, null, "timo");

        Assert.Null(_server.Stored.Single(d => d.Kind == "task").BlobRef);
    }
}
