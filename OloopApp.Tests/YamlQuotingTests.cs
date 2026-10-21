using OloopApp.Core.Io;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// The quoting of the frontmatter.
///
/// The reference clients parse the frontmatter with YAML 1.1 semantics. A
/// plain scalar that YAML 1.1 resolves to another type changes its type on
/// the way back in — a title "on" becomes the boolean true, a title "12:30"
/// the number 750. The spec forbids that: a round-trip changes no value.
///
/// The values below are the edge cases of the resolver patterns of PyYAML.
/// They are checked without the reference CLI, so the rule holds even on a
/// machine where the CLI is not installed.
/// </summary>
public class YamlQuotingTests
{
    /// <summary>The values that YAML 1.1 resolves to a bool, a null, a number or a date.</summary>
    public static TheoryData<string> ResolvedToAnotherType() => new()
    {
        // Booleans.
        "on", "off", "On", "ON", "OFF",
        "yes", "no", "Yes", "YES", "No", "NO",
        "true", "false", "True", "TRUE", "False", "FALSE",

        // Nulls.
        "~", "null", "Null", "NULL",

        // Integers.
        "42", "-7", "+7", "0", "007",
        "0x1f", "0b101", "017",
        "1_000",
        "12:30", "12:30:15", "1:2:3", "+12:30",

        // Floats.
        "3.14", ".5", "1.5e+5", "12:30.5", "1_000.5",
        ".inf", ".Inf", ".INF", "-.inf", "+.inf",
        ".nan", ".NaN", ".NAN",

        // Timestamps.
        "2026-10-20",
        "2026-10-20T10:00:00Z",
        "2026-10-20 10:00:00",
        "2026-10-20t10:00:00",
        "2026-10-20T10:00:00.123Z",
        "2026-10-20T10:00:00+02:00",
    };

    /// <summary>The values that look hostile but that the resolver does not touch.</summary>
    public static TheoryData<string> LeftAlone() => new()
    {
        "12:60",
        "0:30",
        "0o17",
        "1e5",
        "2026-1-1",
        "on the way",
        "null pointer",
        "yes man",
        "42ever",
    };

    [Theory]
    [MemberData(nameof(ResolvedToAnotherType))]
    public void A_Value_That_Yaml_Resolves_To_Another_Type_Is_Quoted(string value)
    {
        var quoted = YamlFrontmatter.Quote(value);

        Assert.StartsWith("\"", quoted, StringComparison.Ordinal);
        Assert.EndsWith("\"", quoted, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(LeftAlone))]
    public void A_Value_That_Yaml_Leaves_Alone_Stays_Unquoted(string value)
    {
        // The quoting must not be too eager: a plain value stays plain, so
        // the files look like the ones of the reference clients.
        Assert.Equal(value, YamlFrontmatter.Quote(value));
    }

    [Fact]
    public void An_Empty_Value_Is_Quoted()
    {
        Assert.Equal("\"\"", YamlFrontmatter.Quote(""));
    }

    /// <summary>
    /// The values with a character that a plain scalar cannot hold. They
    /// have to be quoted — the parser would stop at the line break or read a
    /// different node.
    /// </summary>
    public static TheoryData<string> NeedsQuotes() => new()
    {
        "a: b",
        "a:",
        "a #b",
        "- a",
        "#a",
        "a\nb",
        "a\tb",
        " lead",
        "trail ",
        ">x",
        "*x",
        "&x",
        "!x",
        "|x",
        "%x",
        "@x",
        "`x",
        "{x}",
        "[x]",
        ",x",
    };

    [Theory]
    [MemberData(nameof(NeedsQuotes))]
    public void A_Value_That_A_Plain_Scalar_Cannot_Hold_Is_Quoted(string value)
    {
        Assert.StartsWith("\"", YamlFrontmatter.Quote(value), StringComparison.Ordinal);
    }

    /// <summary>
    /// The values with a character that a plain scalar can hold. The
    /// reference emitter leaves them plain, and so does this client.
    /// </summary>
    public static TheoryData<string> StaysPlain() => new()
    {
        "a\"b",
        "a\\b",
        "a:b",
        "a*b",
        "a&b",
        "a!b",
        "a|b",
        "a?b",
        "a[b]",
        "a{b}",
        "a,b",
        "a`b",
        "a'b",
        "a#b",
        "-x",
        "?x",
        ":x",
        "x-y",
        "a  b",
        "100%",
    };

    [Theory]
    [MemberData(nameof(StaysPlain))]
    public void A_Value_That_A_Plain_Scalar_Can_Hold_Stays_Plain(string value)
    {
        Assert.Equal(value, YamlFrontmatter.Quote(value));
    }

    [Fact]
    public void A_Quoted_Value_Keeps_Its_Text()
    {
        // The quotes and the backslash are escaped, the text stays the same.
        Assert.Equal("\"\\\"a\\\"\"", YamlFrontmatter.Quote("\"a\""));
        Assert.Equal("\"a\\nb\"", YamlFrontmatter.Quote("a\nb"));
        Assert.Equal("\"a\\tb\"", YamlFrontmatter.Quote("a\tb"));

        // A backslash alone needs no quotes — a plain scalar holds it.
        Assert.Equal("a\\b", YamlFrontmatter.Quote("a\\b"));

        // In a quoted value it is escaped.
        Assert.Equal("\"#a\\\\b\"", YamlFrontmatter.Quote("#a\\b"));
    }

    [Fact]
    public void A_Null_Node_Is_Written_As_Null()
    {
        var map = new YamlNode.Mapping(
        [
            new("imagePath", YamlNode.NullNode.Instance),
            new("title", new YamlNode.Scalar("null", YamlDotNet.Core.ScalarStyle.Plain)),
        ]);

        var rendered = YamlFrontmatter.Render(map);

        // The explicit null is a null ...
        Assert.Contains("imagePath: null", rendered, StringComparison.Ordinal);

        // ... and the title that only looks like one is a string.
        Assert.Contains("title: \"null\"", rendered, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Title_With_A_Line_Break_Survives_The_File()
    {
        // A plain scalar ends at the line break — the file would be broken.
        // The title has to be quoted.
        var title = "First line\nSecond line";

        var written = TaskIo.Write(new Core.Models.OloopTask
        {
            FormatVersion = 1,
            Id = "22222222-2222-4222-8222-222222222222",
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = Core.Models.TaskType.todo,
            Title = title,
            Status = Core.Models.TaskStatus.open,
            CreatedAt = "2026-01-01T09:00:00Z",
            CreatedBy = "claude",
            UpdatedAt = "2026-01-01T09:00:00Z",
            UpdatedBy = "claude",
        });

        var read = TaskIo.Read(
            "22222222-2222-4222-8222-222222222222", written,
            "11111111-1111-4111-8111-111111111111");

        Assert.Equal(title, read.Title);
    }

    [Fact]
    public void A_Title_With_A_Tab_Survives_The_File()
    {
        var title = "Column one\tColumn two";

        var written = TaskIo.Write(new Core.Models.OloopTask
        {
            FormatVersion = 1,
            Id = "22222222-2222-4222-8222-222222222222",
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = Core.Models.TaskType.todo,
            Title = title,
            Status = Core.Models.TaskStatus.open,
            CreatedAt = "2026-01-01T09:00:00Z",
            CreatedBy = "claude",
            UpdatedAt = "2026-01-01T09:00:00Z",
            UpdatedBy = "claude",
        });

        var read = TaskIo.Read(
            "22222222-2222-4222-8222-222222222222", written,
            "11111111-1111-4111-8111-111111111111");

        Assert.Equal(title, read.Title);
    }

    [Fact]
    public void A_Title_Of_Null_Is_Read_Back_As_The_Text_Null()
    {
        var written = TaskIo.Write(new Core.Models.OloopTask
        {
            FormatVersion = 1,
            Id = "22222222-2222-4222-8222-222222222222",
            ProjectId = "11111111-1111-4111-8111-111111111111",
            Type = Core.Models.TaskType.todo,
            Title = "null",
            Status = Core.Models.TaskStatus.open,
            CreatedAt = "2026-01-01T09:00:00Z",
            CreatedBy = "claude",
            UpdatedAt = "2026-01-01T09:00:00Z",
            UpdatedBy = "claude",
        });

        var read = TaskIo.Read(
            "22222222-2222-4222-8222-222222222222", written,
            "11111111-1111-4111-8111-111111111111");

        Assert.Equal("null", read.Title);
        Assert.Null(read.ImagePath);
    }
}
