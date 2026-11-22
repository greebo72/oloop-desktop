using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Io;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The settings dialog is a list of rows. A grid without row definitions
/// puts every row into the first one, so the labels of the fields overlap
/// each other. The rows are on three tabs and only the selected tab is part of
/// the window, so a test selects the tab it looks at.
/// </summary>
public class SettingsLayoutTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(SettingsLayoutTests).Assembly);

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

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

    /// <summary>The top edge of a text, relative to the dialog.</summary>
    private static double TopOf(Visual root, Visual element) =>
        element.TranslatePoint(new Point(0, 0), root)?.Y ?? -1;

    private static Window ShowDialog(string? tab = null)
    {
        var settings = new OloopSettings { Actor = "alice", DataRoot = "/tmp" };
        var dialog = new SettingsDialog(new SettingsViewModel(settings));
        dialog.Show();
        if (tab is not null)
            SelectTab(dialog, tab);
        dialog.UpdateLayout();
        return dialog;
    }

    private static void SelectTab(Window dialog, string tabName)
    {
        dialog.FindControl<TabControl>("Tabs")!.SelectedItem = dialog.FindControl<TabItem>(tabName);
        dialog.UpdateLayout();
    }

    private static TextBlock Find(Window dialog, string text)
        => Descendants<TextBlock>(dialog).First(t => t.Text == text);

    [Fact]
    public async Task The_Two_Rows_Of_The_List_Settings_Do_Not_Overlap()
    {
        await OnUiThread(() =>
        {
            var dialog = ShowDialog("TasksTab");
            var groupBy = Find(dialog, "Group by");
            var stale = Find(dialog, "Stale since (days)");

            Assert.True(stale.Bounds.Top > 0);
            Assert.True(
                TopOf(dialog, stale) > TopOf(dialog, groupBy),
                $"'Stale since (days)' at {TopOf(dialog, stale)} must be below " +
                $"'Group by' at {TopOf(dialog, groupBy)}");

            dialog.Close();
        });
    }

    [Fact]
    public async Task The_Shortcut_Rows_Do_Not_Overlap()
    {
        await OnUiThread(() =>
        {
            var dialog = ShowDialog("InterfaceTab");

            var names = new[] { "Ctrl+F", "Ctrl+N", "Ctrl+E", "Ctrl+,", "Escape", "F5" };
            var rows = names.Select(n => Find(dialog, n)).ToArray();

            for (var i = 1; i < rows.Length; i++)
            {
                var above = TopOf(dialog, rows[i - 1]);
                var below = TopOf(dialog, rows[i]);

                Assert.True(
                    below > above,
                    $"'{names[i]}' at {below} must be below '{names[i - 1]}' at {above}");
            }

            dialog.Close();
        });
    }

    [Fact]
    public async Task Every_Grid_With_A_Row_Index_Has_Row_Definitions()
    {
        await OnUiThread(() =>
        {
            var dialog = ShowDialog();

            foreach (var tab in new[] { "StorageTab", "TasksTab", "InterfaceTab" })
            {
                SelectTab(dialog, tab);

                // A grid that gets a child into row 1 or later without row
                // definitions stacks every child into the first row.
                foreach (var grid in Descendants<Grid>(dialog))
                {
                    var maxRow = grid.Children
                        .OfType<Control>()
                        .Select(c => Grid.GetRow(c))
                        .DefaultIfEmpty(0)
                        .Max();

                    // A grid whose children all sit in row 0 needs no row
                    // definitions at all.
                    if (maxRow == 0)
                        continue;

                    Assert.True(
                        grid.RowDefinitions.Count > maxRow,
                        $"A grid on {tab} has a child in row {maxRow} but only " +
                        $"{grid.RowDefinitions.Count} row definitions.");
                }
            }

            dialog.Close();
        });
    }
}
