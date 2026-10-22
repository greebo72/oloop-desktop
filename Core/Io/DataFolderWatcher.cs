namespace OloopApp.Core.Io;

/// <summary>
/// Watches the data folder and raises <see cref="Changed"/> once after a burst
/// of file events. CLI, other clients and the sync (for example Google Drive)
/// write into the same folder while the app runs.
///
/// One <see cref="FileSystemWatcher"/> on the folder, recursive, on every
/// system the same way: no polling and no special case per system. The events
/// are debounced: a write is several events (temp file, replace, backup), and
/// a sync can write many files in a row, so every event restarts a short
/// timer and only its end raises <see cref="Changed"/>.
///
/// Events of the <c>.backup</c> folders and of temp files are ignored: they
/// carry no change that anyone sees.
///
/// <see cref="Changed"/> is raised on a thread pool thread.
/// </summary>
public sealed class DataFolderWatcher : IDisposable
{
    /// <summary>The default length of the debounce window.</summary>
    public static readonly TimeSpan DefaultDebounce = TimeSpan.FromMilliseconds(400);

    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _debounce;
    private bool _disposed;

    /// <summary>Starts watching the folder.</summary>
    /// <param name="folder">The data folder. It must exist.</param>
    /// <param name="debounce">The quiet time after the last event. Null takes <see cref="DefaultDebounce"/>.</param>
    public DataFolderWatcher(string folder, TimeSpan? debounce = null)
    {
        Folder = folder;
        _debounce = debounce ?? DefaultDebounce;
        _timer = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                           | NotifyFilters.LastWrite | NotifyFilters.Size,
            InternalBufferSize = 64 * 1024,
        };
        _watcher.Created += (_, e) => OnEvent(e.FullPath);
        _watcher.Changed += (_, e) => OnEvent(e.FullPath);
        _watcher.Deleted += (_, e) => OnEvent(e.FullPath);
        _watcher.Renamed += (_, e) =>
        {
            OnEvent(e.OldFullPath);
            OnEvent(e.FullPath);
        };
        _watcher.Error += (_, e) => Failed?.Invoke(e.GetException());
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>The folder that is watched.</summary>
    public string Folder { get; }

    /// <summary>Raised once after a burst of events has ended.</summary>
    public event Action? Changed;

    /// <summary>
    /// Raised when the system reports a problem with the watch, for example a
    /// full event buffer. Events may have been missed then. Nothing is done
    /// about it here: the owner reports it.
    /// </summary>
    public event Action<Exception>? Failed;

    /// <summary>Whether a path is of no interest: a backup copy or a temp file.</summary>
    public static bool IsIgnored(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            return true;

        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p => p.Equals(".backup", StringComparison.OrdinalIgnoreCase));
    }

    private void OnEvent(string path)
    {
        if (IsIgnored(path))
            return;

        lock (_gate)
        {
            if (_disposed)
                return;

            _timer.Change(_debounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void Raise()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
        }

        Changed?.Invoke();
    }

    /// <summary>Stops watching.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Dispose();
        _timer.Dispose();
    }
}
