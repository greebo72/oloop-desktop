using System.Globalization;
using System.Text;

namespace OloopApp.Core.Crypto;

/// <summary>
/// The associated data of an envelope (SERVER.md, "Envelope"): the bytes
/// <c>v</c> and <c>alg</c>, then every field as UTF-8 text, each preceded by one
/// 0x00 byte. UUIDs are lowercase text, numbers are decimal text. It binds a
/// ciphertext to its item, its key and its revision.
/// </summary>
public static class AssociatedData
{
    public static byte[] Document(string kind, Guid id, int keyVersion, int rev)
        => Build("doc", kind, Text(id), Number(keyVersion), Number(rev));

    public static byte[] Blob(Guid blobId, Guid taskId, int keyVersion)
        => Build("blob", Text(blobId), Text(taskId), Number(keyVersion));

    /// <summary>The wrapped secret key of a user.</summary>
    public static byte[] Key(Guid userId) => Build("key", Text(userId));

    private static byte[] Build(params string[] fields)
    {
        using var stream = new MemoryStream();
        stream.WriteByte(Envelope.Version);
        stream.WriteByte(Envelope.Algorithm);
        foreach (var field in fields)
        {
            stream.WriteByte(0);
            stream.Write(Encoding.UTF8.GetBytes(field));
        }
        return stream.ToArray();
    }

    // "D" is the form with dashes, in lowercase.
    private static string Text(Guid id) => id.ToString("D");

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
