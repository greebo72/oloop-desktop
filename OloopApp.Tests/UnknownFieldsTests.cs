using OloopApp.Core.Io;
using OloopApp.Core.Models;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// Fields this client does not know are kept: read, written back unchanged,
/// and still there after every change the app makes (SPEC.md, "Unknown
/// fields"). The due date is a field of its own: a calendar date or nothing.
/// </summary>
public class UnknownFieldsTests : IDisposable
{
    private const string Id = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee";
    private const string ProjectId = "11111111-1111-4111-8111-111111111111";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-unknown-" + Guid.NewGuid().ToString("N"));

    public UnknownFieldsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static string TaskFile(string extraTop = "", string extraComment = "", string dueDate = "") => $"""
        ---
        formatVersion: 1
        id: {Id}
        projectId: {ProjectId}
        type: todo
        title: A task
        status: open
        imagePath: null
        {dueDate}
        labels: []
        createdAt: 2026-01-02T10:00:00Z
        createdBy: alice
        updatedAt: 2026-01-03T11:30:00Z
        updatedBy: alice
        comments:
        - body: First comment
          createdAt: 2026-01-03T11:30:00Z
          {extraComment}
        {extraTop}
        ---
        Body.
        """.Replace("\r\n", "\n") + "\n";

    private static OloopTask Read(string content) => TaskIo.Read(Id, content, ProjectId);

    // ---------------- Reading keeps them ----------------

    [Fact]
    public void A_File_With_Unknown_Fields_Is_Read_Not_Refused()
    {
        var task = Read(TaskFile(extraTop: "priority: high"));

        var field = Assert.Single(task.ExtraFields);
        Assert.Equal("priority", field.Key);
        Assert.Equal("high", Assert.IsType<YamlNode.Scalar>(field.Value).Value);
    }

    [Fact]
    public void The_Order_Of_The_Unknown_Fields_Is_Kept()
    {
        var task = Read(TaskFile(extraTop: "zeta: 1\nalpha: 2\nmid: 3"));

        Assert.Equal(["zeta", "alpha", "mid"], task.ExtraFields.Select(f => f.Key));
    }

    [Fact]
    public void Writing_Puts_Them_After_The_Known_Fields_Unchanged()
    {
        var written = TaskIo.Write(Read(TaskFile(extraTop: "priority: high\nestimate: 3")));

        var lines = written.Split('\n');
        var comments = Array.FindIndex(lines, l => l.StartsWith("comments:"));
        var priority = Array.FindIndex(lines, l => l == "priority: high");
        var estimate = Array.FindIndex(lines, l => l == "estimate: 3");

        Assert.True(priority > comments);
        Assert.True(estimate > priority);
    }

    [Fact]
    public void The_Type_Of_An_Unknown_Scalar_Is_Not_Changed()
    {
        // 42, true and a date are not strings in YAML. The file must not turn
        // them into strings by quoting them.
        var written = TaskIo.Write(Read(TaskFile(
            extraTop: "count: 42\nflag: true\nday: 2026-11-01\nnothing: ~\nquoted: \"42\"\nsingle: 'text'")));

        Assert.Contains("\ncount: 42\n", written);
        Assert.Contains("\nflag: true\n", written);
        Assert.Contains("\nday: 2026-11-01\n", written);
        Assert.Contains("\nnothing: ~\n", written);

        // A quoted number is a string and stays one.
        Assert.Contains("\nquoted: \"42\"\n", written);
        Assert.Contains("\nsingle: text\n", written);
    }

    [Fact]
    public void Nested_Unknown_Values_Survive()
    {
        var content = TaskFile(extraTop: """
            meta:
              owner: bob
              tags:
              - a
              - b
              nested:
                deep:
                - x: 1
                  y: 2
                - z: 3
            matrix:
            - - 1
              - 2
            - - 3
            empty: []
            none: {}
            """);

        var task = Read(content);
        var again = Read(TaskIo.Write(task));

        Assert.Equal(
            task.ExtraFields.Select(f => f.Key),
            again.ExtraFields.Select(f => f.Key));

        // The second write is the same as the first: nothing drifts.
        Assert.Equal(TaskIo.Write(task), TaskIo.Write(again));

        var meta = Assert.IsType<YamlNode.Mapping>(again.ExtraFields.Single(f => f.Key == "meta").Value);
        Assert.Equal("bob", Assert.IsType<YamlNode.Scalar>(meta.Get("owner")).Value);
        var deep = Assert.IsType<YamlNode.Sequence>(
            Assert.IsType<YamlNode.Mapping>(meta.Get("nested")).Get("deep"));
        Assert.Equal(2, deep.Items.Count);
        Assert.IsType<YamlNode.Sequence>(again.ExtraFields.Single(f => f.Key == "matrix").Value);
    }

    [Fact]
    public void A_Key_That_Needs_Quotes_Is_Written_Quoted()
    {
        var task = Read(TaskFile(extraTop: "\"on\": 1\n\"a key\": 2"));

        var again = Read(TaskIo.Write(task));

        Assert.Equal(["on", "a key"], again.ExtraFields.Select(f => f.Key));
    }

    [Fact]
    public void Unknown_Fields_Of_A_Comment_Survive()
    {
        var task = Read(TaskFile(extraComment: "author: robot"));

        var field = Assert.Single(Assert.Single(task.Comments).ExtraFields);
        Assert.Equal("author", field.Key);

        var again = Read(TaskIo.Write(task));
        var kept = Assert.Single(Assert.Single(again.Comments).ExtraFields);
        Assert.Equal("author", kept.Key);
        Assert.Equal("robot", Assert.IsType<YamlNode.Scalar>(kept.Value).Value);
    }

    [Fact]
    public void Unknown_Fields_Of_A_Project_Survive()
    {
        var content = $"""
            ---
            formatVersion: 1
            id: {ProjectId}
            name: Example Project
            createdAt: 2026-01-01T09:00:00Z
            color: green
            ---

            """.Replace("\r\n", "\n");

        var project = ProjectIo.Read(content);

        var field = Assert.Single(project.ExtraFields);
        Assert.Equal("color", field.Key);

        var written = ProjectIo.Write(project);
        Assert.Contains("\ncolor: green\n", written);
        Assert.Equal("color", Assert.Single(ProjectIo.Read(written).ExtraFields).Key);
    }

    [Fact]
    public void A_Newer_Format_Version_Is_Still_Refused()
    {
        var content = TaskFile(extraTop: "priority: high").Replace("formatVersion: 1", "formatVersion: 2");

        Assert.Throws<OloopFormatException>(() => Read(content));
    }

    // ---------------- The app keeps them when it changes a task ----------------

    private async Task<(OloopStore Store, OloopProject Project, OloopTask Task)> StoreWithExtrasAsync()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "A task", "Body", [], "claude");

        // A newer client adds fields behind the back of this one.
        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var text = await File.ReadAllTextAsync(path);
        text = text.Replace("\n---\n", "\npriority: high\nmeta:\n  owner: bob\n---\n");
        await File.WriteAllTextAsync(path, text);

        var loaded = (await store.LoadAsync()).Tasks.Single();
        return (store, project, loaded);
    }

    private async Task<string> FileAsync(OloopTask task)
        => await File.ReadAllTextAsync(Path.Combine(_root, "my-project", "tasks", task.Id + ".md"));

    [Fact]
    public async Task Editing_A_Task_Keeps_The_Unknown_Fields()
    {
        var (store, project, task) = await StoreWithExtrasAsync();
        Assert.Equal(2, task.ExtraFields.Count);

        await store.EditTaskAsync(task, project, "New title", "New body", ["x"], "claude");

        var file = await FileAsync(task);
        Assert.Contains("priority: high", file);
        Assert.Contains("owner: bob", file);
    }

    [Fact]
    public async Task Closing_A_Task_Keeps_The_Unknown_Fields()
    {
        var (store, project, task) = await StoreWithExtrasAsync();

        await store.SetStatusAsync(task, project, closed: true, "claude");

        var file = await FileAsync(task);
        Assert.Contains("status: closed", file);
        Assert.Contains("priority: high", file);
    }

    [Fact]
    public async Task Adding_A_Comment_Keeps_The_Unknown_Fields()
    {
        var (store, project, task) = await StoreWithExtrasAsync();

        await store.AddCommentAsync(task, project, "A comment", "claude");

        var file = await FileAsync(task);
        Assert.Contains("A comment", file);
        Assert.Contains("priority: high", file);
    }

    [Fact]
    public async Task A_Task_With_Unknown_Fields_Is_Listed_Not_Unreadable()
    {
        var (store, _, _) = await StoreWithExtrasAsync();

        var snapshot = await store.LoadAsync();

        Assert.Empty(snapshot.Unreadable);
        Assert.Single(snapshot.Tasks);
    }

    // ---------------- The due date ----------------

    [Fact]
    public void A_Due_Date_Is_Read_As_Text()
    {
        Assert.Equal("2026-11-01", Read(TaskFile(dueDate: "dueDate: 2026-11-01")).DueDate);
        Assert.Equal("2026-11-01", Read(TaskFile(dueDate: "dueDate: \"2026-11-01\"")).DueDate);
    }

    [Fact]
    public void Absent_And_Null_Both_Mean_No_Due_Date()
    {
        Assert.Null(Read(TaskFile()).DueDate);
        Assert.Null(Read(TaskFile(dueDate: "dueDate: null")).DueDate);
        Assert.Null(Read(TaskFile(dueDate: "dueDate: ~")).DueDate);
    }

    [Theory]
    [InlineData("dueDate: 2026-02-30")]
    [InlineData("dueDate: 2026-13-01")]
    [InlineData("dueDate: 01.11.2026")]
    [InlineData("dueDate: 2026-11-01T10:00:00Z")]
    [InlineData("dueDate: soon")]
    [InlineData("dueDate: 20261101")]
    public void An_Invalid_Due_Date_Makes_The_File_Unreadable(string line)
    {
        var ex = Assert.Throws<OloopFormatException>(() => Read(TaskFile(dueDate: line)));

        Assert.Contains("dueDate", ex.Message);
    }

    [Fact]
    public void A_Due_Date_That_Is_Not_A_Scalar_Is_Refused()
    {
        var ex = Assert.Throws<OloopFormatException>(() => Read(TaskFile(dueDate: "dueDate: [2026-11-01]")));

        Assert.Contains("dueDate", ex.Message);
    }

    [Fact]
    public void Writing_Leaves_The_Due_Date_Out_When_There_Is_None()
    {
        var written = TaskIo.Write(Read(TaskFile(dueDate: "dueDate: null")));

        Assert.DoesNotContain("dueDate", written);
    }

    [Fact]
    public void Writing_Puts_The_Due_Date_Right_After_The_Image()
    {
        var written = TaskIo.Write(Read(TaskFile(dueDate: "dueDate: 2026-11-01")));

        var lines = written.Split('\n');
        var image = Array.FindIndex(lines, l => l.StartsWith("imagePath:"));

        Assert.StartsWith("dueDate:", lines[image + 1]);
        Assert.Equal("2026-11-01", Read(written).DueDate);
    }

    [Fact]
    public async Task Edit_Close_And_Comment_Keep_The_Due_Date()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("My Project");
        var task = await store.CreateTaskAsync(project, TaskType.todo, "A task", "", [], "claude");

        var path = Path.Combine(_root, "my-project", "tasks", task.Id + ".md");
        var text = await File.ReadAllTextAsync(path);
        await File.WriteAllTextAsync(path, text.Replace("labels:", "dueDate: 2026-11-01\nlabels:"));

        var loaded = (await store.LoadAsync()).Tasks.Single();
        Assert.Equal("2026-11-01", loaded.DueDate);

        var edited = await store.EditTaskAsync(loaded, project, "New title", "", [], "claude");
        var closed = await store.SetStatusAsync(edited, project, closed: true, "claude");
        await store.AddCommentAsync(closed, project, "Done", "claude");

        var after = (await store.LoadAsync()).Tasks.Single();
        Assert.Equal("2026-11-01", after.DueDate);
        Assert.Equal(TaskStatus.closed, after.Status);
    }
}
