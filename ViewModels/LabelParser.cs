using OloopApp.Core.Io;

namespace OloopApp.ViewModels;

/// <summary>
/// Turns the comma separated label text of the task dialogs into the label
/// list the store expects.
///
/// The normalization rules (trim, drop empty labels, remove duplicates) are
/// not re-implemented here: <see cref="OloopStore.NormalizeLabels"/> is the
/// very method the store applies when it writes, so the dialog and the store
/// always agree.
/// </summary>
public static class LabelParser
{
    /// <summary>
    /// Splits comma separated text into labels and normalizes them the way the
    /// store does. Returns an empty list for null or blank text.
    /// </summary>
    public static IReadOnlyList<string> Parse(string? commaSeparated)
    {
        if (string.IsNullOrWhiteSpace(commaSeparated))
            return [];

        var parts = commaSeparated.Split(',', StringSplitOptions.RemoveEmptyEntries);
        return OloopStore.NormalizeLabels(parts);
    }
}
