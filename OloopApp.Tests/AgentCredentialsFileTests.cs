using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using Sodium;
using Xunit;

namespace OloopApp.Tests;

public sealed class AgentCredentialsFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "oloop-agentfile-" + Guid.NewGuid().ToString("N"));

    public AgentCredentialsFileTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string PathOf(string name) => Path.Combine(_folder, name);

    private static AgentCredentials Sample(Guid? id = null)
    {
        var agent = PublicKeyBox.GenerateKeyPair();
        var owner = PublicKeyBox.GenerateKeyPair();
        return new AgentCredentials(
            "https://oloop.example.com", id ?? Guid.NewGuid(), "claude", "token-1", agent.PublicKey, agent.PrivateKey,
            Guid.NewGuid(), owner.PublicKey, new DateTimeOffset(2027, 2, 19, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void The_File_Has_Exactly_The_Fields_Of_The_Contract_With_The_Cli()
    {
        var sample = Sample();
        AgentCredentialsFile.Write(PathOf("claude.json"), sample);

        var node = JsonNode.Parse(File.ReadAllText(PathOf("claude.json")))!.AsObject();

        Assert.Equal(
            ["server", "agentId", "name", "token", "publicKey", "secretKey", "owner", "expiresAt"],
            node.Select(p => p.Key));
        Assert.Equal("https://oloop.example.com", (string?)node["server"]);
        Assert.Equal(sample.AgentId.ToString("D"), (string?)node["agentId"]);
        Assert.Equal("claude", (string?)node["name"]);
        Assert.Equal("2027-02-19T10:00:00Z", (string?)node["expiresAt"]);
        Assert.Equal(["id", "publicKey"], node["owner"]!.AsObject().Select(p => p.Key));
        Assert.Equal(sample.OwnerId.ToString("D"), (string?)node["owner"]!["id"]);
        Assert.False(node.ContainsKey("keyNumber"));
    }

    [Fact]
    public void The_Keys_Are_Base64Url_Without_Padding_Of_32_Bytes()
    {
        var sample = Sample();
        AgentCredentialsFile.Write(PathOf("a.json"), sample);
        var node = JsonNode.Parse(File.ReadAllText(PathOf("a.json")))!;

        foreach (var text in new[] { (string?)node["publicKey"], (string?)node["secretKey"], (string?)node["owner"]!["publicKey"] })
        {
            Assert.DoesNotContain('=', text!);
            Assert.DoesNotContain('+', text);
            Assert.DoesNotContain('/', text);
            Assert.Equal(32, Base64Url.Decode(text).Length);
        }

        Assert.Equal(sample.SecretKey, Base64Url.Decode((string?)node["secretKey"]!));
    }

    [Fact]
    public void The_File_Is_Only_Readable_By_The_Owner_And_Leaves_No_Temporary_File()
    {
        AgentCredentialsFile.Write(PathOf("a.json"), Sample());

        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PathOf("a.json")));
        Assert.Equal(["a.json"], Directory.GetFiles(_folder).Select(Path.GetFileName));
    }

    [Fact]
    public void A_Failed_Write_Leaves_The_Old_File_And_No_Temporary_File()
    {
        // The target is a folder: the move at the end cannot replace it.
        Directory.CreateDirectory(PathOf("target"));

        Assert.ThrowsAny<IOException>(() => AgentCredentialsFile.Write(PathOf("target"), Sample()));

        Assert.True(Directory.Exists(PathOf("target")));
        Assert.Empty(Directory.GetFiles(_folder));
    }

    [Fact]
    public void A_File_That_Exists_Is_Replaced_In_One_Piece()
    {
        File.WriteAllText(PathOf("a.json"), "old");
        var sample = Sample();

        AgentCredentialsFile.Write(PathOf("a.json"), sample);

        var node = JsonNode.Parse(File.ReadAllText(PathOf("a.json")))!;
        Assert.Equal(sample.Token, (string?)node["token"]);
    }

    [Fact]
    public void A_New_Token_Replaces_Token_And_Expiry_And_Keeps_Every_Other_Field()
    {
        var sample = Sample();
        AgentCredentialsFile.Write(PathOf("a.json"), sample);
        var withExtras = JsonNode.Parse(File.ReadAllText(PathOf("a.json")))!.AsObject();
        withExtras["keyNumber"] = 2;
        withExtras["note"] = "kept";
        File.WriteAllText(PathOf("a.json"), withExtras.ToJsonString());

        AgentCredentialsFile.ReplaceToken(
            PathOf("a.json"), sample.AgentId, "token-2", new DateTimeOffset(2027, 5, 1, 0, 0, 0, TimeSpan.Zero));

        var node = JsonNode.Parse(File.ReadAllText(PathOf("a.json")))!.AsObject();
        Assert.Equal("token-2", (string?)node["token"]);
        Assert.Equal("2027-05-01T00:00:00Z", (string?)node["expiresAt"]);
        Assert.Equal("claude", (string?)node["name"]);
        Assert.Equal(2, (int?)node["keyNumber"]);
        Assert.Equal("kept", (string?)node["note"]);
        Assert.Equal(Base64Url.Encode(sample.SecretKey), (string?)node["secretKey"]);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PathOf("a.json")));
    }

    [Fact]
    public void A_File_Of_Another_Agent_Or_Something_Else_Is_Not_Touched()
    {
        var sample = Sample();
        AgentCredentialsFile.Write(PathOf("a.json"), sample);
        var before = File.ReadAllText(PathOf("a.json"));

        Assert.Throws<AgentCredentialsException>(() => AgentCredentialsFile.ReplaceToken(
            PathOf("a.json"), Guid.NewGuid(), "token-2", DateTimeOffset.UtcNow.AddDays(1)));
        Assert.Equal(before, File.ReadAllText(PathOf("a.json")));

        File.WriteAllText(PathOf("b.json"), "not json");
        Assert.Throws<AgentCredentialsException>(() => AgentCredentialsFile.ReplaceToken(
            PathOf("b.json"), sample.AgentId, "token-2", DateTimeOffset.UtcNow.AddDays(1)));
        File.WriteAllText(PathOf("c.json"), "[1]");
        Assert.Throws<AgentCredentialsException>(() => AgentCredentialsFile.ReplaceToken(
            PathOf("c.json"), sample.AgentId, "token-2", DateTimeOffset.UtcNow.AddDays(1)));
    }

    /// <summary>
    /// For the manual check against oloop-cli: with OLOOP_INTEROP_FILE set, writes a file there that
    /// <c>AgentCredentials.from_path</c> of the CLI has to accept. Does nothing otherwise.
    /// </summary>
    [Fact]
    public void Writes_A_File_For_The_Interop_Check_When_Asked()
    {
        var target = Environment.GetEnvironmentVariable("OLOOP_INTEROP_FILE");
        if (string.IsNullOrEmpty(target))
            return;

        AgentCredentialsFile.Write(target, Sample());
    }
}
