using System.Text.Json;
using System.Text.Json.Nodes;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The conformance fixtures of the oloop spec. They are the hard contract:
/// a valid fixture must be read exactly as <c>expected.json</c> says, and
/// every invalid fixture must fail with the message the spec names.
/// </summary>
public class FixtureTests
{
    private static string FixturesRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static string FixturePath(string relative)
        => Path.Combine(FixturesRoot, relative.Replace('/', Path.DirectorySeparatorChar));

    [Theory]
    [InlineData("basic")]
    [InlineData("due-date")]
    [InlineData("unknown-fields")]
    public async Task Valid_Fixture_Matches_ExpectedJson(string caseName)
    {
        var root = FixturePath($"valid/{caseName}/root");
        var store = new OloopStore(root);

        var snapshot = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(snapshot.Unreadable);

        var actual = ToJson(snapshot);
        var expected = JsonNode.Parse(
            await File.ReadAllTextAsync(FixturePath($"valid/{caseName}/expected.json"), CancellationToken.None))!;

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"loaded data differs from expected.json{Environment.NewLine}" +
            $"expected: {expected.ToJsonString()}{Environment.NewLine}" +
            $"actual:   {actual.ToJsonString()}");
    }

    [Theory]
    [InlineData("id-differs-from-file-name", "file name")]
    [InlineData("missing-created-by", "createdBy")]
    [InlineData("newer-format-version", "newer than supported")]
    [InlineData("no-frontmatter", "no frontmatter")]
    [InlineData("unknown-type", "type")]
    public async Task Invalid_Fixture_Fails_With_Expected_Message(string caseName, string expectedSubstring)
    {
        var root = FixturePath($"invalid/{caseName}/root");
        var store = new OloopStore(root);

        var snapshot = await store.LoadAsync(CancellationToken.None);

        Assert.NotEmpty(snapshot.Unreadable);
        Assert.Contains(snapshot.Unreadable, u => u.Reason.Contains(expectedSubstring, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("basic")]
    [InlineData("due-date")]
    [InlineData("unknown-fields")]
    public async Task Round_Trip_Changes_No_Value(string caseName)
    {
        var root = FixturePath($"valid/{caseName}/root");
        var store = new OloopStore(root);

        var snapshot = await store.LoadAsync(CancellationToken.None);
        Assert.Empty(snapshot.Unreadable);

        foreach (var task in snapshot.Tasks)
        {
            var project = snapshot.Projects.Single(p => p.Id == task.ProjectId);
            var folder = store.ProjectFolder(project);
            var path = Path.Combine(folder, "tasks", task.Id + ".md");

            var original = await File.ReadAllTextAsync(path, CancellationToken.None);
            var written = TaskIo.Write(task);

            // Writing the loaded task back must produce the same values.
            var reread = TaskIo.Read(task.Id, written, task.ProjectId);

            Assert.Equal(task.Title, reread.Title);
            Assert.Equal(task.Type, reread.Type);
            Assert.Equal(task.Status, reread.Status);
            Assert.Equal(task.CreatedAt, reread.CreatedAt);
            Assert.Equal(task.CreatedBy, reread.CreatedBy);
            Assert.Equal(task.UpdatedAt, reread.UpdatedAt);
            Assert.Equal(task.UpdatedBy, reread.UpdatedBy);
            Assert.Equal(task.ImagePath, reread.ImagePath);
            Assert.Equal(task.DueDate, reread.DueDate);
            Assert.Equal(task.Labels, reread.Labels);
            Assert.Equal(task.Comments.Count, reread.Comments.Count);
            Assert.Equal(task.Description, reread.Description);

            // The task is the same after the round trip, field for field —
            // the due date and the fields this client does not know included.
            // (The project name is filled in by the store, it is not in the file.)
            var before = TaskToJson(task);
            var after = TaskToJson(reread);
            before.Remove("projectName");
            after.Remove("projectName");

            Assert.True(
                JsonNode.DeepEquals(before, after),
                $"round trip changed the task{Environment.NewLine}" +
                $"before: {before.ToJsonString()}{Environment.NewLine}" +
                $"after:  {after.ToJsonString()}");

            // The written file must be stable: writing the re-read task again
            // produces the same bytes (quoting style may differ from the
            // original file — the spec pins the values, not the quoting).
            Assert.Equal(written, TaskIo.Write(reread));
        }
    }

    /// <summary>
    /// A task the way expected.json writes it: the keys of the frontmatter
    /// that are present, the description and the project name. A due date
    /// that is absent or null is left out, a field this client does not know
    /// is written like any other.
    /// </summary>
    private static JsonObject TaskToJson(OloopTask t)
    {
        var comments = new JsonArray();
        foreach (var c in t.Comments)
        {
            var comment = new JsonObject
            {
                ["body"] = c.Body,
                ["createdAt"] = c.CreatedAt,
            };
            foreach (var (key, value) in c.ExtraFields)
                comment[key] = NodeToJson(value);
            comments.Add(comment);
        }

        var task = new JsonObject
        {
            ["formatVersion"] = t.FormatVersion,
            ["id"] = t.Id,
            ["projectId"] = t.ProjectId,
            ["type"] = t.Type.ToString(),
            ["title"] = t.Title,
            ["status"] = t.Status.ToString(),
            ["imagePath"] = t.ImagePath,
        };

        if (t.DueDate is not null)
            task["dueDate"] = t.DueDate;

        task["labels"] = new JsonArray(t.Labels.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
        task["createdAt"] = t.CreatedAt;
        task["createdBy"] = t.CreatedBy;
        task["updatedAt"] = t.UpdatedAt;
        task["updatedBy"] = t.UpdatedBy;
        task["comments"] = comments;

        foreach (var (key, value) in t.ExtraFields)
            task[key] = NodeToJson(value);

        task["description"] = t.Description;
        task["projectName"] = t.ProjectName;
        return task;
    }

    /// <summary>A YAML value as JSON. Scalars stay text: the type of an unknown field is not ours to guess.</summary>
    private static JsonNode? NodeToJson(YamlNode node) => node switch
    {
        YamlNode.Scalar { IsPlainNull: true } => null,
        YamlNode.NullNode => null,
        YamlNode.Scalar scalar => JsonValue.Create(scalar.Value),
        YamlNode.Raw raw => JsonValue.Create(raw.Value),
        YamlNode.Sequence sequence => new JsonArray(sequence.Items.Select(NodeToJson).ToArray()),
        YamlNode.Mapping mapping => new JsonObject(
            mapping.Entries.Select(e => new KeyValuePair<string, JsonNode?>(e.Key, NodeToJson(e.Value)))),
        _ => null,
    };

    private static JsonNode ToJson(OloopSnapshot snapshot)
    {
        var projects = new JsonArray();
        foreach (var p in snapshot.Projects)
        {
            var project = new JsonObject
            {
                ["formatVersion"] = p.FormatVersion,
                ["id"] = p.Id,
                ["name"] = p.Name,
                ["createdAt"] = p.CreatedAt,
                ["referencePath"] = p.ReferencePath,
                ["referenceHost"] = p.ReferenceHost,
            };
            foreach (var (key, value) in p.ExtraFields)
                project[key] = NodeToJson(value);
            projects.Add(project);
        }

        var tasks = new JsonArray();
        foreach (var t in snapshot.Tasks)
            tasks.Add(TaskToJson(t));

        return new JsonObject
        {
            ["projects"] = projects,
            ["tasks"] = tasks,
        };
    }
}
