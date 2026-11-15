using System.Text;
using Sodium;

namespace OloopApp.Core.Crypto;

/// <summary>
/// The recovery code (SERVER.md, "Key hierarchy"): 32 random bytes that are the key of the
/// second copy of the secret key. Shown once, as base32 (RFC 4648, no padding) in groups of
/// four letters, so that it can be written down and read back: 13 groups.
/// </summary>
public static class RecoveryCode
{
    public const int Bytes = 32;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private const int GroupSize = 4;

    public static byte[] Create() => SodiumCore.GetRandomBytes(Bytes);

    /// <summary>The code as the user sees it: <c>ABCD-EFGH-…</c>.</summary>
    public static string Format(byte[] code)
    {
        if (code.Length != Bytes)
            throw new ArgumentException($"a recovery code is {Bytes} bytes", nameof(code));

        var letters = new StringBuilder();
        var buffer = 0;
        var bits = 0;
        foreach (var b in code)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                letters.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
            buffer &= (1 << bits) - 1;
        }

        if (bits > 0)
            letters.Append(Alphabet[(buffer << (5 - bits)) & 31]);

        var grouped = new StringBuilder();
        for (var i = 0; i < letters.Length; i += GroupSize)
        {
            if (i > 0)
                grouped.Append('-');
            grouped.Append(letters.ToString(i, Math.Min(GroupSize, letters.Length - i)));
        }

        return grouped.ToString();
    }

    /// <summary>
    /// The bytes of what the user typed. Spaces, dashes and case do not matter. Returns null for
    /// text that is not a recovery code, so that a dialog can say "check the code" without a
    /// distinction that would help a guesser.
    /// </summary>
    public static byte[]? TryParse(string typed)
    {
        var letters = new StringBuilder();
        foreach (var c in typed)
        {
            if (c is ' ' or '-' or '\t' or '\r' or '\n')
                continue;
            letters.Append(char.ToUpperInvariant(c));
        }

        // 32 bytes are 256 bits: 52 letters, the last one with 4 unused bits.
        if (letters.Length != (Bytes * 8 + 4) / 5)
            return null;

        var result = new byte[Bytes];
        var buffer = 0;
        var bits = 0;
        var index = 0;
        foreach (var c in letters.ToString())
        {
            var value = Alphabet.IndexOf(c);
            if (value < 0)
                return null;

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                result[index++] = (byte)(buffer >> (bits - 8));
                bits -= 8;
                buffer &= (1 << bits) - 1;
            }
        }

        // The unused bits must be zero, or two spellings would stand for one code.
        return buffer == 0 ? result : null;
    }
}
