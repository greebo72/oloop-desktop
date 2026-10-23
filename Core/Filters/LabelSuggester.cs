namespace OloopApp.Core.Filters;

/// <summary>The part of the label text the user is typing: the text between two commas.</summary>
/// <param name="Start">Where the label starts, after the comma and the spaces behind it.</param>
/// <param name="End">Where the label ends, at the next comma or the end of the text.</param>
/// <param name="Text">The label as typed, without the spaces around it.</param>
public sealed record LabelFragment(int Start, int End, string Text);

/// <summary>A label that was written in another case than the one that already exists.</summary>
public sealed record AdoptedLabel(string Typed, string Adopted);

/// <summary>
/// The logic behind the label input: which of the known labels fit what is
/// being typed, how a chosen label is put into the text, and which spelling
/// a new label takes when it only differs from a known one in case.
///
/// It works on text and numbers only, so it is tested without a window.
/// </summary>
public static class LabelSuggester
{
    /// <summary>How many suggestions are shown at most.</summary>
    public const int MaxSuggestions = 8;

    /// <summary>
    /// The label at the cursor: the text between the comma before the cursor
    /// and the comma behind it. A cursor outside the text is moved into it.
    /// </summary>
    public static LabelFragment CurrentFragment(string? text, int caret)
    {
        text ??= "";
        caret = Math.Clamp(caret, 0, text.Length);

        var start = caret == 0 ? 0 : text.LastIndexOf(',', caret - 1) + 1;
        var comma = text.IndexOf(',', caret);
        var end = comma < 0 ? text.Length : comma;

        // The spaces after the comma belong to the separator, not to the label.
        var first = start;
        while (first < end && char.IsWhiteSpace(text[first]))
            first++;

        var last = end;
        while (last > first && char.IsWhiteSpace(text[last - 1]))
            last--;

        return new LabelFragment(first, end, text[first..last]);
    }

    /// <summary>
    /// The known labels that fit the label at the cursor: first those that
    /// start with it, then those that contain it, each group with the most
    /// used label first. Nothing is suggested for an empty fragment, for a
    /// label the task already has, or for a label that is typed exactly.
    /// </summary>
    public static IReadOnlyList<LabelEntry> Suggest(
        IEnumerable<LabelEntry>? known,
        string? text,
        int caret,
        int max = MaxSuggestions)
    {
        if (known is null)
            return [];

        var fragment = CurrentFragment(text, caret);
        if (fragment.Text.Length == 0)
            return [];

        // The other labels of the text: a task does not carry a label twice.
        var taken = new HashSet<string>(LabelIndex.Comparer);
        foreach (var other in Split(text, fragment))
            taken.Add(other);

        var needle = fragment.Text;

        var fitting = known
            .Where(e => !taken.Contains(e.Name))
            .Where(e => !string.Equals(e.Name, needle, StringComparison.Ordinal))
            .Where(e => e.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return fitting
            .OrderBy(e => e.Name.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenByDescending(e => e.Count)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// Puts a chosen label into the text in place of the label at the cursor.
    /// At the end of the text a comma and a space follow, so the next label
    /// can be typed right away; before another comma only the label is
    /// replaced. Returns the new text and where the cursor goes.
    /// </summary>
    public static (string Text, int Caret) Accept(string? text, int caret, string label)
    {
        text ??= "";
        var fragment = CurrentFragment(text, caret);

        var before = text[..fragment.Start];
        var after = text[fragment.End..];

        if (after.Length == 0)
        {
            var result = before + label + ", ";
            return (result, result.Length);
        }

        var replaced = before + label + after;
        return (replaced, before.Length + label.Length);
    }

    /// <summary>
    /// Gives each new label the spelling of the known label that only
    /// differs in case. A label the task already carried (<paramref name="keep"/>)
    /// is left as it is: editing a task does not rewrite its old labels.
    /// Returns the labels, without duplicates, and the labels that changed.
    /// </summary>
    public static (IReadOnlyList<string> Labels, IReadOnlyList<AdoptedLabel> Adopted) Adopt(
        IEnumerable<string> labels,
        IEnumerable<string>? keep,
        IEnumerable<LabelEntry>? known)
    {
        var kept = new HashSet<string>(keep ?? [], StringComparer.Ordinal);
        var byName = new Dictionary<string, string>(LabelIndex.Comparer);
        foreach (var entry in known ?? [])
            byName.TryAdd(entry.Name, entry.Name);

        var result = new List<string>();
        var adopted = new List<AdoptedLabel>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var label in labels)
        {
            var name = label;
            if (!kept.Contains(label)
                && byName.TryGetValue(label, out var existing)
                && !string.Equals(existing, label, StringComparison.Ordinal))
            {
                name = existing;
                adopted.Add(new AdoptedLabel(label, existing));
            }

            if (seen.Add(name))
                result.Add(name);
        }

        return (result, adopted);
    }

    /// <summary>A line for the status bar: "Label 'bug' saved as 'Bug'." Empty when nothing changed.</summary>
    public static string Describe(IReadOnlyList<AdoptedLabel> adopted)
    {
        if (adopted.Count == 0)
            return "";

        var parts = adopted.Select(a => $"'{a.Typed}' as '{a.Adopted}'");
        return adopted.Count == 1
            ? $"Label {string.Join(", ", parts)} saved."
            : $"Labels {string.Join(", ", parts)} saved.";
    }

    private static IEnumerable<string> Split(string? text, LabelFragment current)
    {
        text ??= "";
        var position = 0;
        foreach (var part in text.Split(','))
        {
            var start = position;
            position += part.Length + 1;

            // The part that holds the cursor is the label being typed.
            if (current.Start >= start && current.End <= start + part.Length)
                continue;

            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                yield return trimmed;
        }
    }
}
