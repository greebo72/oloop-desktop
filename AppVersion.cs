using System.Reflection;

namespace OloopApp;

/// <summary>
/// The version of the app.
///
/// It is read from the assembly at runtime, so the release pipeline does not
/// have to maintain it: the tag of the release becomes the version of the
/// package, and that is the version that ends up in the binary.
/// </summary>
public static class AppVersion
{
    /// <summary>The version, for example "0.2.1".</summary>
    public static string Text { get; } = ReadVersion();

    /// <summary>The title of the main window, for example "oloop 0.2.1".</summary>
    public static string WindowTitle { get; } = $"oloop {Text}";

    private static string ReadVersion()
    {
        var assembly = typeof(AppVersion).Assembly;

        // The SDK can append the commit hash to the informational version
        // ("0.2.1+abc123"). Only the version itself belongs in the title.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
            return informational.Split('+')[0];

        // The fallback for a build without the attribute: the assembly
        // version has four parts ("0.2.1.0"), the title needs three.
        return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    }
}
