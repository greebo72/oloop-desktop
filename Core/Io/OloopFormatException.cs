namespace OloopApp.Core.Io;

/// <summary>
/// A file violates the oloop format. The message names the field or rule that
/// was broken, so the UI can show it as the reason for an unreadable file.
/// </summary>
public class OloopFormatException : Exception
{
    public OloopFormatException(string message) : base(message) { }
    public OloopFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// The file on disk changed since it was read. The write is refused; the user
/// decides between reloading and overwriting (SPEC.md, "Concurrent writes").
/// </summary>
public sealed class OloopConflictException : OloopFormatException
{
    public OloopConflictException(string message, string currentUpdatedAt)
        : base(message)
    {
        CurrentUpdatedAt = currentUpdatedAt;
    }

    /// <summary>The <c>updatedAt</c> currently on disk.</summary>
    public string CurrentUpdatedAt { get; }
}
