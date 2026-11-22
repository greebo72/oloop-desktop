using System.Runtime.InteropServices;
using Sodium;

namespace OloopApp.Core.Crypto;

/// <summary>
/// <c>crypto_pwhash</c> of libsodium, called directly. The binding refuses fewer
/// than 3 passes for Argon2id (it applies the limit of Argon2i), but the spec
/// allows 2, which is also what the "interactive" setting of libsodium uses. A
/// bundle that another client made with 2 passes must still open. The native
/// library is the one that the binding brings; it is found the same way.
/// </summary>
internal static partial class Argon2
{
    private const int AlgorithmArgon2id13 = 2; // crypto_pwhash_ALG_ARGON2ID13

    [DllImport("libsodium", EntryPoint = "crypto_pwhash", CallingConvention = CallingConvention.Cdecl)]
    private static extern int crypto_pwhash(
        byte[] output, ulong outputLength, byte[] password, ulong passwordLength, byte[] salt,
        ulong opsLimit, nuint memLimit, int algorithm);

    public static byte[] DeriveKey(byte[] password, byte[] salt, long passes, long memoryBytes, int keyBytes)
    {
        SodiumCore.Init(); // makes sure libsodium is loaded and initialized

        var key = new byte[keyBytes];
        var result = crypto_pwhash(key, (ulong)keyBytes, password, (ulong)password.Length, salt,
            (ulong)passes, (nuint)memoryBytes, AlgorithmArgon2id13);

        if (result != 0)
            throw new OloopCryptoException("the key could not be made from the passphrase (not enough memory?)");

        return key;
    }
}
