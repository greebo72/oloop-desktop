using OloopApp.Core.Io;
using OloopApp.Core.Models;

namespace OloopApp.Core.Server;

/// <summary>
/// The store of a server session that is not open (not signed in, keys locked, server not
/// reached). It exists so that the rest of the app has a store to ask: it says why it cannot be
/// used through <see cref="CheckAvailable"/>, and every other call fails with the same words
/// instead of doing something with no data behind it.
/// </summary>
public sealed class UnavailableServerStore : OloopStoreBase
{
    private readonly string _reason;

    public UnavailableServerStore(string reason) => _reason = reason;

    public override string? CheckAvailable() => _reason;

    public override string Fingerprint() => "";

    public override IChangeSource CreateChangeSource(TimeSpan debounce) => throw new IOException(_reason);

    public override Task<OloopSnapshot> LoadAsync(CancellationToken ct = default) => throw new IOException(_reason);

    public override ImageOpenResult OpenImage(OloopProject project, string? imagePath) => new(null, _reason);

    public override Task<ImageOpenResult> OpenImageAsync(OloopProject project, OloopTask task, CancellationToken ct = default)
        => Task.FromResult(new ImageOpenResult(null, _reason));

    public override Task<OloopProject> CreateProjectAsync(
        string name, string? referencePath = null, string? referenceHost = null, CancellationToken ct = default)
        => throw new IOException(_reason);

    public override Task WriteTaskAsync(
        OloopProject project, OloopTask task, string? expectedUpdatedAt, CancellationToken ct = default)
        => throw new IOException(_reason);
}
