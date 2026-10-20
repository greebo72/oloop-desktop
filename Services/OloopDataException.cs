namespace OloopApp.Services;

/// <summary>Raised when a data file violates the oloop format or spec rules.</summary>
public sealed class OloopDataException : Exception
{
    public OloopDataException(string message) : base(message) { }
    public OloopDataException(string message, Exception inner) : base(message, inner) { }
}
