using System.Security.Cryptography;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.Core.Server;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

public class FolderImportTests : IDisposable
{
    private readonly FakeOloopServer _server = new();
    private readonly string _folder = Path.Combine(
        Path.GetTempPath(), "oloop-import-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private async Task<ServerOloopStore> OpenStoreAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        return new ServerOloopStore(api,
            new UnlockedUser(api.UserId, _server.UserKeys.PrivateKey, _server.UserKeys.PublicKey));
    }

    // ---------------- The source folder ----------------

    private static OloopProject Project(string name, int formatVersion = 1) => new()
    {
        FormatVersion = formatVersion,
        Id = Guid.NewGuid().ToString("D"),
        Name = name,
        CreatedAt = "2026-10-16T19:18:14Z",
        ReferencePath = "/Users/timo/dev/" + name,
        ReferenceHost = "MacBookAir.fritz.box",
    };

    private static OloopTask Task_(OloopProject project, string title, string? imagePath = null, string? description = null) => new()
    {
        FormatVersion = 1,
        Id = Guid.NewGuid().ToString("D"),
        ProjectId = project.Id,
        Type = TaskType.feature,
        Title = title,
        Status = TaskStatus.closed,
        ImagePath = imagePath,
        DueDate = "2026-12-01",
        Labels = ["spec", "server"],
        CreatedAt = "2026-10-16T19:20:00Z",
        CreatedBy = "claude",
        UpdatedAt = "2026-10-17T08:00:00Z",
        UpdatedBy = "Timo",
        Comments = [new TaskComment { Body = "first comment", CreatedAt = "2026-10-17T08:00:00Z" }],
        Description = description ?? "A description\n\n- one\n- two",
    };

    private string Write(string slug, OloopProject project, params OloopTask[] tasks)
    {
        var dir = Path.Combine(_folder, slug);
        Directory.CreateDirectory(Path.Combine(dir, "tasks"));
        File.WriteAllText(Path.Combine(dir, "project.md"), ProjectIo.Write(project));
        foreach (var task in tasks)
            File.WriteAllText(Path.Combine(dir, "tasks", task.Id + ".md"), TaskIo.Write(task));
        return dir;
    }

    private string HashOfFolder()
    {
        var lines = Directory.EnumerateFiles(_folder, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => p + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
        return string.Join("\n", lines);
    }

    // ---------------- Tests ----------------

    [Fact]
    public async Task A_New_Project_Is_Written_With_Its_Id_And_Its_Tasks_Unchanged()
    {
        var project = Project("oloop-spec");
        var tasks = new[] { Task_(project, "one"), Task_(project, "two"), Task_(project, "three") };
        Write("oloop-spec", project, tasks);
        var store = await OpenStoreAsync();

        var plan = await FolderImport.PlanAsync(_folder, store);

        var row = Assert.Single(plan.Projects);
        Assert.Equal(ImportAction.Create, row.Action);
        Assert.Equal(3, row.ToWrite.Count);
        Assert.Equal(1, plan.ProjectsToWrite);
        Assert.Equal(3, plan.TasksToWrite);

        var report = await FolderImport.RunAsync(plan, store);

        Assert.Equal(3, report.Written);
        Assert.Equal(0, report.Failed);

        var onServer = await (await OpenStoreAsync()).LoadAsync();
        var imported = Assert.Single(onServer.Projects);
        Assert.Equal(project.Id, imported.Id);
        Assert.Equal(project.CreatedAt, imported.CreatedAt);
        Assert.Equal(project.ReferencePath, imported.ReferencePath);
        Assert.Equal(project.ReferenceHost, imported.ReferenceHost);
        Assert.Equal(3, onServer.Tasks.Count);
        foreach (var task in tasks)
        {
            var back = Assert.Single(onServer.Tasks, t => t.Id == task.Id);
            Assert.Equal(task.ProjectId, back.ProjectId);
            Assert.Equal(task.CreatedAt, back.CreatedAt);
            Assert.Equal(task.CreatedBy, back.CreatedBy);
            Assert.Equal(task.UpdatedAt, back.UpdatedAt);
            Assert.Equal(task.UpdatedBy, back.UpdatedBy);
            Assert.Equal(task.Status, back.Status);
            Assert.Equal(task.DueDate, back.DueDate);
            Assert.Equal(task.Labels, back.Labels);
            Assert.Equal(task.Description, back.Description);
            Assert.Equal(task.Comments.Single().Body, back.Comments.Single().Body);
        }
    }

    [Fact]
    public async Task A_Second_Run_Has_Nothing_To_Write_And_Changes_Nothing()
    {
        var project = Project("oloop-ios");
        Write("oloop-ios", project, Task_(project, "one"), Task_(project, "two"));
        var store = await OpenStoreAsync();
        await FolderImport.RunAsync(await FolderImport.PlanAsync(_folder, store), store);
        var requestsBefore = _server.Requests;

        var plan = await FolderImport.PlanAsync(_folder, store);

        Assert.Equal(ImportAction.NothingToDo, Assert.Single(plan.Projects).Action);
        Assert.All(plan.Projects.Single().Skipped, s => Assert.Equal(SkipReason.Exists, s.Reason));
        Assert.Equal(0, plan.TasksToWrite);
        var report = await FolderImport.RunAsync(plan, store);
        Assert.Empty(report.Projects);
        Assert.Equal(2, (await (await OpenStoreAsync()).LoadAsync()).Tasks.Count);
        Assert.True(_server.Requests > requestsBefore); // the plan reads, but writes nothing
    }

    [Fact]
    public async Task Only_The_Missing_Tasks_Of_A_Project_That_Exists_Are_Written()
    {
        var project = Project("oloop-android");
        var tasks = new[] { Task_(project, "one"), Task_(project, "two"), Task_(project, "three") };
        Write("oloop-android", project, tasks);
        var store = await OpenStoreAsync();
        await store.CreateProjectAsync(project);
        await store.WriteTaskAsync(project, tasks[0], null);
        await store.WriteTaskAsync(project, tasks[1], null);

        var plan = await FolderImport.PlanAsync(_folder, store);

        var row = Assert.Single(plan.Projects);
        Assert.Equal(ImportAction.AddMissing, row.Action);
        Assert.Equal(tasks[2].Id, Assert.Single(row.ToWrite).Id);
        Assert.Equal(2, row.Skipped.Count(s => s.Reason == SkipReason.Exists));

        var report = await FolderImport.RunAsync(plan, store);

        Assert.Equal(1, report.Written);
        Assert.False(report.Projects.Single().Created);
        Assert.Equal(3, (await (await OpenStoreAsync()).LoadAsync()).Tasks.Count);
    }

    [Fact]
    public async Task A_Task_That_The_Server_Has_In_Another_Project_Is_Skipped_And_No_Project_Is_Made_For_It()
    {
        // The folder "OloopApp" was imported earlier as the project "oloop-desktop", with new ids.
        var serverProject = Project("oloop-desktop");
        var folderProject = Project("OloopApp");
        var task = Task_(serverProject, "shared");
        var store = await OpenStoreAsync();
        await store.CreateProjectAsync(serverProject);
        await store.WriteTaskAsync(serverProject, task, null);
        var copy = Task_(folderProject, "shared");
        Write("oloop-desktop", folderProject, new OloopTask
        {
            FormatVersion = 1, Id = task.Id, ProjectId = folderProject.Id, Type = copy.Type, Title = copy.Title,
            Status = copy.Status, CreatedAt = copy.CreatedAt, CreatedBy = copy.CreatedBy,
            UpdatedAt = copy.UpdatedAt, UpdatedBy = copy.UpdatedBy,
        });

        var plan = await FolderImport.PlanAsync(_folder, store);

        var row = Assert.Single(plan.Projects);
        Assert.Equal(ImportAction.NothingToDo, row.Action);
        var skipped = Assert.Single(row.Skipped);
        Assert.Equal(SkipReason.ExistsElsewhere, skipped.Reason);
        Assert.Contains("oloop-desktop", skipped.Detail);
        Assert.Equal(0, plan.ProjectsToWrite);
        Assert.Single((await store.LoadAsync()).Projects);
    }

    [Theory]
    [InlineData("oloop-spec")]
    [InlineData("OLOOP-SPEC")]
    public async Task Another_Project_Id_With_The_Same_Name_Is_A_Conflict_That_The_User_Has_To_Choose(string name)
    {
        var onServer = Project("oloop-spec");
        var store = await OpenStoreAsync();
        await store.CreateProjectAsync(onServer);
        var project = Project(name);
        var task = Task_(project, "one");
        Write("spec", project, task);

        var plan = await FolderImport.PlanAsync(_folder, store);

        var row = Assert.Single(plan.Projects);
        Assert.Equal(ImportAction.Conflict, row.Action);
        Assert.Contains(onServer.Id, row.ConflictWith);
        Assert.Equal(onServer.Id, row.MergeInto!.Id);
        // The tasks are ready for the project of the server, with its id.
        var ready = Assert.Single(row.ToWrite);
        Assert.Equal(task.Id, ready.Id);
        Assert.Equal(onServer.Id, ready.ProjectId);
        Assert.True(row.HasWork);
        // Never chosen by itself: the same name may be another project.
        Assert.False(row.Selected);
        Assert.Equal(0, plan.TasksToWrite);
        Assert.Equal(0, plan.ProjectsToWrite);
    }

    [Fact]
    public async Task A_Conflict_That_The_User_Chooses_Adds_The_Tasks_To_The_Project_Of_The_Server_And_Is_Idempotent()
    {
        var onServer = Project("oloop-android");
        var store = await OpenStoreAsync();
        await store.CreateProjectAsync(onServer);
        var project = Project("oloop-android");
        var tasks = new[] { Task_(project, "one"), Task_(project, "two") };
        Write("oloop-android", project, tasks);

        var plan = await FolderImport.PlanAsync(_folder, store);
        plan.Projects.Single().Selected = true;
        Assert.Equal(2, plan.TasksToWrite);

        var report = await FolderImport.RunAsync(plan, store);

        Assert.Equal(2, report.Written);
        Assert.False(report.Projects.Single().Created);
        var onServerNow = await (await OpenStoreAsync()).LoadAsync();
        Assert.Single(onServerNow.Projects);
        Assert.Equal(onServer.Id, Assert.Single(onServerNow.Projects).Id);
        foreach (var task in tasks)
        {
            var back = Assert.Single(onServerNow.Tasks, t => t.Id == task.Id);
            Assert.Equal(onServer.Id, back.ProjectId);
            Assert.Equal(task.CreatedAt, back.CreatedAt);
            Assert.Equal(task.CreatedBy, back.CreatedBy);
            Assert.Equal(task.Comments.Single().Body, back.Comments.Single().Body);
        }

        // A second run finds everything in the project of the server: nothing to do, nothing written.
        var again = await FolderImport.PlanAsync(_folder, await OpenStoreAsync());
        Assert.Equal(ImportAction.NothingToDo, again.Projects.Single().Action);
        Assert.All(again.Projects.Single().Skipped, x => Assert.Equal(SkipReason.Exists, x.Reason));
    }

    [Fact]
    public async Task A_Conflict_Skips_A_Task_That_The_Server_Has_In_A_Third_Project()
    {
        var onServer = Project("oloop-android");
        var third = Project("somewhere-else");
        var store = await OpenStoreAsync();
        await store.CreateProjectAsync(onServer);
        await store.CreateProjectAsync(third);
        var project = Project("oloop-android");
        var elsewhere = Task_(third, "lives in a third project");
        await store.WriteTaskAsync(third, elsewhere, null);
        var fresh = Task_(project, "new");
        Write("oloop-android", project, fresh, elsewhere.InProject(project.Id, project.Name));

        var plan = await FolderImport.PlanAsync(_folder, store);

        var row = Assert.Single(plan.Projects);
        Assert.Equal(fresh.Id, Assert.Single(row.ToWrite).Id);
        var skipped = Assert.Single(row.Skipped);
        Assert.Equal(SkipReason.ExistsElsewhere, skipped.Reason);
        Assert.Contains("somewhere-else", skipped.Detail);
    }

    [Fact]
    public async Task A_Task_With_An_Image_Is_Skipped_And_The_Others_Are_Written()
    {
        var project = Project("oloop-cli");
        var withImage = Task_(project, "has a picture", imagePath: "images/a.png");
        var plain = Task_(project, "plain");
        Write("oloop-cli", project, withImage, plain);
        var store = await OpenStoreAsync();

        var plan = await FolderImport.PlanAsync(_folder, store);
        var row = Assert.Single(plan.Projects);
        Assert.Equal(withImage.Id, Assert.Single(row.Skipped).TaskId);
        Assert.Equal(SkipReason.HasImage, row.Skipped.Single().Reason);

        await FolderImport.RunAsync(plan, store);

        var tasks = (await (await OpenStoreAsync()).LoadAsync()).Tasks;
        Assert.Equal(plain.Id, Assert.Single(tasks).Id);
    }

    [Fact]
    public async Task Files_Without_FormatVersion_Are_Imported_As_Version_1()
    {
        var project = Project("demo");
        var task = Task_(project, "old");
        var dir = Write("demo", project, task);
        // The files of the first versions of oloop have no formatVersion.
        var projectFile = Path.Combine(dir, "project.md");
        File.WriteAllText(projectFile, string.Join("\n",
            File.ReadAllText(projectFile).Split('\n').Where(l => !l.StartsWith("formatVersion"))));
        var taskFile = Path.Combine(dir, "tasks", task.Id + ".md");
        File.WriteAllText(taskFile, string.Join("\n",
            File.ReadAllText(taskFile).Split('\n').Where(l => !l.StartsWith("formatVersion"))));
        var store = await OpenStoreAsync();

        var plan = await FolderImport.PlanAsync(_folder, store);
        await FolderImport.RunAsync(plan, store);

        var onServer = await (await OpenStoreAsync()).LoadAsync();
        Assert.Equal(1, Assert.Single(onServer.Projects).FormatVersion);
        Assert.Equal(1, Assert.Single(onServer.Tasks).FormatVersion);
    }

    [Fact]
    public async Task A_File_That_Cannot_Be_Read_Is_Listed_And_Does_Not_Stop_The_Others()
    {
        var project = Project("oloop-spec");
        var good = Task_(project, "good");
        var dir = Write("oloop-spec", project, good);
        File.WriteAllText(Path.Combine(dir, "tasks", Guid.NewGuid() + ".md"), "no frontmatter here");
        var store = await OpenStoreAsync();

        var plan = await FolderImport.PlanAsync(_folder, store);

        Assert.Single(plan.Unreadable);
        Assert.Equal(good.Id, Assert.Single(plan.Projects.Single().ToWrite).Id);
    }

    [Fact]
    public async Task A_Task_Over_The_Limit_Is_Skipped_Without_A_Request()
    {
        var project = Project("oloop-spec");
        var small = Task_(project, "small");
        var big = Task_(project, "big", description: new string('x', 5000));
        Write("oloop-spec", project, small, big);
        var store = await OpenStoreAsync();

        var plan = await FolderImport.PlanAsync(_folder, store, maxDocumentBytes: 2000);

        var row = Assert.Single(plan.Projects);
        Assert.Equal(small.Id, Assert.Single(row.ToWrite).Id);
        var skipped = Assert.Single(row.Skipped);
        Assert.Equal(big.Id, skipped.TaskId);
        Assert.Equal(SkipReason.TooLarge, skipped.Reason);
    }

    [Fact]
    public async Task A_Refused_Task_Is_Reported_The_Rest_Is_Written_And_A_Later_Run_Adds_It()
    {
        var project = Project("oloop-spec");
        Write("oloop-spec", project, Task_(project, "one"), Task_(project, "two"), Task_(project, "three"));
        var store = await OpenStoreAsync();
        var plan = await FolderImport.PlanAsync(_folder, store);
        var refused = plan.Projects.Single().ToWrite[1];

        // The third write of the run fails: the project is the first, then the tasks one and two.
        _server.FailDocumentPutNumber = _server.DocumentPuts + 3;
        var report = await FolderImport.RunAsync(plan, store);

        var entry = Assert.Single(report.Projects);
        Assert.Equal(2, entry.Written);
        var failed = Assert.Single(entry.Failed);
        Assert.Equal(refused.Id, failed.TaskId);
        Assert.Equal(refused.Title, failed.Title);

        var again = await FolderImport.PlanAsync(_folder, await OpenStoreAsync());
        Assert.Equal(refused.Id, Assert.Single(again.Projects.Single().ToWrite).Id);
    }

    [Fact]
    public async Task When_The_Project_Cannot_Be_Made_None_Of_Its_Tasks_Is_Tried()
    {
        var project = Project("oloop-spec");
        Write("oloop-spec", project, Task_(project, "one"), Task_(project, "two"));
        var store = await OpenStoreAsync();
        var plan = await FolderImport.PlanAsync(_folder, store);
        var putsBefore = _server.DocumentPuts;

        _server.FailDocumentPutNumber = putsBefore + 1;
        var report = await FolderImport.RunAsync(plan, store);

        var entry = Assert.Single(report.Projects);
        Assert.NotNull(entry.ProjectError);
        Assert.Equal(0, entry.Written);
        Assert.Empty(entry.Failed);
        // Only the attempt to make the project; no task was tried.
        Assert.Equal(putsBefore + 1, _server.DocumentPuts);
    }

    [Fact]
    public async Task A_Task_That_Another_Client_Wrote_Meanwhile_Counts_As_There_Not_As_A_Failure()
    {
        var project = Project("oloop-spec");
        var one = Task_(project, "one");
        Write("oloop-spec", project, one);
        var store = await OpenStoreAsync();
        var plan = await FolderImport.PlanAsync(_folder, store);

        // Another device writes the project and the task after the plan was made.
        var other = await OpenStoreAsync();
        await other.CreateProjectAsync(project);
        await other.WriteTaskAsync(project, one, null);
        var existing = plan.Projects.Single();
        var addMissing = new ImportPlan
        {
            Projects = [new ImportProjectPlan
            {
                Project = existing.Project, Action = ImportAction.AddMissing, TaskCount = existing.TaskCount,
                ToWrite = existing.ToWrite, Skipped = existing.Skipped,
            }],
            Unreadable = [],
        };
        await store.LoadAsync();

        var report = await FolderImport.RunAsync(addMissing, store);

        var entry = Assert.Single(report.Projects);
        Assert.Equal(1, entry.AlreadyThere);
        Assert.Equal(0, entry.Written);
        Assert.Empty(entry.Failed);
    }

    [Fact]
    public async Task The_Source_Folder_Is_Byte_For_Byte_The_Same_After_The_Plan_And_The_Run()
    {
        var project = Project("oloop-spec");
        Write("oloop-spec", project, Task_(project, "one"), Task_(project, "two"));
        var before = HashOfFolder();
        var store = await OpenStoreAsync();

        var plan = await FolderImport.PlanAsync(_folder, store);
        Assert.Equal(before, HashOfFolder());

        await FolderImport.RunAsync(plan, store);
        Assert.Equal(before, HashOfFolder());
    }

    [Fact]
    public async Task The_Plan_Writes_Nothing_To_The_Server()
    {
        var project = Project("oloop-spec");
        Write("oloop-spec", project, Task_(project, "one"));
        var store = await OpenStoreAsync();
        var puts = _server.DocumentPuts;

        await FolderImport.PlanAsync(_folder, store);

        Assert.Equal(puts, _server.DocumentPuts);
        Assert.Empty((await store.LoadAsync()).Projects);
    }

    [Fact]
    public async Task A_Project_That_Is_Unticked_Is_Not_Written()
    {
        var a = Project("a");
        var b = Project("b");
        Write("a", a, Task_(a, "one"));
        Write("b", b, Task_(b, "two"));
        var store = await OpenStoreAsync();
        var plan = await FolderImport.PlanAsync(_folder, store);
        plan.Projects.Single(p => p.Project.Name == "b").Selected = false;

        Assert.Equal(1, plan.ProjectsToWrite);
        Assert.Equal(1, plan.TasksToWrite);
        await FolderImport.RunAsync(plan, store);

        Assert.Equal("a", Assert.Single((await (await OpenStoreAsync()).LoadAsync()).Projects).Name);
    }

    [Fact]
    public async Task Cancelling_Stops_After_The_Current_Task_And_Keeps_What_Was_Written()
    {
        var project = Project("oloop-spec");
        Write("oloop-spec", project, Task_(project, "one"), Task_(project, "two"), Task_(project, "three"));
        var store = await OpenStoreAsync();
        var plan = await FolderImport.PlanAsync(_folder, store);
        using var cts = new CancellationTokenSource();
        var progress = new Progress1(p => { if (p.Done == 0) cts.Cancel(); });

        var report = await FolderImport.RunAsync(plan, store, progress, cts.Token);

        Assert.True(report.Cancelled);
        Assert.Equal(1, report.Written);
        Assert.Single((await (await OpenStoreAsync()).LoadAsync()).Tasks);
    }

    private sealed class Progress1(Action<ImportProgress> handler) : IProgress<ImportProgress>
    {
        public void Report(ImportProgress value) => handler(value);
    }

    [Fact]
    public async Task CreateProject_With_A_Given_Id_Refuses_An_Id_Or_Name_That_Exists()
    {
        var store = await OpenStoreAsync();
        var project = Project("oloop-spec");
        await store.CreateProjectAsync(project);

        await Assert.ThrowsAsync<OloopFormatException>(() => store.CreateProjectAsync(project));
        await Assert.ThrowsAsync<OloopFormatException>(() => store.CreateProjectAsync(Project("OLOOP-SPEC")));
        await Assert.ThrowsAsync<OloopFormatException>(() => store.CreateProjectAsync(
            new OloopProject { FormatVersion = 1, Id = "not-a-uuid", Name = "x", CreatedAt = "2026-10-16T19:18:14Z" }));
    }
}
