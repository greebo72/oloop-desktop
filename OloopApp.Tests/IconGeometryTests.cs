using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// A PathIcon fills its geometry. An icon that is drawn as a stroke - a
/// plus made of two lines, a cross - has no area, so nothing is painted,
/// while the layout still reserves the room for it. That is how the "New
/// task" button ended up with a gap in front of the text.
///
/// Every icon in the theme therefore has to be a filled shape. The test
/// renders the geometry and compares it with an empty picture: if the two
/// are the same, the icon would be invisible.
/// </summary>
public class IconGeometryTests
{
    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(IconGeometryTests).Assembly);

    [Fact]
    public async Task Every_Icon_In_The_Theme_Paints_Something()
    {
        await Session.Dispatch(() =>
        {
            // The theme is loaded on its own: the app merges it into the
            // resources of the application, and a merged dictionary does not
            // show its keys in the dictionary that holds it.
            var theme = (ResourceDictionary)AvaloniaXamlLoader.Load(
                new Uri("avares://oloop/Themes/Theme.axaml"));

            var icons = new Dictionary<string, Geometry>();

            foreach (var key in theme.Keys.OfType<string>()
                         .Where(k => k.StartsWith("Icon", StringComparison.Ordinal)))
            {
                if (theme.TryGetResource(key, null, out var value) && value is Geometry geometry)
                    icons[key] = geometry;
            }

            Assert.NotEmpty(icons);

            foreach (var (key, geometry) in icons)
            {
                Assert.True(PaintsSomething(geometry), $"{key} has no fill area - a PathIcon would draw nothing.");
            }
        }, CancellationToken.None);
    }

    /// <summary>Renders the geometry and checks that it differs from an empty picture.</summary>
    private static bool PaintsSomething(Geometry geometry)
    {
        var bounds = geometry.Bounds;

        if (bounds.Width <= 0 || bounds.Height <= 0)
            return false;

        var size = new PixelSize(
            Math.Max(1, (int)Math.Ceiling(bounds.Width)),
            Math.Max(1, (int)Math.Ceiling(bounds.Height)));

        using var empty = new RenderTargetBitmap(size, new Vector(96, 96));
        using var painted = new RenderTargetBitmap(size, new Vector(96, 96));

        using (var context = painted.CreateDrawingContext())
        {
            using (context.PushTransform(Matrix.CreateTranslation(-bounds.X, -bounds.Y)))
            {
                context.DrawGeometry(Brushes.White, null, geometry);
            }
        }

        return !ToPng(empty).SequenceEqual(ToPng(painted));
    }

    private static byte[] ToPng(RenderTargetBitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, new PngBitmapEncoderOptions());
        return stream.ToArray();
    }
}
