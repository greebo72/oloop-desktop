namespace OloopApp.Core.Io;

/// <summary>
/// Tells the app that the data of a store changed behind its back: another
/// client, the CLI or a sync wrote to it. The folder store watches the file
/// system, other stores can listen to their server.
///
/// The events are raised on a thread pool thread.
/// </summary>
public interface IChangeSource : IDisposable
{
    /// <summary>Raised once after a burst of changes has ended.</summary>
    event Action? Changed;

    /// <summary>
    /// Raised when the source reports a problem. Changes may have been
    /// missed then; the owner reports it.
    /// </summary>
    event Action<Exception>? Failed;
}
