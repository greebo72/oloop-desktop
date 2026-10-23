using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>
/// How many open tasks of one type a project has: one chip on the project
/// card, in the colours of the type.
/// </summary>
public sealed class TypeCountViewModel
{
    /// <summary>Creates the count of a type.</summary>
    public TypeCountViewModel(TaskType type, int count)
    {
        Type = type;
        Count = count;
    }

    /// <summary>The type of the tasks.</summary>
    public TaskType Type { get; }

    /// <summary>How many open tasks of this type the project has.</summary>
    public int Count { get; }

    /// <summary>The text of the chip, for example "3 Bug".</summary>
    public string Text => $"{Count} {Type.DisplayName()}";

    /// <summary>The background brush key of the chip.</summary>
    public string BackgroundKey => Type switch
    {
        TaskType.todo => "TypeTodoBgBrush",
        TaskType.question => "TypeQuestionBgBrush",
        TaskType.decision => "TypeDecisionBgBrush",
        TaskType.feature => "TypeFeatureBgBrush",
        TaskType.bug => "TypeBugBgBrush",
        _ => "TypeClosedBgBrush",
    };

    /// <summary>The text brush key of the chip.</summary>
    public string ForegroundKey => Type switch
    {
        TaskType.todo => "TypeTodoBrush",
        TaskType.question => "TypeQuestionBrush",
        TaskType.decision => "TypeDecisionBrush",
        TaskType.feature => "TypeFeatureBrush",
        TaskType.bug => "TypeBugBrush",
        _ => "TypeClosedBrush",
    };
}
