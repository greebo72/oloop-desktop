namespace OloopApp.ViewModels;

/// <summary>
/// Remembers which groups of a list are collapsed, by the title of the group.
///
/// A list builds its groups again on every reload, so the state cannot live
/// in the group alone: the list keeps this object and hands the state to
/// the new groups. It is not saved in the settings.
/// </summary>
public sealed class CollapseState
{
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);

    /// <summary>Whether the group with the given title is collapsed.</summary>
    public bool IsCollapsed(string? title)
        => title is not null && _collapsed.Contains(title);

    /// <summary>Remembers the state of a group.</summary>
    public void Set(string? title, bool expanded)
    {
        if (string.IsNullOrEmpty(title))
            return;

        if (expanded)
            _collapsed.Remove(title);
        else
            _collapsed.Add(title);
    }

    /// <summary>Forgets everything: all groups are expanded again.</summary>
    public void Clear() => _collapsed.Clear();
}
