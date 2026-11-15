using System.Text;

namespace OloopApp.Core.Crypto;

/// <summary>
/// How a typed passphrase becomes the bytes that go into Argon2id (SERVER.md, "Key
/// hierarchy"): the UTF-8 bytes of the text exactly as it was typed or pasted,
/// <b>without normalization</b>.
///
/// Do not add <c>Normalize()</c> here. It would not do what it promises: this app
/// runs with <c>InvariantGlobalization</c> (no ICU is needed, so the Linux package
/// runs on every distribution), and then <c>string.Normalize()</c> leaves
/// non-ASCII text unchanged without a word. A client that normalizes only where it
/// can would give the same passphrase different keys on different systems. The
/// spec asks for none and the test vectors (<c>kdf.encoding</c>) make every
/// implementation agree: <c>ü</c> as one character and <c>u</c> with a combining
/// diaeresis are two passphrases.
/// </summary>
public static class Passphrase
{
    // Strict: text with a lone surrogate half is no Unicode. The default encoder would
    // replace it with U+FFFD and another client would refuse it, two different keys.
    private static readonly UTF8Encoding Strict = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The bytes for <see cref="KeyDerivation.DerivePassphraseKey"/>.</summary>
    public static byte[] ToKeyBytes(string typed)
    {
        try
        {
            return Strict.GetBytes(typed);
        }
        catch (EncoderFallbackException ex)
        {
            throw new OloopCryptoException(
                "the passphrase holds a character that is not valid Unicode (a lone half of a surrogate pair)", ex);
        }
    }
}
