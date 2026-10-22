using Avalonia.Controls;
using Avalonia.Media;

namespace OloopApp;

/// <summary>
/// The size of the whole UI. One factor for every window of the app: the
/// content of a window is scaled by a layout transform, so text, icons and
/// spacing grow together and nothing is clipped by a fixed height.
///
/// The window sizes are scaled with it. Without that a dialog would keep its
/// width and its content would be squeezed.
/// </summary>
public static class UiScale
{
    private static double _factor = 1.0;

    /// <summary>Raised when <see cref="Factor"/> changed.</summary>
    public static event Action? Changed;

    /// <summary>
    /// The scale, 1.0 is the normal size. Windows that are open follow a
    /// change right away.
    /// </summary>
    public static double Factor
    {
        get => _factor;
        set
        {
            if (value <= 0 || Math.Abs(value - _factor) < 0.0001)
                return;

            _factor = value;
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Scales the content of the window and follows later changes of
    /// <see cref="Factor"/> until the window closes. Call it once, after
    /// InitializeComponent.
    /// </summary>
    public static void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var content = window.Content;
        window.Content = null;

        var scale = new ScaleTransform(1, 1);
        window.Content = new LayoutTransformControl
        {
            LayoutTransform = scale,
            Child = content as Control,
        };

        // The sizes the window was declared with, at scale 1.
        var applied = 1.0;
        var width = window.Width;
        var minWidth = window.MinWidth;
        var height = window.Height;
        var minHeight = window.MinHeight;

        void Apply()
        {
            var factor = _factor;
            scale.ScaleX = factor;
            scale.ScaleY = factor;

            // A window the user resized keeps its proportion on a change.
            var ratio = factor / applied;
            applied = factor;

            if (!double.IsNaN(window.Width))
                window.Width *= ratio;
            if (!double.IsNaN(window.Height))
                window.Height *= ratio;

            window.MinWidth = minWidth * factor;
            window.MinHeight = minHeight * factor;
        }

        // Starts at the declared size times the factor. The declared values
        // are NaN or 0 for "not set", which stay as they are when scaled.
        window.Width = width;
        window.Height = height;
        Apply();

        Changed += Apply;
        window.Closed += (_, _) => Changed -= Apply;
    }
}
