using Avalonia.Headless;
using Avalonia.Threading;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using Xunit;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Tests;

/// <summary>
/// An image that has to be fetched is shown as loading and the window is not held up. The store
/// here answers when the test says so; the fetch itself is tested with the server store.
/// </summary>
public class ImageLoadingTests : IDisposable
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(ImageLoadingTests).Assembly);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "oloop-imgload-" + Guid.NewGuid().ToString("N"));
    private readonly TaskCompletionSource<ImageOpenResult> _answer = new();

    public ImageLoadingTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static OloopProject Project() => new()
    {
        FormatVersion = 1, Id = "11111111-1111-4111-8111-111111111111", Name = "P", CreatedAt = "2026-01-01T09:00:00Z",
    };

    private static OloopTask TaskWithImage() => new()
    {
        FormatVersion = 1, Id = "22222222-2222-4222-8222-222222222222", ProjectId = Project().Id,
        Type = TaskType.todo, Title = "T", Status = TaskStatus.open, ImagePath = "blob:33333333-3333-4333-8333-333333333333",
        CreatedAt = "2026-01-01T09:00:00Z", CreatedBy = "a", UpdatedAt = "2026-01-01T09:00:00Z", UpdatedBy = "a",
    };

    private MainViewModel Shell() => new(new OloopSettings
    {
        FilePath = Path.Combine(_dir, "settings.json"), DataRoot = _dir, Actor = "a",
    })
    {
        StoreFactory = _ => new SlowImageStore(_answer.Task),
    };

    // The first look at the image starts the loading. These tests make it on a thread of the pool, so
    // that what follows the fetch runs there and the test can wait for it: the headless window does
    // not run the jobs that are posted to its thread. Decoding a picture needs the thread of the
    // window, so only the ways that end without one are tested here; a picture that arrives at once
    // is tested with the server store.

    private async Task<TaskDetailViewModel> StartLoadingAsync(TaskCompletionSource finished)
    {
        using var shell = Shell();
        TaskDetailViewModel? detail = null;
        await Session.Dispatch(() =>
        {
            detail = new TaskDetailViewModel(TaskWithImage(), Project(), shell);
            detail.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(TaskDetailViewModel.IsImageLoading))
                    finished.TrySetResult();
            };
        }, CancellationToken.None);

        await Task.Run(() =>
        {
            Assert.True(detail!.IsImageLoading);   // the first look starts it
            Assert.False(detail.HasImage);
            Assert.False(detail.HasImageError);
        });

        return detail!;
    }

    [Fact]
    public async Task A_Failed_Fetch_Ends_As_A_Message_And_Not_As_Loading_For_Ever()
    {
        var finished = new TaskCompletionSource();
        var detail = await StartLoadingAsync(finished);

        _answer.SetException(new IOException("the server did not answer"));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(detail.IsImageLoading);
        Assert.True(detail.HasImageError);
        Assert.Contains("did not answer", detail.ImageError);
        Assert.False(detail.HasImage);
    }

    [Fact]
    public async Task A_Failure_Nobody_Expected_Ends_As_A_Message_Too()
    {
        var finished = new TaskCompletionSource();
        var detail = await StartLoadingAsync(finished);

        _answer.SetException(new InvalidOperationException("something that was not foreseen"));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(detail.IsImageLoading);
        Assert.Contains("not foreseen", detail.ImageError);
    }

    [Fact]
    public async Task A_Reason_That_The_Store_Gives_Is_Shown_As_It_Is()
    {
        var finished = new TaskCompletionSource();
        var detail = await StartLoadingAsync(finished);

        _answer.SetResult(new ImageOpenResult(null, "The image of the task is missing."));
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(detail.IsImageLoading);
        Assert.Equal("The image of the task is missing.", detail.ImageError);
    }

    /// <summary>A store that has nothing but an image that arrives when the test lets it.</summary>
    private sealed class SlowImageStore(Task<ImageOpenResult> answer) : OloopStoreBase
    {
        public override string? CheckAvailable() => null;
        public override string Fingerprint() => "";
        public override IChangeSource CreateChangeSource(TimeSpan debounce) => throw new NotSupportedException();
        public override Task<OloopSnapshot> LoadAsync(CancellationToken ct = default) => Task.FromResult(OloopSnapshot.Empty);
        public override ImageOpenResult OpenImage(OloopProject project, string? imagePath) => throw new NotSupportedException();

        public override Task<ImageOpenResult> OpenImageAsync(OloopProject project, OloopTask task, CancellationToken ct = default)
            => answer;

        public override Task<OloopProject> CreateProjectAsync(
            string name, string? referencePath = null, string? referenceHost = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public override Task WriteTaskAsync(
            OloopProject project, OloopTask task, string? expectedUpdatedAt, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
