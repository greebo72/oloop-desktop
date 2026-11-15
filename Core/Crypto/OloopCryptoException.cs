namespace OloopApp.Core.Crypto;

/// <summary>
/// Encrypted data cannot be read or trusted: the key is wrong, a byte was
/// changed, or what was handed over is not what was asked for. Nothing of it is
/// used (SERVER.md, "Encryption").
/// </summary>
public class OloopCryptoException : Exception
{
    public OloopCryptoException(string message) : base(message) { }
    public OloopCryptoException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The envelope has a version or an algorithm this client does not know. The
/// client stops and says so; it never writes data that it could not read
/// (SERVER.md, "Envelope").
/// </summary>
public sealed class UnknownEnvelopeException : OloopCryptoException
{
    public UnknownEnvelopeException(string message) : base(message) { }
}
