namespace OloopApp.ViewModels;

/// <summary>
/// The rules a task title has to satisfy: it must not be empty and it must
/// fit into the 300 characters the oloop format allows.
/// </summary>
public static class TitleValidation
{
    /// <summary>The maximum length of a task title.</summary>
    public const int MaxLength = 300;

    /// <summary>
    /// Whether the title may be written: not empty after trimming and not
    /// longer than <see cref="MaxLength"/>.
    /// </summary>
    public static bool IsValid(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return false;

        return title.Trim().EnumerateRunes().Count() <= MaxLength;
    }

    /// <summary>
    /// The validation message for the title. Empty when the title is valid.
    /// </summary>
    public static string? Error(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "A title is required.";

        if (title.Trim().EnumerateRunes().Count() > MaxLength)
            return $"The title must have at most {MaxLength} characters.";

        return null;
    }
}
