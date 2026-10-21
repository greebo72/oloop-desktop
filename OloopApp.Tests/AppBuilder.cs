using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using OloopApp.Tests;

[assembly: AvaloniaTestApplication(typeof(OloopApp.Tests.TestAppBuilder))]

namespace OloopApp.Tests;

/// <summary>
/// Starts the real <see cref="App"/> headless, so the UI tests exercise the
/// same theme, styles and view models as the desktop app.
///
/// The real renderer (Skia) is used, not the headless drawing stub — otherwise
/// a window renders nothing and cannot be looked at.
///
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseSkia()
            // The desktop app registers the Inter font collection, so the
            // tests have to do the same. Otherwise they would measure the
            // fallback font and miss everything that depends on the metrics
            // of Inter.
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false,
            });
}
