using System.Text.Json;
using Sodium;

namespace OloopApp.Core.Crypto;

/// <summary>
/// The key bundle of a user (SERVER.md, "Key bundle"): the key pair, with the
/// secret key encrypted twice, once with the key of the passphrase and once with
/// the recovery code. The server stores it and cannot open it.
/// </summary>
public sealed record KeyBundle(
    byte[] Salt,
    long MemoryBytes,
    long Passes,
    byte[] PublicKey,
    byte[] WrappedSecretKey,
    byte[] RecoveryWrappedSecretKey)
{
    public const int PublicKeyBytes = 32;
    public const int SecretKeyBytes = 32;

    /// <summary>
    /// A new bundle: a fresh key pair, its secret key encrypted with the key of the passphrase and
    /// with the recovery code. The cost is the default of the spec. The secret key comes back too,
    /// so that the device that made the bundle is unlocked without a second Argon2id run.
    /// </summary>
    public static (KeyBundle Bundle, byte[] SecretKey) Create(
        byte[] passphrase, byte[] recoveryCode, Guid userId,
        long memoryBytes = KeyDerivation.DefaultMemoryBytes, long passes = KeyDerivation.DefaultPasses)
    {
        if (recoveryCode.Length != RecoveryCode.Bytes)
            throw new ArgumentException($"a recovery code is {RecoveryCode.Bytes} bytes", nameof(recoveryCode));

        var salt = SodiumCore.GetRandomBytes(KeyDerivation.SaltBytes);
        var passphraseKey = KeyDerivation.DerivePassphraseKey(passphrase, salt, memoryBytes, passes);

        var pair = PublicKeyBox.GenerateKeyPair();
        var associatedData = AssociatedData.Key(userId);
        var bundle = new KeyBundle(
            salt, memoryBytes, passes, pair.PublicKey,
            Envelope.Seal(passphraseKey, pair.PrivateKey, associatedData),
            Envelope.Seal(recoveryCode, pair.PrivateKey, associatedData));
        return (bundle, pair.PrivateKey);
    }

    /// <summary>Reads the bundle as the API carries it. A bundle of another version or kind is refused.</summary>
    public static KeyBundle FromJson(string json)
    {
        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (JsonException ex)
        {
            throw new OloopCryptoException("the key bundle is not JSON", ex);
        }

        if (Int(root, "v") != 1)
            throw new UnknownEnvelopeException($"a key bundle of version {Int(root, "v")} is not known to this client");
        if (Text(root, "kdf") != "argon2id")
            throw new OloopCryptoException("the key bundle names a key derivation this client does not know");

        var bundle = new KeyBundle(
            Binary(root, "salt", KeyDerivation.SaltBytes),
            Int(root, "m"),
            Int(root, "t"),
            Binary(root, "publicKey", PublicKeyBytes),
            Binary(root, "wrappedSecretKey", Envelope.MinBytes + SecretKeyBytes),
            Binary(root, "recoveryWrappedSecretKey", Envelope.MinBytes + SecretKeyBytes));

        KeyDerivation.CheckPolicy(bundle.MemoryBytes, bundle.Passes);
        return bundle;
    }

    /// <summary>The bundle as the API carries it.</summary>
    public string ToJson() => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["v"] = 1,
        ["kdf"] = "argon2id",
        ["salt"] = Base64Url.Encode(Salt),
        ["m"] = MemoryBytes,
        ["t"] = Passes,
        ["publicKey"] = Base64Url.Encode(PublicKey),
        ["wrappedSecretKey"] = Base64Url.Encode(WrappedSecretKey),
        ["recoveryWrappedSecretKey"] = Base64Url.Encode(RecoveryWrappedSecretKey),
    });

    /// <summary>The secret key, opened with the key that the passphrase makes.</summary>
    public byte[] UnlockWithPassphraseKey(byte[] passphraseKey, Guid userId)
        => Unlock(WrappedSecretKey, passphraseKey, userId);

    /// <summary>The secret key, opened with the recovery code (32 bytes, used directly as the key).</summary>
    public byte[] UnlockWithRecoveryCode(byte[] recoveryCode, Guid userId)
        => Unlock(RecoveryWrappedSecretKey, recoveryCode, userId);

    private byte[] Unlock(byte[] wrapped, byte[] key, Guid userId)
    {
        var secretKey = Envelope.Open(key, wrapped, AssociatedData.Key(userId));
        if (secretKey.Length != SecretKeyBytes)
            throw new OloopCryptoException("the secret key in the bundle has the wrong size");

        // The public key of the secret key has to be the one of the bundle: otherwise
        // the bundle is not what its owner made.
        if (!PublicKeyBox.GenerateKeyPair(secretKey).PublicKey.AsSpan().SequenceEqual(PublicKey))
            throw new OloopCryptoException("the secret key does not belong to the public key of the bundle");

        return secretKey;
    }

    private static string Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new OloopCryptoException($"the key bundle has no {name}");

    private static long Int(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number)
            ? number
            : throw new OloopCryptoException($"the key bundle has no number {name}");

    private static byte[] Binary(JsonElement root, string name, int length)
    {
        var data = Base64Url.Decode(Text(root, name));
        return data.Length == length
            ? data
            : throw new OloopCryptoException($"{name} of the key bundle must be {length} bytes");
    }
}
