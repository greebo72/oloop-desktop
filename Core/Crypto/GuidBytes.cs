namespace OloopApp.Core.Crypto;

/// <summary>
/// A UUID as 16 bytes in the order of RFC 4122, as the other implementations
/// write it. <see cref="Guid.ToByteArray()"/> has another order for the first
/// three parts, which would give another message and another key.
/// </summary>
public static class GuidBytes
{
    public const int Length = 16;

    public static byte[] ToBytes(Guid id) => id.ToByteArray(bigEndian: true);

    public static Guid FromBytes(ReadOnlySpan<byte> bytes) => new(bytes, bigEndian: true);
}
