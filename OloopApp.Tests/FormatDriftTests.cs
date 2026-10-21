using System.Diagnostics;
using System.Text;
using System.Text.Json;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// The format drift check against the reference Python CLI.
///
/// The CLI is the second implementation of the same spec. A value that one
/// implementation writes and the other reads back differently is a drift —
/// and the drift is invisible to the fixtures, because the fixtures hold
/// friendly values only.
///
/// The test writes the values that YAML 1.1 resolves to another type
/// (<c>on</c>, <c>null</c>, <c>12:30</c>, a date) and checks that the CLI
/// reads them back unchanged. Then the CLI writes a task and this client has
/// to read it back unchanged.
/// </summary>
public class FormatDriftTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "oloop-drift-" + Guid.NewGuid().ToString("N"));

    private readonly string _cliRoot = Path.Combine(
        Path.GetTempPath(), "oloop-drift-cli-" + Guid.NewGuid().ToString("N"));

    public FormatDriftTests()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_cliRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);

        if (Directory.Exists(_cliRoot))
            Directory.Delete(_cliRoot, recursive: true);
    }

    // ---------------- The values that break the naive writers ----------------

    /// <summary>
    /// The titles that YAML 1.1 resolves to something else: a boolean, a
    /// null, a number or a timestamp. A plain scalar with one of these texts
    /// changes its type on the way back in.
    ///
    /// The list is the edge cases of the resolver patterns of PyYAML, not a
    /// guess: every value here is resolved to a bool, a None, an int, a
    /// float or a date by <c>yaml.safe_load</c>.
    /// </summary>
    public static TheoryData<string> HostileTitles() => new()
    {
        // Booleans.
        "on", "off", "On", "ON", "OFF",
        "yes", "no", "Yes", "YES", "No", "NO",
        "true", "false", "True", "TRUE", "False", "FALSE",

        // Nulls.
        "~", "null", "Null", "NULL",

        // Integers.
        "42", "-7", "+7", "0", "007",
        "0x1f", "0b101", "017",
        "1_000",
        "12:30", "12:30:15", "1:2:3", "+12:30",

        // Floats.
        "3.14", ".5", "1.5e+5", "12:30.5", "1_000.5",
        ".inf", ".Inf", ".INF", "-.inf", "+.inf",
        ".nan", ".NaN", ".NAN",

        // Timestamps.
        "2026-10-20",
        "2026-10-20T10:00:00Z",
        "2026-10-20 10:00:00",
        "2026-10-20t10:00:00",
        "2026-10-20T10:00:00.123Z",
        "2026-10-20T10:00:00+02:00",
    };

    /// <summary>
    /// The values that look like the hostile ones but that the resolver does
    /// not touch. They must survive the round trip as well — and they are the
    /// ones that show whether the quoting is too eager.
    /// </summary>
    public static TheoryData<string> HarmlessTitles() => new()
    {
        "12:60",
        "0:30",
        "0o17",
        "1e5",
        "2026-1-1",
        "on the way",
        "null pointer",
        "yes man",
        "42ever",
    };

    // ---------------- The round trip through the CLI ----------------

    [CliFact]
    public async Task The_CLI_Reads_Every_Title_That_This_Client_Writes()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Drift");

        var written = new List<(string Title, string Id)>();
        foreach (var title in HostileTitles())
        {
            var task = await store.CreateTaskAsync(
                project, TaskType.todo, title, "description", ["label"], "claude");

            written.Add((title, task.Id));
        }

        // The CLI reads the data root and hands the tasks back as JSON.
        var json = RunCli("task", "list", "--json");
        using var document = JsonDocument.Parse(json);
        var tasks = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(written.Count, tasks.Count);

        foreach (var (title, id) in written)
        {
            var read = tasks.Single(t => t.GetProperty("id").GetString() == id);

            // The title is the same string — not a boolean, not a number, not
            // a date. That is the whole point of the quoting.
            Assert.Equal(title, read.GetProperty("title").GetString());
            Assert.Equal("todo", read.GetProperty("type").GetString());
            Assert.Equal("open", read.GetProperty("status").GetString());
            Assert.Equal("claude", read.GetProperty("createdBy").GetString());
            Assert.Equal("label", read.GetProperty("labels")[0].GetString());
        }
    }

    [CliFact]
    public async Task The_CLI_Reads_Every_Title_That_Looks_Harmless_Too()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Drift");

        var written = new List<(string Title, string Id)>();
        foreach (var title in HarmlessTitles())
        {
            var task = await store.CreateTaskAsync(
                project, TaskType.todo, title, "description", [], "claude");

            written.Add((title, task.Id));
        }

        using var document = JsonDocument.Parse(RunCli("task", "list", "--json"));
        var tasks = document.RootElement.EnumerateArray().ToList();

        Assert.Equal(written.Count, tasks.Count);

        foreach (var (title, id) in written)
        {
            var read = tasks.Single(t => t.GetProperty("id").GetString() == id);
            Assert.Equal(title, read.GetProperty("title").GetString());
        }
    }

    [CliFact]
    public async Task The_CLI_Reads_A_Title_With_A_Line_Break()
    {
        var store = new OloopStore(_root);
        var project = await store.CreateProjectAsync("Drift");

        var title = "First line\nSecond line";
        var task = await store.CreateTaskAsync(
            project, TaskType.todo, title, "description", [], "claude");

        using var document = JsonDocument.Parse(RunCli("task", "list", "--json"));
        var read = document.RootElement.EnumerateArray()
            .Single(t => t.GetProperty("id").GetString() == task.Id);

        Assert.Equal(title, read.GetProperty("title").GetString());
    }

    [CliFact]
    public async Task The_CLI_Reads_A_Project_Without_Reference_Fields()
    {
        var store = new OloopStore(_root);
        await store.CreateProjectAsync("No Reference");

        // The reference fields are omitted, not written as null — the schema
        // types them as a string.
        var projectFile = Path.Combine(_root, "no-reference", "project.md");
        var text = await File.ReadAllTextAsync(projectFile);
        Assert.DoesNotContain("referencePath", text, StringComparison.Ordinal);
        Assert.DoesNotContain("referenceHost", text, StringComparison.Ordinal);

        var json = RunCli("project", "list", "--json");
        using var document = JsonDocument.Parse(json);
        var projects = document.RootElement.EnumerateArray().ToList();

        var project = Assert.Single(projects);
        Assert.Equal("No Reference", project.GetProperty("name").GetString());
    }

    [CliFact]
    public async Task This_Client_Reads_Everything_The_CLI_Writes()
    {
        // The CLI writes a task with a hostile title of its own.
        var title = "on";
        RunCliIn(_cliRoot, "project", "add", "CLI Project");
        var output = RunCliIn(_cliRoot, "task", "add", "-p", "CLI Project", "-t", "bug", title, "-d", "body", "-l", "x");
        Assert.Contains("Task created", output, StringComparison.Ordinal);

        var store = new OloopStore(_cliRoot);
        var snapshot = await store.LoadAsync();

        Assert.Empty(snapshot.Unreadable);

        var task = Assert.Single(snapshot.Tasks);
        Assert.Equal(title, task.Title);
        Assert.Equal(TaskType.bug, task.Type);
        Assert.Equal("x", Assert.Single(task.Labels));
        Assert.Equal("body", task.Description);
        Assert.Equal("claude", task.CreatedBy);

        var project = Assert.Single(snapshot.Projects);
        Assert.Equal("CLI Project", project.Name);
    }

    [CliFact]
    public async Task A_Change_Made_By_The_CLI_Is_Read_By_This_Client()
    {
        RunCliIn(_cliRoot, "project", "add", "CLI Project");
        RunCliIn(_cliRoot, "task", "add", "-p", "CLI Project", "-t", "todo", "A task");

        // The CLI writes the file again — this time with its own YAML writer.
        using (var document = JsonDocument.Parse(RunCliIn(_cliRoot, "task", "list", "--json")))
        {
            var id = document.RootElement[0].GetProperty("id").GetString()!;
            RunCliIn(_cliRoot, "comment", "add", id, "A comment");
        }

        var store = new OloopStore(_cliRoot);
        var snapshot = await store.LoadAsync();

        Assert.Empty(snapshot.Unreadable);

        var task = Assert.Single(snapshot.Tasks);
        Assert.Equal("A task", task.Title);
        Assert.Equal("A comment", Assert.Single(task.Comments).Body);
    }

    [CliFact]
    public async Task The_Frontmatter_Key_Order_Is_The_Same_In_Both_Implementations()
    {
        // The order the CLI writes (yaml.safe_dump with sort_keys=False over
        // the dict of Task.frontmatter()).
        RunCliIn(_cliRoot, "project", "add", "CLI Project");
        RunCliIn(_cliRoot, "task", "add", "-p", "CLI Project", "-t", "todo", "A task");

        var cliFile = Directory
            .EnumerateFiles(Path.Combine(_cliRoot, "cli-project", "tasks"), "*.md")
            .Single();

        var cliKeys = ReadKeys(await File.ReadAllTextAsync(cliFile));

        // The order this client writes.
        var task = new OloopTask
        {
            FormatVersion = 1,
            Id = "22222222-2222-4222-8222-222222222222",
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = TaskType.todo,
            Title = "A task",
            Status = TaskStatus.open,
            CreatedAt = "2026-01-01T09:00:00Z",
            CreatedBy = "claude",
            UpdatedAt = "2026-01-01T09:00:00Z",
            UpdatedBy = "claude",
        };

        var ourKeys = ReadKeys(TaskIo.Write(task));

        Assert.Equal(cliKeys, ourKeys);
        Assert.Equal("formatVersion", cliKeys[0]);
    }

    [CliFact]
    public async Task The_Project_Key_Order_Is_The_Same_In_Both_Implementations()
    {
        RunCliIn(_cliRoot, "project", "add", "CLI Project");

        var cliKeys = ReadKeys(
            await File.ReadAllTextAsync(Path.Combine(_cliRoot, "cli-project", "project.md")));

        var ourKeys = ReadKeys(ProjectIo.Write(new OloopProject
        {
            FormatVersion = 1,
            Id = "11111111-1111-4111-8111-111111111111",
            Name = "Mine",
            CreatedAt = "2026-01-01T09:00:00Z",
        }));

        Assert.Equal(cliKeys, ourKeys);
    }

    [CliFact]
    public async Task The_Schemas_Of_The_CLI_Are_The_Schemas_Of_The_Spec()
    {
        var cliSchemas = Cli.SchemaFolder();
        Assert.NotNull(cliSchemas);

        foreach (var name in new[] { "project.schema.json", "task.schema.json" })
        {
            var cli = await File.ReadAllTextAsync(Path.Combine(cliSchemas!, name));
            var spec = await File.ReadAllTextAsync(
                Path.Combine(AppContext.BaseDirectory, "schemas", name));

            Assert.Equal(spec.Trim(), cli.Trim());
        }
    }

    // ---------------- Helpers ----------------

    /// <summary>The keys of the frontmatter, in the order they are written.</summary>
    private static List<string> ReadKeys(string file)
    {
        var keys = new List<string>();
        var inFrontmatter = false;

        foreach (var line in file.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');

            if (!inFrontmatter)
            {
                if (trimmed == "---")
                    inFrontmatter = true;

                continue;
            }

            if (trimmed == "---")
                break;

            if (trimmed.Length == 0)
                continue;

            var colon = trimmed.IndexOf(':');
            if (colon < 0)
                continue;

            var key = trimmed[..colon].Trim();

            // Only the top level keys — the comments are nested.
            if (trimmed.StartsWith(' ') || trimmed.StartsWith('\t'))
                continue;

            keys.Add(key);
        }

        return keys;
    }

    /// <summary>Runs the CLI against the test data root and returns its output.</summary>
    private string RunCli(params string[] arguments)
        => RunCliIn(_root, arguments);

    /// <summary>Runs the CLI against the given data root.</summary>
    private string RunCliIn(string root, params string[] arguments)
    {
        var result = Cli.Run(arguments, root, "claude");
        Assert.True(result.Success, $"oloop {string.Join(' ', arguments)} failed: {result.Error}");
        return result.Output;
    }
}

/// <summary>
/// A fact that needs the reference Python CLI. It is skipped when the CLI is
/// not installed on this machine — the drift check is a check of two
/// implementations, and a missing implementation is not a drift.
/// </summary>
public sealed class CliFactAttribute : FactAttribute
{
    public CliFactAttribute()
    {
        if (!Cli.IsAvailable)
            Skip = "the reference oloop CLI is not installed on this machine";
    }
}

/// <summary>
/// The reference Python CLI: where it is, how to run it and where its schemas
/// live.
/// </summary>
public static class Cli
{
    /// <summary>The path of the <c>oloop</c> executable, or null.</summary>
    public static string? Executable { get; } = Find();

    /// <summary>Whether the CLI is installed on this machine.</summary>
    public static bool IsAvailable => Executable is not null;

    /// <summary>
    /// Runs the CLI. The root and the actor are passed as arguments, so the
    /// environment of the test run does not matter.
    ///
    /// The arguments are given as a list: a value with a space in it stays
    /// one argument, and the shell never sees it.
    /// </summary>
    public static (bool Success, string Output, string Error) Run(
        IReadOnlyList<string> arguments, string root, string actor)
    {
        var start = new ProcessStartInfo
        {
            FileName = Executable!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        start.ArgumentList.Add("--root");
        start.ArgumentList.Add(root);
        start.ArgumentList.Add("--actor");
        start.ArgumentList.Add(actor);

        foreach (var part in arguments)
            start.ArgumentList.Add(part);

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException("the oloop CLI could not be started");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode == 0, output, error);
    }

    /// <summary>
    /// The folder with the schemas of the installed CLI, or null when the
    /// layout is not the one of a virtual environment.
    /// </summary>
    public static string? SchemaFolder()
    {
        if (Executable is null)
            return null;

        // The executable is a script; its first line names the interpreter of
        // the virtual environment it belongs to.
        var firstLine = File.ReadLines(Executable).FirstOrDefault();
        if (firstLine is null || !firstLine.StartsWith("#!"))
            return null;

        var python = firstLine[2..].Trim();
        var environment = Path.GetDirectoryName(Path.GetDirectoryName(python));
        if (environment is null)
            return null;

        foreach (var lib in Directory.EnumerateDirectories(Path.Combine(environment, "lib")))
        {
            var schemas = Path.Combine(lib, "site-packages", "oloop", "schemas");
            if (Directory.Exists(schemas))
                return schemas;
        }

        return null;
    }

    private static string? Find()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var separator = OperatingSystem.IsWindows() ? ';' : ':';

        foreach (var folder in path.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = OperatingSystem.IsWindows() ? "oloop.exe" : "oloop";
            var candidate = Path.Combine(folder, name);

            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
