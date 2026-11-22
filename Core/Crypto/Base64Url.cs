using NetBase64Url = System.Buffers.Text.Base64Url;

namespace OloopApp.Core.Crypto;

/// <summary>
/// base64url as the API uses it: no padding, and one spelling for one value, so
/// what the server stores is what the client sent.
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> data) => NetBase64Url.EncodeToString(data);

    /// <summary>
    /// Decodes text, or throws when it is not base64url without padding in its
    /// shortest form (a changed last character that decodes to the same bytes is
    /// refused, too).
    /// </summary>
    public static byte[] Decode(string text)
    {
        foreach (var c in text)
        {
            var ok = c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_';
            if (!ok)
                throw new OloopCryptoException("not base64url without padding");
        }

        if (text.Length % 4 == 1)
            throw new OloopCryptoException("not base64url without padding");

        byte[] data;
        try
        {
            data = NetBase64Url.DecodeFromChars(text);
        }
        catch (FormatException ex)
        {
            throw new OloopCryptoException("not base64url without padding", ex);
        }

        if (Encode(data) != text)
            throw new OloopCryptoException("not base64url in its shortest form");

        return data;
    }
}
