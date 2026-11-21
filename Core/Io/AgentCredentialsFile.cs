using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;

namespace OloopApp.Core.Io;

/// <summary>
/// What the file of an agent holds (AGENTS.md, "The credentials file"). The CLI reads it with
/// <c>AgentCredentials.from_dict</c>, so the shape is the contract with it.
/// </summary>
public sealed record AgentCredentials(
    string Server,
    Guid AgentId,
    string Name,
    string Token,
    byte[] PublicKey,
    byte[] SecretKey,
    Guid OwnerId,
    byte[] OwnerPublicKey,
    DateTimeOffset ExpiresAt);

public class AgentCredentialsException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>
/// Writes and changes the credentials file of an agent: UTF-8 JSON, mode <c>0600</c> (on Windows an
/// access list for the current user only), written to a temporary file in the same folder and then
/// moved over the target, so a reader never sees half a file.
/// </summary>
public static class AgentCredentialsFile
{
    /// <summary>Writes the file. A file that exists is replaced: the caller asked the user before.</summary>
    public static void Write(string path, AgentCredentials credentials)
        => WriteNode(path, ToNode(credentials));

    /// <summary>
    /// Puts a new token into the file of an existing agent and keeps every other field, those this
    /// app does not know included. Throws when the file is not that agent's.
    /// </summary>
    public static void ReplaceToken(string path, Guid agentId, string token, DateTimeOffset expiresAt)
    {
        JsonObject node;
        try
        {
            node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new AgentCredentialsException("this is not a credentials file of an agent");
        }
        catch (JsonException ex)
        {
            throw new AgentCredentialsException("this is not a credentials file of an agent", ex);
        }

        if (!Guid.TryParse(node["agentId"]?.GetValue<string>(), out var found) || found != agentId)
            throw new AgentCredentialsException("this file belongs to another agent");

        node["token"] = token;
        node["expiresAt"] = Format(expiresAt);
        WriteNode(path, node);
    }

    /// <summary>
    /// Checks that the file is the credentials file of this agent, without changing it. Done before a
    /// new token is issued, so that a token is never issued for a file that cannot take it.
    /// </summary>
    public static void CheckBelongsTo(string path, Guid agentId)
    {
        try
        {
            var node = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new AgentCredentialsException("this is not a credentials file of an agent");
            if (!Guid.TryParse(node["agentId"]?.GetValue<string>(), out var found) || found != agentId)
                throw new AgentCredentialsException("this file belongs to another agent");
        }
        catch (JsonException ex)
        {
            throw new AgentCredentialsException("this is not a credentials file of an agent", ex);
        }
    }

    internal static JsonObject ToNode(AgentCredentials c) => new()
    {
        ["server"] = c.Server,
        ["agentId"] = c.AgentId.ToString("D"),
        ["name"] = c.Name,
        ["token"] = c.Token,
        ["publicKey"] = Base64Url.Encode(c.PublicKey),
        ["secretKey"] = Base64Url.Encode(c.SecretKey),
        ["owner"] = new JsonObject
        {
            ["id"] = c.OwnerId.ToString("D"),
            ["publicKey"] = Base64Url.Encode(c.OwnerPublicKey),
        },
        ["expiresAt"] = Format(c.ExpiresAt),
    };

    internal static string Format(DateTimeOffset moment)
        => moment.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static void WriteNode(string path, JsonObject node)
    {
        var target = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(folder);

        var text = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        var temporary = Path.Combine(folder, $".{Path.GetFileName(target)}.tmp.{Guid.NewGuid():N}");
        try
        {
            // The mode is set before the secret is written, not after.
            using (var stream = new FileStream(temporary, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            }))
            {
                if (OperatingSystem.IsWindows())
                    RestrictToCurrentUser(stream);
                using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
                writer.Write(text);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, target, overwrite: true);
        }
        catch
        {
            try { File.Delete(temporary); } catch (IOException) { }
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static void RestrictToCurrentUser(FileStream stream)
    {
        // Through the open handle: the file is opened without sharing, a second open would fail.
        var security = stream.GetAccessControl();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var me = WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
        stream.SetAccessControl(security);
    }
}
