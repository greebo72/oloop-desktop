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

    [Fact]
    public async Task Valid_Basic_Matches_ExpectedJson()
    {
        var root = FixturePath("valid/basic/root");
        var store = new OloopStore(root);

        var snapshot = await store.LoadAsync(CancellationToken.None);

        Assert.Empty(snapshot.Unreadable);

        var actual = ToJson(snapshot);
        var expected = JsonNode.Parse(
            await File.ReadAllTextAsync(FixturePath("valid/basic/expected.json"), CancellationToken.None))!;

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

    [Fact]
    public async Task Round_Trip_Changes_No_Value()
    {
        var root = FixturePath("valid/basic/root");
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
            Assert.Equal(task.Labels, reread.Labels);
            Assert.Equal(task.Comments.Count, reread.Comments.Count);
            Assert.Equal(task.Description, reread.Description);

            // The written file must be stable: writing the re-read task again
            // produces the same bytes (quoting style may differ from the
            // original file — the spec pins the values, not the quoting).
            Assert.Equal(written, TaskIo.Write(reread));
        }
    }

    private static JsonNode ToJson(OloopSnapshot snapshot)
    {
        var projects = new JsonArray();
        foreach (var p in snapshot.Projects)
        {
            projects.Add(new JsonObject
            {
                ["formatVersion"] = p.FormatVersion,
                ["id"] = p.Id,
                ["name"] = p.Name,
                ["createdAt"] = p.CreatedAt,
                ["referencePath"] = p.ReferencePath,
                ["referenceHost"] = p.ReferenceHost,
            });
        }

        var tasks = new JsonArray();
        foreach (var t in snapshot.Tasks)
        {
            var comments = new JsonArray();
            foreach (var c in t.Comments)
            {
                comments.Add(new JsonObject
                {
                    ["body"] = c.Body,
                    ["createdAt"] = c.CreatedAt,
                });
            }

            tasks.Add(new JsonObject
            {
                ["formatVersion"] = t.FormatVersion,
                ["id"] = t.Id,
                ["projectId"] = t.ProjectId,
                ["type"] = t.Type.ToString(),
                ["title"] = t.Title,
                ["status"] = t.Status.ToString(),
                ["imagePath"] = t.ImagePath,
                ["labels"] = new JsonArray(t.Labels.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray()),
                ["createdAt"] = t.CreatedAt,
                ["createdBy"] = t.CreatedBy,
                ["updatedAt"] = t.UpdatedAt,
                ["updatedBy"] = t.UpdatedBy,
                ["comments"] = comments,
                ["description"] = t.Description,
                ["projectName"] = t.ProjectName,
            });
        }

        return new JsonObject
        {
            ["projects"] = projects,
            ["tasks"] = tasks,
        };
    }
}
