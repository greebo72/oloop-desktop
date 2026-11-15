using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OloopApp.Core.Server;

/// <summary>
/// The ledger in a file of its own for each account on each server, next to the settings. The
/// numbers are not secret, but a ledger of one account must not be used for another: the file
/// name is made from the server and the user id.
///
/// A file that is missing starts a new ledger. A file that cannot be read (damaged, or of a
/// version this client does not know) does too, and the next save replaces it: the protection
/// starts over, it does not stop the app.
/// </summary>
public sealed class FileRevisionLedger : IRevisionLedger
{
    private const int FormatVersion = 1;
    private readonly string _path;

    public FileRevisionLedger(string path) => _path = path;

    /// <summary>The ledger of one account: <c>revisions/&lt;hash of server and user&gt;.json</c> in the folder.</summary>
    public static FileRevisionLedger For(string folder, string serverUrl, Guid userId)
    {
        var name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(serverUrl + "|" + userId.ToString("D"))))[..24]
            .ToLowerInvariant();
        return new FileRevisionLedger(Path.Combine(folder, "revisions", name + ".json"));
    }

    public IReadOnlyDictionary<Guid, int> Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<Guid, int>();

            using var document = JsonDocument.Parse(File.ReadAllBytes(_path));
            var root = document.RootElement;
            if (!root.TryGetProperty("v", out var version) || version.GetInt32() != FormatVersion)
                return new Dictionary<Guid, int>();

            var result = new Dictionary<Guid, int>();
            foreach (var item in root.GetProperty("docs").EnumerateObject())
            {
                // An entry that is not a revision costs only itself: the rest of the ledger still counts.
                if (Guid.TryParse(item.Name, out var id) && item.Value.ValueKind == JsonValueKind.Number
                    && item.Value.TryGetInt32(out var rev) && rev > 0)
                {
                    result[id] = rev;
                }
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or InvalidOperationException or KeyNotFoundException)
        {
            return new Dictionary<Guid, int>();
        }
    }

    public void Save(IReadOnlyDictionary<Guid, int> revisions)
    {
        var docs = revisions.OrderBy(p => p.Key).ToDictionary(p => p.Key.ToString("D"), p => p.Value);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object> { ["v"] = FormatVersion, ["docs"] = docs });

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);

        // Written to a file next to it and moved into place: a crash in between leaves the old
        // ledger, never half of a new one.
        var temp = _path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, _path, overwrite: true);
    }

    public void Clear()
    {
        try
        {
            File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("The record of seen revisions could not be deleted: " + ex.Message, ex);
        }
    }
}
