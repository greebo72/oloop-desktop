using CommunityToolkit.Mvvm.ComponentModel;

namespace OloopApp.ViewModels;

/// <summary>One label in the choice list of the label filter.</summary>
public partial class LabelOptionViewModel : ViewModelBase
{
    /// <summary>Creates the option.</summary>
    public LabelOptionViewModel(string name, int count, bool isSelected)
    {
        Name = name;
        Count = count;
        _isSelected = isSelected;
    }

    /// <summary>The label as it is shown.</summary>
    public string Name { get; }

    /// <summary>How many tasks carry the label.</summary>
    public int Count { get; }

    /// <summary>Whether the label is part of the filter.</summary>
    [ObservableProperty]
    private bool _isSelected;
}
