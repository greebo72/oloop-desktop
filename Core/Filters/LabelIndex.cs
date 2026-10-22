using OloopApp.Core.Models;

namespace OloopApp.Core.Filters;

/// <summary>One label of the label filter: the name to show and how many tasks carry it.</summary>
public sealed record LabelEntry(string Name, int Count);

/// <summary>
/// The labels that occur in a set of tasks, for the choice list of the label
/// filter. Labels are compared without regard to case and without a culture
/// (<see cref="StringComparison.OrdinalIgnoreCase"/>), so "Bug" and "bug" are
/// one label on every system and in every language setting. The files are not
/// changed, each task keeps the spelling it was written with.
/// </summary>
public static class LabelIndex
{
    /// <summary>The comparison of labels.</summary>
    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Builds the list: every label once, sorted alphabetically, with the
    /// number of tasks that carry it in any spelling. A task counts once for
    /// a label, also when it carries two spellings of it. The name shown is
    /// the spelling that occurs most often, and the alphabetically first one
    /// when several occur equally often.
    /// </summary>
    public static IReadOnlyList<LabelEntry> Build(IEnumerable<OloopTask> tasks)
    {
        var spellings = new Dictionary<string, Dictionary<string, int>>(Comparer);
        var counts = new Dictionary<string, int>(Comparer);

        foreach (var task in tasks)
        {
            var seen = new HashSet<string>(Comparer);

            foreach (var label in task.Labels)
            {
                if (string.IsNullOrWhiteSpace(label))
                    continue;

                if (!spellings.TryGetValue(label, out var forms))
                    spellings[label] = forms = new Dictionary<string, int>(StringComparer.Ordinal);

                forms[label] = forms.GetValueOrDefault(label) + 1;

                if (seen.Add(label))
                    counts[label] = counts.GetValueOrDefault(label) + 1;
            }
        }

        return spellings
            .Select(pair => new LabelEntry(
                pair.Value
                    .OrderByDescending(f => f.Value)
                    .ThenBy(f => f.Key, StringComparer.Ordinal)
                    .First().Key,
                counts[pair.Key]))
            .OrderBy(e => e.Name, Comparer)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .ToList();
    }
}
