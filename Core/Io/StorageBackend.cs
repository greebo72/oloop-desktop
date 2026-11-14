namespace OloopApp.Core.Io;

/// <summary>Where the projects and tasks of the app live.</summary>
public enum StorageBackend
{
    /// <summary>A folder of Markdown files. The default.</summary>
    Folder,

    /// <summary>An oloop server; the data is encrypted on this computer.</summary>
    Server,
}

/// <summary>
/// The address of an oloop server. The server runs behind a reverse proxy
/// that ends TLS, so the address is https; plain http is only accepted for
/// the computer itself, where a server under development listens.
/// </summary>
public static class ServerEndpoint
{
    /// <summary>
    /// Checks the address the user typed. Returns the address without a
    /// trailing slash, or null with the reason in <paramref name="error"/>.
    /// </summary>
    public static string? Normalize(string? text, out string? error)
    {
        var trimmed = text?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            error = "The server address is required.";
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.Host.Length == 0)
        {
            error = $"'{trimmed}' is not a server address. Use https://host.";
            return null;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            error = "The server address must not contain a user name, a query or a fragment.";
            return null;
        }

        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            error = "The server address must start with https://. Plain http is only allowed for localhost.";
            return null;
        }

        error = null;
        return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
