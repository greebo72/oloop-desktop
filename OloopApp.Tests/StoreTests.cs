using OloopApp.Core.Io;
using OloopApp.Core.Models;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

public class StoreTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private OloopStore Store => new(_root);

    [Fact]
    public async Task CreateProject_Writes_ProjectMd_And_Tasks_Folder()
    {
        var store = Store;
        var project = await store.CreateProjectAsync("My Project");

        Assert.Equal("my-project", project.FolderName);
        Assert.True(File.Exists(Path.Combine(_root, "my-project", "project.md")));
        Assert.True(Directory.Exists(Path.Combine(_root, "my-project", "tasks")));
    }

    [Fact]
    public async Task CreateProject_Refuses_A_Duplicate_Name()
    {
        var store = Store;
        await store.CreateProjectAsync("My Project");

        await Assert.ThrowsAsync<OloopFormatException>(() => store.CreateProjectAsync("my project"));
    }

    [Fact]
    public async Task CreateTask_Writes_A_Readable_File()
    {
        var store = Store;
        var project = await store.CreateProjectAsync("My Project");

        var task = await store.CreateTaskAsync(
            project, TaskType.todo, "First task", "Some **description**", ["docs"], "claude");

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        Assert.True(File.Exists(path));

        var reread = TaskIo.Read(task.Id, await File.ReadAllTextAsync(path), project.Id);
        Assert.Equal("First task", reread.Title);
        Assert.Equal("Some **description**", reread.Description);
        Assert.Equal(["docs"], reread.Labels);
        Assert.Equal(TaskStatus.open, reread.Status);
        Assert.Equal("claude", reread.CreatedBy);
    }

    [Fact]
    public async Task EditTask_Updates_Fields_And_Bumps_UpdatedAt()
    {
        var store = Store;
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Title", "Body", [], "alice");

        var edited = await store.EditTaskAsync(task, project, "New title", "New body", ["x"], "bob");

        Assert.Equal("New title", edited.Title);
        Assert.Equal("bob", edited.UpdatedBy);
        Assert.Equal("alice", edited.CreatedBy);
        // Timestamps have second precision, so the edit may land in the same
        // second as the creation. The actor always changes.
        Assert.True(string.CompareOrdinal(edited.UpdatedAt, task.UpdatedAt) >= 0, "updatedAt must not go back");
    }

    [Fact]
    public async Task SetStatus_Closes_And_Reopens()
    {
        var store = Store;
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.question, "Title", "", [], "alice");

        var closed = await store.SetStatusAsync(task, project, closed: true, "alice");
        Assert.Equal(TaskStatus.closed, closed.Status);

        var reopened = await store.SetStatusAsync(closed, project, closed: false, "alice");
        Assert.Equal(TaskStatus.open, reopened.Status);
    }

    [Fact]
    public async Task AddComment_Appends()
    {
        var store = Store;
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Title", "", [], "alice");

        var withComment = await store.AddCommentAsync(task, project, "First", "alice");

        Assert.Single(withComment.Comments);
        Assert.Equal("First", withComment.Comments[0].Body);
    }

    [Fact]
    public async Task Write_Creates_A_Backup_Of_The_Previous_File()
    {
        var store = Store;
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Title", "", [], "alice");

        await store.EditTaskAsync(task, project, "Changed", "", null, "alice");

        var backup = Path.Combine(_root, "my-project", ".backup", task.Id + ".md.bak");
        Assert.True(File.Exists(backup));
        Assert.Contains("title: Title", await File.ReadAllTextAsync(backup));
    }

    [Fact]
    public async Task Write_Refuses_When_The_File_Changed_On_Disk()
    {
        var store = Store;
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "Title", "", [], "alice");

        // Someone else writes the file.
        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var foreign = TaskIo.Write(task.With(
            title: "Foreign title",
            updatedAt: "2030-01-01T00:00:00Z",
            updatedBy: "someone"));
        await File.WriteAllTextAsync(path, foreign);

        await Assert.ThrowsAsync<OloopConflictException>(
            () => store.EditTaskAsync(task, project, "My title", "", null, "alice"));
    }

    [Fact]
    public async Task Load_Reports_Unreadable_Files_Instead_Of_Failing()
    {
        var store = Store;
        await store.CreateProjectAsync("My Project");

        var broken = Path.Combine(_root, "my-project", "tasks", "broken.md");
        await File.WriteAllTextAsync(broken, "no frontmatter here");

        var snapshot = await store.LoadAsync();

        Assert.Single(snapshot.Unreadable);
        Assert.Contains("no frontmatter", snapshot.Unreadable[0].Reason);
        Assert.Empty(snapshot.Tasks);
    }

    [Fact]
    public void Slugify_Folds_To_Lowercase_Ascii()
    {
        Assert.Equal("my-project", OloopStore.Slugify("My Project"));
        Assert.Equal("server-haus", OloopStore.Slugify("Server-Haus"));
        Assert.Equal("strasse", OloopStore.Slugify("Straße"));
    }

    [Fact]
    public void NormalizeLabels_Trims_Drops_Empty_And_Deduplicates()
    {
        var labels = OloopStore.NormalizeLabels([" a ", "", "a", "b"]);

        Assert.Equal(["a", "b"], labels);
    }
}
