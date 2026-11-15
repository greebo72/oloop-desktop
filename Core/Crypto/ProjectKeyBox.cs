using System.Buffers.Binary;
using System.Security.Cryptography;
using Sodium;

namespace OloopApp.Core.Crypto;

/// <summary>
/// Handing a project key to a principal (SERVER.md, "Handing a project key to a
/// principal"): <c>nonce(24) | crypto_box_easy(message, nonce, recipientPublicKey,
/// senderSecretKey)</c> with <c>message = projectKey(32) | projectId(16) |
/// keyVersion(4, big endian)</c>.
///
/// The box is authenticated, so only the holder of the sender's secret key can
/// make it. The server knows every public key and could otherwise hand out a key
/// of its own. The public key of the sender therefore never comes from the answer
/// of the server alone: it is the own key, or one that was pinned.
/// </summary>
public static class ProjectKeyBox
{
    public const int KeyBytes = 32;
    public const int NonceBytes = 24;
    private const int MessageBytes = KeyBytes + GuidBytes.Length + 4;
    private const int MacBytes = 16;

    public const int WrappedBytes = NonceBytes + MessageBytes + MacBytes;

    public static byte[] Wrap(byte[] projectKey, Guid projectId, int keyVersion,
        byte[] recipientPublicKey, byte[] senderSecretKey)
        => Wrap(projectKey, projectId, keyVersion, recipientPublicKey, senderSecretKey,
            SodiumCore.GetRandomBytes(NonceBytes));

    /// <summary>For the tests, which need the same bytes as the test vectors.</summary>
    internal static byte[] Wrap(byte[] projectKey, Guid projectId, int keyVersion,
        byte[] recipientPublicKey, byte[] senderSecretKey, byte[] nonce)
    {
        if (projectKey.Length != KeyBytes)
            throw new ArgumentException($"the project key must be {KeyBytes} bytes", nameof(projectKey));

        var message = new byte[MessageBytes];
        projectKey.CopyTo(message, 0);
        GuidBytes.ToBytes(projectId).CopyTo(message, KeyBytes);
        BinaryPrimitives.WriteUInt32BigEndian(message.AsSpan(KeyBytes + GuidBytes.Length), (uint)keyVersion);

        var box = PublicKeyBox.Create(message, nonce, senderSecretKey, recipientPublicKey);
        var wrapped = new byte[NonceBytes + box.Length];
        nonce.CopyTo(wrapped, 0);
        box.CopyTo(wrapped, NonceBytes);
        return wrapped;
    }

    /// <summary>
    /// Opens a handed-over key and checks that it is for this project and this
    /// version. A key for something else is refused although it opens.
    /// </summary>
    public static byte[] Unwrap(byte[] wrapped, byte[] recipientSecretKey, byte[] senderPublicKey,
        Guid projectId, int keyVersion)
    {
        if (wrapped.Length != WrappedBytes)
            throw new OloopCryptoException($"a handed-over key is {WrappedBytes} bytes");

        byte[] message;
        try
        {
            message = PublicKeyBox.Open(wrapped[NonceBytes..], wrapped[..NonceBytes],
                recipientSecretKey, senderPublicKey);
        }
        catch (CryptographicException ex)
        {
            throw new OloopCryptoException(
                "the key was not made by the sender it is said to come from, or it was changed", ex);
        }

        if (!GuidBytes.FromBytes(message.AsSpan(KeyBytes, GuidBytes.Length)).Equals(projectId))
            throw new OloopCryptoException("the key is for another project than the one asked for");
        if (BinaryPrimitives.ReadUInt32BigEndian(message.AsSpan(KeyBytes + GuidBytes.Length)) != (uint)keyVersion)
            throw new OloopCryptoException("the key is of another version than the one asked for");

        return message[..KeyBytes];
    }
}
