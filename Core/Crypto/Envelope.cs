using System.Security.Cryptography;
using Sodium;

namespace OloopApp.Core.Crypto;

/// <summary>
/// The envelope of SERVER.md, "Envelope": version, algorithm, a 24 byte nonce,
/// then the ciphertext with its 16 byte tag. Algorithm 1 is XChaCha20-Poly1305
/// (IETF), whose nonce is long enough to be chosen at random every time.
/// </summary>
public static class Envelope
{
    public const byte Version = 1;
    public const byte Algorithm = 1;
    public const int HeaderBytes = 2;
    public const int NonceBytes = 24;
    public const int KeyBytes = 32;
    public const int TagBytes = 16;

    /// <summary>The fixed parts: what an envelope of nothing weighs.</summary>
    public const int MinBytes = HeaderBytes + NonceBytes + TagBytes;

    /// <summary>Encrypts with a fresh random nonce.</summary>
    public static byte[] Seal(byte[] key, byte[] plaintext, byte[] associatedData)
        => Seal(key, plaintext, associatedData, SodiumCore.GetRandomBytes(NonceBytes));

    /// <summary>For the tests, which need the same bytes as the test vectors. A nonce is never chosen by hand.</summary>
    internal static byte[] Seal(byte[] key, byte[] plaintext, byte[] associatedData, byte[] nonce)
    {
        RequireLength(key, KeyBytes, nameof(key));
        RequireLength(nonce, NonceBytes, nameof(nonce));

        var sealedData = SecretAeadXChaCha20Poly1305.Encrypt(plaintext, nonce, key, associatedData);
        var envelope = new byte[HeaderBytes + NonceBytes + sealedData.Length];
        envelope[0] = Version;
        envelope[1] = Algorithm;
        nonce.CopyTo(envelope, HeaderBytes);
        sealedData.CopyTo(envelope, HeaderBytes + NonceBytes);
        return envelope;
    }

    /// <summary>
    /// Decrypts and checks. Throws <see cref="UnknownEnvelopeException"/> for a
    /// version or algorithm that is not known, <see cref="OloopCryptoException"/>
    /// for everything else that is wrong: the key, the associated data, a changed byte.
    /// </summary>
    public static byte[] Open(byte[] key, byte[] envelope, byte[] associatedData)
    {
        RequireLength(key, KeyBytes, nameof(key));

        if (envelope.Length < MinBytes)
            throw new OloopCryptoException("the data is too short to be an envelope");
        if (envelope[0] != Version || envelope[1] != Algorithm)
        {
            throw new UnknownEnvelopeException(
                $"an envelope of version {envelope[0]} with algorithm {envelope[1]} is not known to this client");
        }

        try
        {
            return SecretAeadXChaCha20Poly1305.Decrypt(
                envelope[(HeaderBytes + NonceBytes)..], envelope[HeaderBytes..(HeaderBytes + NonceBytes)],
                key, associatedData);
        }
        catch (CryptographicException ex)
        {
            throw new OloopCryptoException(
                "the data cannot be decrypted: the key is wrong or the data was changed", ex);
        }
    }

    private static void RequireLength(byte[] value, int length, string name)
    {
        if (value.Length != length)
            throw new ArgumentException($"{name} must be {length} bytes", name);
    }
}
