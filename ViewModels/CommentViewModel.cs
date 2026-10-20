using OloopApp.Core.Grouping;
using OloopApp.Core.Models;

namespace OloopApp.ViewModels;

/// <summary>One comment of a task.</summary>
public partial class CommentViewModel : ViewModelBase
{
    /// <summary>Creates the view model of a comment.</summary>
    public CommentViewModel(TaskComment comment)
    {
        Body = comment.Body;
        CreatedText = $"{RelativeAge.Format(comment.CreatedAt)} ago";
    }

    /// <summary>The text of the comment.</summary>
    public string Body { get; }

    /// <summary>How long ago the comment was written.</summary>
    public string CreatedText { get; }
}
