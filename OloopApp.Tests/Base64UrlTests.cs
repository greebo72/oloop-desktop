using OloopApp.Core.Crypto;
using Xunit;

namespace OloopApp.Tests;

/// <summary>base64url as the API uses it: no padding, and one spelling for one value.</summary>
public class Base64UrlTests
{
    [Fact]
    public void Every_Byte_Value_Survives_A_Round_Trip()
    {
        var data = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();

        Assert.Equal(data, Base64Url.Decode(Base64Url.Encode(data)));
    }

    [Fact]
    public void The_Url_Alphabet_Is_Used_And_There_Is_No_Padding()
        => Assert.Equal("-_8", Base64Url.Encode([0xfb, 0xff]));

    [Fact]
    public void Nothing_Is_Empty()
    {
        Assert.Equal("", Base64Url.Encode([]));
        Assert.Empty(Base64Url.Decode(""));
    }

    [Theory]
    [InlineData("a")]
    [InlineData("ab=")]
    [InlineData("ab==")]
    [InlineData("a b")]
    [InlineData("a+b/")]
    [InlineData("ü")]
    [InlineData("AB\n")]
    [InlineData("-_9")]
    public void Text_That_Is_Not_The_Shortest_Form_Is_Refused(string text)
        => Assert.Throws<OloopCryptoException>(() => Base64Url.Decode(text));

    [Fact]
    public void Another_Spelling_Of_The_Same_Bytes_Is_Refused()
    {
        // 16 bytes are 22 characters = 132 bits: the last 4 bits of the last character are not used
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";
        var text = Base64Url.Encode(new byte[16]);
        var other = text[..^1] + alphabet[alphabet.IndexOf(text[^1]) | 1];

        Assert.NotEqual(text, other);
        Assert.Throws<OloopCryptoException>(() => Base64Url.Decode(other));
    }
}
