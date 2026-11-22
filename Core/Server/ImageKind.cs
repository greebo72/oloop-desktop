namespace OloopApp.Core.Server;

/// <summary>
/// Which pictures the app accepts from a server (SERVER.md, "Images"): PNG, JPEG, GIF and WebP,
/// recognized by their first bytes. The blob has no type of its own, and nothing that is not one
/// of these is handed on to the image decoder.
/// </summary>
internal static class ImageKind
{
    public static bool IsKnown(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (bytes.StartsWith(png))
            return true;

        // JPEG: start of image, then another marker.
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return true;

        // GIF87a and GIF89a.
        if (bytes.Length >= 6 && bytes[..3].SequenceEqual("GIF"u8) && bytes[3] == (byte)'8'
            && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a')
        {
            return true;
        }

        // WebP: "RIFF", a size, "WEBP".
        return bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8);
    }
}
