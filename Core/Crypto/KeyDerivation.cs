namespace OloopApp.Core.Crypto;

/// <summary>
/// The key that the passphrase makes: Argon2id (SERVER.md, "Key hierarchy").
/// The parameters come from the key bundle, so the server could name weak ones;
/// this client refuses them.
/// </summary>
public static class KeyDerivation
{
    /// <summary>The least cost a client accepts: 19 MiB and 2 passes.</summary>
    public const long MinMemoryBytes = 19L * 1024 * 1024;

    public const long MinPasses = 2;

    /// <summary>The cost of a new bundle.</summary>
    public const long DefaultMemoryBytes = 64L * 1024 * 1024;

    public const long DefaultPasses = 3;

    public const int SaltBytes = 16;
    public const int KeyBytes = 32;

    /// <summary>Throws when the cost is below the minimum, or more than this client can use.</summary>
    public static void CheckPolicy(long memoryBytes, long passes)
    {
        if (memoryBytes < MinMemoryBytes || passes < MinPasses)
        {
            throw new OloopCryptoException(
                "the key derivation of the bundle is weaker than this client accepts "
                + "(at least 19 MiB and 2 passes)");
        }

        // What the memory of a 64 bit process can hold, and what libsodium takes (SERVER.md allows up to 2^32 - 1).
        if (memoryBytes > uint.MaxValue)
            throw new OloopCryptoException("the key derivation needs more memory than this client can use");
    }

    /// <summary>
    /// The key of a passphrase. The passphrase is given as bytes: how text becomes
    /// bytes (and whether it is normalized first) is decided where the text is typed.
    /// </summary>
    public static byte[] DerivePassphraseKey(byte[] passphrase, byte[] salt, long memoryBytes, long passes)
    {
        CheckPolicy(memoryBytes, passes);
        if (salt.Length != SaltBytes)
            throw new OloopCryptoException($"the salt of the bundle must be {SaltBytes} bytes");

        return Argon2.DeriveKey(passphrase, salt, passes, memoryBytes, KeyBytes);
    }
}
