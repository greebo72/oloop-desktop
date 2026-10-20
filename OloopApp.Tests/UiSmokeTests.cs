using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using OloopApp.Core.Models;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The shell must come up, the theme must resolve and the dashboard must show
/// the projects of the data root.
/// </summary>
public class UiSmokeTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(UiSmokeTests).Assembly);

    private static void OnUiThread(Action action) =>
        Session.Dispatch(action, CancellationToken.None);

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T typed)
                yield return typed;

            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    [Fact]
    public void MainWindow_Opens()
    {
        OnUiThread(() =>
        {
            var window = new MainWindow { DataContext = new MainViewModel() };
            window.Show();

            Assert.NotNull(window.Content);
            window.Close();
        });
    }

    [Fact]
    public void Theme_Resolves_Every_Token_The_Views_Use()
    {
        OnUiThread(() =>
        {
            var window = new MainWindow { DataContext = new MainViewModel() };
            window.Show();

            var resources = Application.Current!.Resources;

            foreach (var key in new[]
            {
                "SurfaceBrush", "SurfaceContainerBrush", "SurfaceContainerHighBrush",
                "SurfaceContainerHighestBrush", "PrimaryBrush", "OnPrimaryBrush",
                "PrimaryContainerBrush", "OnPrimaryContainerBrush", "SecondaryContainerBrush",
                "OnSecondaryContainerBrush", "TertiaryContainerBrush", "OnTertiaryContainerBrush",
                "OnSurfaceBrush", "OnSurfaceVariantBrush", "OutlineBrush", "OutlineVariantBrush",
                "StatusOpenBrush", "CodeBlockBgBrush",
                "TypeTodoBrush", "TypeTodoBgBrush", "TypeQuestionBrush", "TypeQuestionBgBrush",
                "TypeDecisionBrush", "TypeDecisionBgBrush", "TypeFeatureBrush", "TypeFeatureBgBrush",
                "TypeBugBrush", "TypeBugBgBrush", "TypeClosedBrush", "TypeClosedBgBrush",
            })
            {
                Assert.True(
                    resources.TryGetResource(key, null, out var value) && value is IBrush,
                    $"theme resource '{key}' is missing");
            }

            foreach (var key in new[]
            {
                "IconChecklist", "IconHelp", "IconGavel", "IconBuild", "IconBugReport",
                "IconFolder", "IconSearch", "IconRefresh", "IconSettings", "IconAdd",
                "IconBack", "IconClose", "IconCopy", "IconSend", "IconMoreVert", "IconEdit",
                "IconTaskAlt", "IconFilter", "IconVisibility", "IconWarning",
            })
            {
                Assert.True(
                    resources.TryGetResource(key, null, out var value) && value is Geometry,
                    $"icon geometry '{key}' is missing or not a Geometry");
            }

            window.Close();
        });
    }

    [Fact]
    public void Dashboard_Shows_A_Card_Per_Project()
    {
        OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects =
                [
                    new OloopProject
                    {
                        FormatVersion = 1,
                        Id = "11111111-1111-4111-8111-111111111111",
                        Name = "Example Project",
                        CreatedAt = "2026-01-01T09:00:00Z",
                        FolderName = "example-project",
                    },
                ],
                Tasks = [],
                Unreadable = [],
            });

            Assert.Single(shell.Dashboard.Projects);
            Assert.Equal("Example Project", shell.Dashboard.Projects[0].Name);
            Assert.Equal("0 open of 0", shell.Dashboard.Projects[0].OpenOfTotal);

            window.Close();
        });
    }

    [Fact]
    public void Dashboard_Renders_Project_Rows()
    {
        OnUiThread(() =>
        {
            var shell = new MainViewModel();
            var window = new MainWindow { DataContext = shell };
            window.Show();

            shell.Dashboard.Update(new OloopSnapshot
            {
                Projects =
                [
                    new OloopProject
                    {
                        FormatVersion = 1,
                        Id = "11111111-1111-4111-8111-111111111111",
                        Name = "Example Project",
                        CreatedAt = "2026-01-01T09:00:00Z",
                        FolderName = "example-project",
                        TaskCount = 3,
                        OpenTaskCount = 2,
                    },
                ],
                Tasks = [],
                Unreadable = [],
            });

            // The item template of the project list must be realized, otherwise
            // the bindings of the row were never evaluated.
            window.UpdateLayout();

            var texts = Descendants<TextBlock>(window).Select(t => t.Text).ToList();
            Assert.Contains("Example Project", texts);
            Assert.Contains("2 open of 3", texts);

            window.Close();
        });
    }
}
