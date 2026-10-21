using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// Avalonia draws the text of a text block from the top of its box. A
/// control that offers more room than one line needs — a button with a
/// minimum height, a combo box with an asymmetric padding, a search field
/// that is higher than its line — would push the text to the top of the
/// control.
///
/// These tests pin down where the text sits in the controls of the app.
/// </summary>
public class VerticalTextAlignmentTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(VerticalTextAlignmentTests).Assembly);

    private static async Task OnUiThread(Action action) =>
        await Session.Dispatch(action, CancellationToken.None);

    private static bool IsInPopup(Visual visual)
    {
        for (var parent = visual.GetVisualParent(); parent is not null; parent = parent.GetVisualParent())
        {
            if (parent is Popup)
                return true;
        }

        return false;
    }

    [Fact]
    public async Task The_Text_Of_A_Button_Sits_In_The_Middle()
    {
        await OnUiThread(() =>
        {
            var window = new Window { Width = 400, Height = 200 };
            var button = new Button { Content = new TextBlock { Text = "Close" } };
            window.Content = button;
            window.Show();
            window.UpdateLayout();

            var text = button.GetVisualDescendants().OfType<TextBlock>().First();
            var top = text.TranslatePoint(new Point(0, 0), button);

            Assert.NotNull(top);
            var center = top!.Value.Y + text.Bounds.Height / 2;
            Assert.InRange(center, button.Bounds.Height / 2 - 0.5, button.Bounds.Height / 2 + 0.5);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Selected_Item_Of_A_ComboBox_Sits_In_The_Middle()
    {
        await OnUiThread(() =>
        {
            var window = new Window { Width = 400, Height = 200 };
            var combo = new ComboBox { ItemsSource = new[] { "No grouping" } };
            window.Content = combo;
            window.Show();
            window.UpdateLayout();

            // The item of the open drop down list must not be taken for the
            // selected item, both are text blocks in the visual tree.
            var text = combo.GetVisualDescendants()
                .OfType<TextBlock>()
                .First(t => !IsInPopup(t));

            var top = text.TranslatePoint(new Point(0, 0), combo);

            Assert.NotNull(top);
            var center = top!.Value.Y + text.Bounds.Height / 2;
            Assert.InRange(center, combo.Bounds.Height / 2 - 0.5, combo.Bounds.Height / 2 + 0.5);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Text_Of_A_Chip_Sits_In_The_Middle()
    {
        await OnUiThread(() =>
        {
            var window = new Window { Width = 400, Height = 200 };
            var chip = new Border { Classes = { "Chip" } };
            chip.Child = new TextBlock { Text = "Todo" };
            window.Content = chip;
            window.Show();
            window.UpdateLayout();

            var text = chip.GetVisualDescendants().OfType<TextBlock>().First();
            var top = text.TranslatePoint(new Point(0, 0), chip);

            Assert.NotNull(top);
            var center = top!.Value.Y + text.Bounds.Height / 2;
            Assert.InRange(center, chip.Bounds.Height / 2 - 0.5, chip.Bounds.Height / 2 + 0.5);

            window.Close();
        });
    }

    [Fact]
    public async Task The_Text_Of_A_Search_Field_Sits_In_The_Middle()
    {
        await OnUiThread(() =>
        {
            var window = new Window { Width = 400, Height = 200 };
            var panel = new StackPanel();
            var box = new TextBox { Classes = { "SearchBox" }, PlaceholderText = "Search" };

            // A text block of the same font gives the height of a line.
            var line = new TextBlock { Text = "M" };

            panel.Children.Add(box);
            panel.Children.Add(line);
            window.Content = panel;
            window.Show();
            window.UpdateLayout();

            // Avalonia aligns the text of a text box to the top of the
            // content area. The content area must therefore be exactly one
            // line high, otherwise the text is pushed to the top.
            var content = box.Bounds.Height - box.Padding.Top - box.Padding.Bottom;

            Assert.InRange(content, line.Bounds.Height - 0.5, line.Bounds.Height + 0.5);

            window.Close();
        });
    }
}
