using Avalonia;
using Avalonia.Headless;
using OloopApp.Tests;

[assembly: AvaloniaTestApplication(typeof(OloopApp.Tests.TestAppBuilder))]

namespace OloopApp.Tests;

/// <summary>
/// Starts the real <see cref="App"/> headless, so the UI tests exercise the
/// same theme, styles and view models as the desktop app.
///
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
