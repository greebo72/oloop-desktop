namespace OloopApp.Core.Server;

/// <summary>
/// The highest revision of each document that this device has seen (SERVER.md, "Limits of the
/// protection"). A server that later shows a lower one is answering with an older state; the
/// ledger is what remembers across restarts, so that a server cannot roll a document back by
/// waiting until the app was closed.
/// </summary>
public interface IRevisionLedger
{
    /// <summary>What was kept. Empty for a ledger that does not exist yet.</summary>
    IReadOnlyDictionary<Guid, int> Load();

    /// <summary>Keeps this state, replacing the one before.</summary>
    /// <exception cref="IOException">It could not be written.</exception>
    void Save(IReadOnlyDictionary<Guid, int> revisions);

    /// <summary>Forgets everything. For a server that was restored from a backup on purpose.</summary>
    void Clear();
}

/// <summary>A ledger that keeps nothing beyond the life of the object. For tests and for stores made without a place to keep it.</summary>
public sealed class MemoryRevisionLedger : IRevisionLedger
{
    private Dictionary<Guid, int> _kept = [];

    public IReadOnlyDictionary<Guid, int> Load() => new Dictionary<Guid, int>(_kept);

    public void Save(IReadOnlyDictionary<Guid, int> revisions) => _kept = new Dictionary<Guid, int>(revisions);

    public void Clear() => _kept = [];
}
