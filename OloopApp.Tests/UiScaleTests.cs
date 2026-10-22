using Avalonia.Controls;
using Avalonia.Headless;
using OloopApp.Core.Io;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The text size scales the whole window, open windows follow a change, and
/// the window sizes scale with it.
/// </summary>
public class UiScaleTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiScaleTests).Assembly);

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    [Fact]
    public async Task A_Dialog_Opens_With_The_Scaled_Size_And_Follows_A_Change()
    {
        await OnUiThread(() =>
        {
            try
            {
                UiScale.Factor = 1.5;
                var dialog = new SettingsDialog(new SettingsViewModel(new OloopSettings()));
                dialog.Show();

                Assert.Equal(520 * 1.5, dialog.Width);
                Assert.Equal(460 * 1.5, dialog.MinWidth);

                var transform = Assert.IsType<LayoutTransformControl>(dialog.Content);
                Assert.Equal(1.5, ((Avalonia.Media.ScaleTransform)transform.LayoutTransform!).ScaleX);

                UiScale.Factor = 1.0;

                Assert.Equal(520, dialog.Width, 3);
                Assert.Equal(460, dialog.MinWidth, 3);
                dialog.Close();
            }
            finally
            {
                UiScale.Factor = 1.0;
            }
        });
    }

    [Fact]
    public async Task A_Closed_Window_No_Longer_Follows_The_Factor()
    {
        await OnUiThread(() =>
        {
            try
            {
                var dialog = new SettingsDialog(new SettingsViewModel(new OloopSettings()));
                dialog.Show();
                dialog.Close();

                UiScale.Factor = 2.0;

                Assert.Equal(520, dialog.Width, 3);
            }
            finally
            {
                UiScale.Factor = 1.0;
            }
        });
    }
}
