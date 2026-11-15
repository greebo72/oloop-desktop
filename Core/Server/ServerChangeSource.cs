using OloopApp.Core.Io;

namespace OloopApp.Core.Server;

/// <summary>
/// Notices changes on the server by asking <c>GET /v1/head</c> every few seconds
/// and comparing the cursor with the one the store knows (SERVER.md, "Noticing
/// changes"). It takes the place of the file watcher.
///
/// The server being unreachable is reported once through <see cref="Failed"/>;
/// when it answers again, <see cref="Changed"/> is raised because changes may have
/// been missed meanwhile.
/// </summary>
internal sealed class ServerChangeSource : IChangeSource
{
    /// <summary>SERVER.md: every 5 to 10 seconds while the app is in front.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(7);

    private readonly ServerOloopStore _store;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public ServerChangeSource(ServerOloopStore store, TimeSpan interval)
    {
        _store = store;
        _interval = interval;
        _loop = Task.Run(PollAsync);
    }

    public event Action? Changed;
    public event Action<Exception>? Failed;

    private async Task PollAsync()
    {
        var failing = false;
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, _stop.Token).ConfigureAwait(false);
                var head = await _store.ReadHeadAsync(_stop.Token).ConfigureAwait(false);

                var changed = _store.NoteHead(head);
                if (failing || changed)
                {
                    failing = false;
                    Changed?.Invoke();
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (IOException ex)
            {
                if (!failing)
                {
                    failing = true;
                    Failed?.Invoke(ex);
                }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(1));
        }
        catch (AggregateException)
        {
            // Already reported through Failed.
        }

        _stop.Dispose();
    }
}
