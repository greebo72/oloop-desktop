using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// One entry of the type row of the new task dialog: the type itself plus the
/// theme keys of its icon and its type container.
///
/// The keys are resolved with a dynamic resource in the view, because a
/// resource key that depends on a runtime value cannot be bound with
/// <c>{StaticResource}</c>.
/// </summary>
public sealed partial class TaskTypeOption : ObservableObject
{
    private readonly Action<TaskType> _select;

    /// <summary>
    /// Creates the option for one task type.
    /// </summary>
    /// <param name="type">The type this option stands for.</param>
    /// <param name="select">Called when the option is picked in the type row.</param>
    public TaskTypeOption(TaskType type, Action<TaskType> select)
    {
        _select = select;
        Type = type;
        Name = type.DisplayName();
        IconKey = type switch
        {
            TaskType.todo => "IconChecklist",
            TaskType.question => "IconHelp",
            TaskType.decision => "IconGavel",
            TaskType.feature => "IconBuild",
            TaskType.bug => "IconBugReport",
            _ => "IconTaskAlt",
        };
        BackgroundKey = type switch
        {
            TaskType.todo => "TypeTodoBgBrush",
            TaskType.question => "TypeQuestionBgBrush",
            TaskType.decision => "TypeDecisionBgBrush",
            TaskType.feature => "TypeFeatureBgBrush",
            TaskType.bug => "TypeBugBgBrush",
            _ => "TypeClosedBgBrush",
        };
        ForegroundKey = type switch
        {
            TaskType.todo => "TypeTodoBrush",
            TaskType.question => "TypeQuestionBrush",
            TaskType.decision => "TypeDecisionBrush",
            TaskType.feature => "TypeFeatureBrush",
            TaskType.bug => "TypeBugBrush",
            _ => "TypeClosedBrush",
        };

        SelectCommand = new RelayCommand(() => _select(Type));
    }

    /// <summary>The task type of this option.</summary>
    public TaskType Type { get; }

    /// <summary>The name of the type, shown under the icon ("Todo", "Bug", …).</summary>
    public string Name { get; }

    /// <summary>The theme key of the icon of the type.</summary>
    public string IconKey { get; }

    /// <summary>The theme key of the container brush of the type.</summary>
    public string BackgroundKey { get; }

    /// <summary>The theme key of the foreground brush of the type.</summary>
    public string ForegroundKey { get; }

    /// <summary>Picks this type.</summary>
    public RelayCommand SelectCommand { get; }

    /// <summary>Whether this type is the currently selected one.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The opacity of the icon container — dimmed when not selected.</summary>
    public double ContainerOpacity => IsSelected ? 1.0 : 0.5;

    /// <summary>The thickness of the selection ring around the icon container.</summary>
    public Thickness SelectionThickness => IsSelected ? new Thickness(2) : new Thickness(0);

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(ContainerOpacity));
        OnPropertyChanged(nameof(SelectionThickness));
    }
}
