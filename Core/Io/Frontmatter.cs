namespace OloopApp.Core.Io;

/// <summary>
/// Splits a data file into its YAML frontmatter and its Markdown body.
/// The Markdown body of a task file is the task's <c>description</c>.
/// </summary>
public static class Frontmatter
{
    /// <summary>The two parts of a data file.</summary>
    public readonly record struct Parts(string Yaml, string Body);

    /// <summary>
    /// Splits the file. The file must start with a <c>---</c> line; the
    /// frontmatter ends at the next line that is exactly <c>---</c>. Everything
    /// after that is the body.
    /// </summary>
    /// <exception cref="OloopFormatException">The file has no frontmatter.</exception>
    public static Parts Split(string content)
    {
        // Normalize line endings first: the format is written with LF.
        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');

        var parts = normalized.Split('\n');
        // A trailing newline produces a final empty entry — drop it.
        if (parts.Length > 0 && parts[^1].Length == 0)
            parts = parts[..^1];

        if (parts.Length == 0 || parts[0] != "---")
            throw new OloopFormatException("no frontmatter");

        var end = Array.IndexOf(parts, "---", 1);
        if (end < 0)
            throw new OloopFormatException("unterminated frontmatter");

        var yaml = string.Join('\n', parts[1..end]);
        var body = end + 1 < parts.Length ? string.Join('\n', parts[(end + 1)..]) : "";
        // The body is the description; leading and trailing newlines are not part of it.
        return new Parts(yaml, body.Trim('\n'));
    }
}
