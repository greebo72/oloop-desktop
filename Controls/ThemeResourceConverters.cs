using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace OloopApp.Controls;

/// <summary>
/// Looks a theme resource up by its key.
///
/// The view models expose the resource keys of the theme (for example
/// <c>TypeTodoBgBrush</c>) because a key that depends on a runtime value
/// cannot be bound with <c>{StaticResource}</c> and <c>{DynamicResource}</c>
/// only takes a constant key. The converters are the escape hatch: they
/// resolve the key against the resources of the running application.
/// </summary>
public static class ThemeResourceLookup
{
    /// <summary>Resolves a resource key, or null when the key is unknown.</summary>
    public static object? Resolve(string? key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        var app = Application.Current;
        if (app is null)
            return null;

        return app.Resources.TryGetResource(key, null, out var value)
            ? value
            : null;
    }
}

/// <summary>Resolves a theme resource key to a brush.</summary>
public sealed class ResourceBrushConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static ResourceBrushConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? ThemeResourceLookup.Resolve(key) as IBrush : null;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Resolves a theme resource key to an icon geometry.</summary>
public sealed class ResourceGeometryConverter : IValueConverter
{
    /// <summary>The shared instance.</summary>
    public static ResourceGeometryConverter Instance { get; } = new();

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string key ? ThemeResourceLookup.Resolve(key) as Geometry : null;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
