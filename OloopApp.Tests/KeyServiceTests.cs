using System.Text;
using OloopApp.Core.Crypto;
using OloopApp.Core.Server;
using Xunit;

namespace OloopApp.Tests;

public class KeyServiceTests
{
    private readonly FakeOloopServer _server = new();

    private async Task<KeyService> SignInAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        return new KeyService(api);
    }

    // ---------------- Recovery code ----------------

    [Fact]
    public void A_Recovery_Code_Is_Thirteen_Groups_And_Reads_Back_Whatever_The_Spacing()
    {
        var code = RecoveryCode.Create();
        var text = RecoveryCode.Format(code);

        Assert.Equal(13, text.Split('-').Length);
        Assert.Equal(code, RecoveryCode.TryParse(text));
        Assert.Equal(code, RecoveryCode.TryParse(text.ToLowerInvariant().Replace('-', ' ')));
        Assert.Equal(code, RecoveryCode.TryParse("  " + text.Replace("-", "") + "\n"));
    }

    [Fact]
    public void Text_That_Is_Not_A_Recovery_Code_Is_Refused()
    {
        var text = RecoveryCode.Format(RecoveryCode.Create()).Replace("-", "");
        Assert.Null(RecoveryCode.TryParse(""));
        Assert.Null(RecoveryCode.TryParse(text[..^1]));
        Assert.Null(RecoveryCode.TryParse(text + "A"));
        Assert.Null(RecoveryCode.TryParse("1" + text[1..])); // 1 is not in the alphabet
    }

    [Fact]
    public void Unused_Bits_Of_The_Last_Letter_Must_Be_Zero()
    {
        var zero = RecoveryCode.Format(new byte[32]).Replace("-", "");
        Assert.EndsWith("A", zero);
        Assert.Null(RecoveryCode.TryParse(zero[..^1] + "B"));
    }

    // ---------------- Setup and unlock ----------------

    [Fact]
    public async Task Nothing_Is_Stored_Before_The_Setup_Is_Committed()
    {
        var keys = await SignInAsync();
        Assert.Null(await keys.FetchBundleAsync());

        await keys.PrepareSetupAsync("a long passphrase");
        Assert.Null(await keys.FetchBundleAsync());
        Assert.Null(_server.Bundle);
    }

    [Fact]
    public async Task A_Second_Device_Unlocks_The_Bundle_With_The_Passphrase_Or_The_Recovery_Code()
    {
        var first = await SignInAsync();
        var pending = await first.PrepareSetupAsync("Grüne Wiese 42!");
        var made = await first.CommitSetupAsync(pending);

        var second = await SignInAsync();
        var bundle = await second.FetchBundleAsync();
        Assert.NotNull(bundle);

        var byPassphrase = await second.UnlockWithPassphraseAsync(bundle, "Grüne Wiese 42!");
        Assert.Equal(made.SecretKey, byPassphrase.SecretKey);
        Assert.Equal(made.PublicKey, byPassphrase.PublicKey);
        Assert.Equal(_server.UserId, byPassphrase.UserId);

        var byCode = second.UnlockWithRecoveryCode(bundle, pending.RecoveryCodeText.ToLowerInvariant());
        Assert.Equal(made.SecretKey, byCode.SecretKey);
    }

    [Fact]
    public async Task A_Wrong_Passphrase_Or_Recovery_Code_Does_Not_Unlock()
    {
        var keys = await SignInAsync();
        var pending = await keys.PrepareSetupAsync("Grüne Wiese 42!");
        await keys.CommitSetupAsync(pending);
        var bundle = (await keys.FetchBundleAsync())!;

        await Assert.ThrowsAsync<OloopCryptoException>(() => keys.UnlockWithPassphraseAsync(bundle, "grüne wiese 42!"));
        // ü as u plus a combining diaeresis is another passphrase: the spec uses the text as typed.
        await Assert.ThrowsAsync<OloopCryptoException>(() => keys.UnlockWithPassphraseAsync(bundle, "Grüne Wiese 42!"));
        Assert.Throws<OloopCryptoException>(() => keys.UnlockWithRecoveryCode(bundle, RecoveryCode.Format(RecoveryCode.Create())));
        Assert.Throws<OloopCryptoException>(() => keys.UnlockWithRecoveryCode(bundle, "not a code"));
    }

    [Fact]
    public async Task The_Stored_Bundle_Holds_No_Secret_In_Clear()
    {
        var keys = await SignInAsync();
        var pending = await keys.PrepareSetupAsync("Grüne Wiese 42!");
        var made = await keys.CommitSetupAsync(pending);

        var stored = _server.Bundle!.ToJsonString();
        Assert.DoesNotContain(Base64Url.Encode(made.SecretKey), stored);
        Assert.DoesNotContain(Base64Url.Encode(pending.RecoveryCode), stored);
        Assert.DoesNotContain("Wiese", stored);
        Assert.Contains(Base64Url.Encode(made.PublicKey), stored);
    }

    [Fact]
    public async Task A_Device_That_Is_Late_Does_Not_Replace_The_Bundle_Of_Another()
    {
        var one = await SignInAsync();
        var two = await SignInAsync();
        var pendingOne = await one.PrepareSetupAsync("first passphrase 1");
        var pendingTwo = await two.PrepareSetupAsync("second passphrase 2");

        await one.CommitSetupAsync(pendingOne);
        var error = await Assert.ThrowsAsync<ServerApiException>(() => two.CommitSetupAsync(pendingTwo));
        Assert.True(error.IsConflict);

        var bundle = (await one.FetchBundleAsync())!;
        await one.UnlockWithPassphraseAsync(bundle, "first passphrase 1");
    }

    [Fact]
    public async Task A_Passphrase_With_A_Lone_Surrogate_Is_Refused_Before_Anything_Is_Made()
    {
        var keys = await SignInAsync();
        await Assert.ThrowsAsync<OloopCryptoException>(() => keys.PrepareSetupAsync("bad\ud800passphrase"));
    }
}
