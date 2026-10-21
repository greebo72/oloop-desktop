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
/// each other.
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

    private static (Window Dialog, TextBlock GroupBy, TextBlock Stale, TextBlock CtrlF,
        TextBlock CtrlN, TextBlock CtrlE, TextBlock CtrlComma, TextBlock Escape, TextBlock F5)
        ShowDialog()
    {
        var settings = new OloopSettings { Actor = "alice", DataRoot = "/tmp" };
        var dialog = new SettingsDialog(new SettingsViewModel(settings));
        dialog.Show();
        dialog.UpdateLayout();

        TextBlock Find(string text) => Descendants<TextBlock>(dialog)
            .First(t => t.Text == text);

        return (dialog,
            Find("Group by"), Find("Stale since (days)"),
            Find("Ctrl+F"), Find("Ctrl+N"), Find("Ctrl+E"),
            Find("Ctrl+,"), Find("Escape"), Find("F5"));
    }

    [Fact]
    public async Task The_Two_Rows_Of_The_List_Settings_Do_Not_Overlap()
    {
        await OnUiThread(() =>
        {
            var (dialog, groupBy, stale, _, _, _, _, _, _) = ShowDialog();

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
            var (dialog, _, _, ctrlF, ctrlN, ctrlE, ctrlComma, escape, f5) = ShowDialog();

            var rows = new[] { ctrlF, ctrlN, ctrlE, ctrlComma, escape, f5 };
            var names = new[] { "Ctrl+F", "Ctrl+N", "Ctrl+E", "Ctrl+,", "Escape", "F5" };

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
            var (dialog, _, _, _, _, _, _, _, _) = ShowDialog();

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
                    $"A grid has a child in row {maxRow} but only " +
                    $"{grid.RowDefinitions.Count} row definitions.");
            }

            dialog.Close();
        });
    }
}
