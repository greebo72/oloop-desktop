using OloopApp.Core.Filters;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The logic of the label input: the label at the cursor, the suggestions,
/// taking a suggestion over into the text, and the spelling of a new label.
/// </summary>
public class LabelSuggesterTests
{
    private static readonly LabelEntry[] Known =
    [
        new("ui", 20),
        new("parked", 12),
        new("deferred", 30),
        new("Bug", 7),
        new("spec", 9),
        new("performance", 4),
        new("backend", 3),
    ];

    private static string[] Names(IEnumerable<LabelEntry> entries) => entries.Select(e => e.Name).ToArray();

    // ---------------- The label at the cursor ----------------

    [Theory]
    [InlineData("", 0, "", 0, 0)]
    [InlineData("ui", 2, "ui", 0, 2)]
    [InlineData("ui, ba", 6, "ba", 4, 6)]
    [InlineData("ui,  ba ", 8, "ba", 5, 8)]
    [InlineData("ui, ba, spec", 5, "ba", 4, 6)]
    [InlineData("ui, ", 4, "", 4, 4)]
    [InlineData("ui, ba", 99, "ba", 4, 6)]
    public void The_Fragment_Is_The_Text_Between_Two_Commas(
        string text, int caret, string expected, int start, int end)
    {
        var fragment = LabelSuggester.CurrentFragment(text, caret);

        Assert.Equal(expected, fragment.Text);
        Assert.Equal(start, fragment.Start);
        Assert.Equal(end, fragment.End);
    }

    // ---------------- Suggestions ----------------

    [Fact]
    public void A_Label_That_Only_Contains_The_Text_Is_Suggested_Too()
    {
        // "er" is inside "deferred" and "performance", at the start of none.
        var result = LabelSuggester.Suggest(Known, "er", 2);

        Assert.Equal(["deferred", "performance"], Names(result));
    }

    [Fact]
    public void A_Prefix_Match_Wins_Over_A_Label_That_Only_Contains_The_Text()
    {
        // "spec" is the most used label but only contains the p.
        var known = new LabelEntry[] { new("spec", 50), new("parked", 1), new("performance", 3) };

        var result = LabelSuggester.Suggest(known, "p", 1);

        Assert.Equal(["performance", "parked", "spec"], Names(result));
    }

    [Fact]
    public void Within_A_Group_The_Most_Used_Label_Comes_First()
    {
        var result = LabelSuggester.Suggest(Known, "p", 1);

        // Starting with p: parked (12), performance (4). Containing p: spec (9).
        Assert.Equal(["parked", "performance", "spec"], Names(result));
    }

    [Fact]
    public void The_Match_Ignores_The_Case()
    {
        var result = LabelSuggester.Suggest(Known, "BU", 2);

        Assert.Equal(["Bug"], Names(result));
    }

    [Fact]
    public void Nothing_Is_Suggested_For_An_Empty_Fragment()
    {
        Assert.Empty(LabelSuggester.Suggest(Known, "", 0));
        Assert.Empty(LabelSuggester.Suggest(Known, "ui, ", 4));
        Assert.Empty(LabelSuggester.Suggest(Known, "   ", 3));
    }

    [Fact]
    public void Nothing_Is_Suggested_Without_Known_Labels()
    {
        Assert.Empty(LabelSuggester.Suggest(null, "ui", 2));
        Assert.Empty(LabelSuggester.Suggest([], "ui", 2));
    }

    [Fact]
    public void A_Label_The_Task_Already_Has_Is_Not_Suggested_Again()
    {
        var result = LabelSuggester.Suggest(Known, "ui, pa", 6);

        Assert.Equal(["parked"], Names(result));

        // The other label is "ui", not suggested for "u" at the end.
        Assert.DoesNotContain("ui", Names(LabelSuggester.Suggest(Known, "ui, u", 5)));
    }

    [Fact]
    public void A_Label_That_Is_Typed_Exactly_Is_Not_Suggested_But_Another_Case_Is()
    {
        Assert.Empty(LabelSuggester.Suggest(Known, "ui", 2));
        Assert.Equal(["Bug"], Names(LabelSuggester.Suggest(Known, "bug", 3)));
    }

    [Fact]
    public void At_Most_Eight_Labels_Are_Suggested()
    {
        var many = Enumerable.Range(0, 20).Select(i => new LabelEntry("step-" + i, i)).ToList();

        var result = LabelSuggester.Suggest(many, "step", 4);

        Assert.Equal(LabelSuggester.MaxSuggestions, result.Count);
        Assert.Equal("step-19", result[0].Name);
    }

    [Fact]
    public void The_Label_In_The_Middle_Of_The_Text_Is_The_One_That_Is_Completed()
    {
        // The cursor sits behind "pa" in "ui, pa, spec".
        var result = LabelSuggester.Suggest(Known, "ui, pa, spec", 6);

        Assert.Equal(["parked"], Names(result));
    }

    // ---------------- Taking a suggestion over ----------------

    [Fact]
    public void A_Suggestion_At_The_End_Gets_A_Comma_And_A_Space()
    {
        var (text, caret) = LabelSuggester.Accept("ui, pa", 6, "parked");

        Assert.Equal("ui, parked, ", text);
        Assert.Equal(text.Length, caret);
    }

    [Fact]
    public void A_Suggestion_At_The_Start_Replaces_The_Typed_Part()
    {
        var (text, caret) = LabelSuggester.Accept("pa", 2, "parked");

        Assert.Equal("parked, ", text);
        Assert.Equal(8, caret);
    }

    [Fact]
    public void A_Suggestion_Before_Another_Label_Only_Replaces_The_Label()
    {
        var (text, caret) = LabelSuggester.Accept("ui, pa, spec", 6, "parked");

        Assert.Equal("ui, parked, spec", text);
        Assert.Equal("ui, parked".Length, caret);
    }

    // ---------------- The spelling of a new label ----------------

    [Fact]
    public void A_New_Label_Takes_The_Spelling_Of_The_Existing_One()
    {
        var (labels, adopted) = LabelSuggester.Adopt(["bug", "ui"], keep: [], Known);

        Assert.Equal(["Bug", "ui"], labels);
        var change = Assert.Single(adopted);
        Assert.Equal("bug", change.Typed);
        Assert.Equal("Bug", change.Adopted);
    }

    [Fact]
    public void A_Label_The_Task_Already_Carried_Keeps_Its_Spelling()
    {
        var (labels, adopted) = LabelSuggester.Adopt(["bug", "ui"], keep: ["bug"], Known);

        Assert.Equal(["bug", "ui"], labels);
        Assert.Empty(adopted);
    }

    [Fact]
    public void A_Label_That_Does_Not_Exist_Yet_Is_Written_As_Typed()
    {
        var (labels, adopted) = LabelSuggester.Adopt(["Brand-New"], keep: [], Known);

        Assert.Equal(["Brand-New"], labels);
        Assert.Empty(adopted);
    }

    [Fact]
    public void A_Label_In_The_Right_Spelling_Is_Not_Reported()
    {
        var (labels, adopted) = LabelSuggester.Adopt(["Bug"], keep: [], Known);

        Assert.Equal(["Bug"], labels);
        Assert.Empty(adopted);
    }

    [Fact]
    public void Two_Spellings_Of_One_Label_Become_One()
    {
        var (labels, adopted) = LabelSuggester.Adopt(["Bug", "bug", "BUG"], keep: [], Known);

        Assert.Equal(["Bug"], labels);
        Assert.Equal(2, adopted.Count);
    }

    [Fact]
    public void The_Status_Line_Names_The_Changed_Labels()
    {
        Assert.Equal("", LabelSuggester.Describe([]));
        Assert.Equal(
            "Label 'bug' as 'Bug' saved.",
            LabelSuggester.Describe([new AdoptedLabel("bug", "Bug")]));
        Assert.Equal(
            "Labels 'bug' as 'Bug', 'UI' as 'ui' saved.",
            LabelSuggester.Describe([new AdoptedLabel("bug", "Bug"), new AdoptedLabel("UI", "ui")]));
    }
}
