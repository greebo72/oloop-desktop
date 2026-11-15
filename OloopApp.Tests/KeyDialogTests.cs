using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using OloopApp.Core.Crypto;
using OloopApp.Core.Server;
using OloopApp.ViewModels;
using OloopApp.Views;
using Xunit;

namespace OloopApp.Tests;

public class KeyDialogTests
{
    private const string Passphrase = "Grüne Wiese 42!";

    private static HeadlessUnitTestSession Session { get; } =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(KeyDialogTests).Assembly);

    private readonly FakeOloopServer _server = new();

    private async Task<KeyService> SignInAsync()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        await api.LoginAsync("timo", "secret", "test device");
        return new KeyService(api);
    }

    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        foreach (var child in root.GetVisualChildren())
        {
            if (child is T typed)
                yield return typed;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
    }

    // ---------------- Setup ----------------

    [Fact]
    public async Task Setup_Wants_Twelve_Characters_Twice_And_Not_The_Login_Password()
    {
        var vm = new KeySetupViewModel(await SignInAsync(), loginPassword: "secret-login-pw");

        Assert.False(vm.CanContinue);
        Assert.Null(vm.PassphraseHint);

        vm.Passphrase = "too short";
        vm.Confirmation = "too short";
        Assert.False(vm.CanContinue);
        Assert.Contains("12", vm.PassphraseHint);

        vm.Passphrase = "secret-login-pw";
        vm.Confirmation = "secret-login-pw";
        Assert.False(vm.CanContinue);
        Assert.Contains("another", vm.PassphraseHint);

        vm.Passphrase = Passphrase;
        vm.Confirmation = Passphrase + "x";
        Assert.False(vm.CanContinue);
        Assert.Contains("not the same", vm.PassphraseHint);

        vm.Confirmation = Passphrase;
        Assert.True(vm.CanContinue);
        Assert.Null(vm.PassphraseHint);
    }

    [Fact]
    public async Task Twelve_Characters_Are_Counted_As_A_Person_Counts_Them()
    {
        var vm = new KeySetupViewModel(await SignInAsync());

        // 11 letters and a u with a combining diaeresis: 12 characters, 13 UTF-16 units.
        vm.Passphrase = "abcdefghijkü";
        vm.Confirmation = vm.Passphrase;
        Assert.True(vm.CanContinue);

        vm.Passphrase = "abcdefghijü";
        vm.Confirmation = vm.Passphrase;
        Assert.False(vm.CanContinue);
    }

    [Fact]
    public async Task Nothing_Is_Stored_Until_The_Code_Is_Confirmed_And_Then_The_Keys_Are_Set_Up()
    {
        var vm = new KeySetupViewModel(await SignInAsync());
        vm.Passphrase = Passphrase;
        vm.Confirmation = Passphrase;

        Assert.True(await vm.ContinueAsync());
        Assert.True(vm.IsRecoveryStep);
        Assert.Equal(13, vm.RecoveryCodeText.Split('-').Length);
        Assert.Null(_server.Bundle);

        Assert.False(vm.CanFinish);
        Assert.False(await vm.FinishAsync());
        Assert.Null(_server.Bundle);

        vm.CodeIsKept = true;
        Assert.True(await vm.FinishAsync());
        Assert.NotNull(_server.Bundle);
        Assert.NotNull(vm.Result);
        Assert.Equal(_server.UserId, vm.Result!.UserId);
    }

    [Fact]
    public async Task Going_Back_Drops_The_Code_And_The_Next_One_Is_New()
    {
        var vm = new KeySetupViewModel(await SignInAsync());
        vm.Passphrase = Passphrase;
        vm.Confirmation = Passphrase;
        await vm.ContinueAsync();
        var first = vm.RecoveryCodeText;
        vm.CodeIsKept = true;

        vm.GoBack();
        Assert.True(vm.IsPassphraseStep);
        Assert.False(vm.CodeIsKept);
        Assert.Equal("", vm.RecoveryCodeText);

        await vm.ContinueAsync();
        Assert.NotEqual(first, vm.RecoveryCodeText);
        Assert.False(vm.CodeIsKept);
    }

    [Fact]
    public async Task A_Device_That_Comes_Second_Is_Told_And_Stores_Nothing()
    {
        var late = new KeySetupViewModel(await SignInAsync());
        late.Passphrase = "second passphrase 2";
        late.Confirmation = late.Passphrase;
        await late.ContinueAsync();
        late.CodeIsKept = true;

        var early = new KeySetupViewModel(await SignInAsync());
        early.Passphrase = Passphrase;
        early.Confirmation = Passphrase;
        await early.ContinueAsync();
        early.CodeIsKept = true;
        Assert.True(await early.FinishAsync());

        Assert.False(await late.FinishAsync());
        Assert.Null(late.Result);
        Assert.Contains("Another device", late.Error);
    }

    // ---------------- Unlock ----------------

    private async Task<(KeyService Keys, KeyBundle Bundle, string Code)> SetUpAsync()
    {
        var keys = await SignInAsync();
        var pending = await keys.PrepareSetupAsync(Passphrase);
        await keys.CommitSetupAsync(pending);
        return (keys, (await keys.FetchBundleAsync())!, pending.RecoveryCodeText);
    }

    [Fact]
    public async Task Unlock_Opens_With_The_Passphrase_And_Says_Nothing_Useful_To_A_Guesser()
    {
        var (keys, bundle, _) = await SetUpAsync();
        var vm = new KeyUnlockViewModel(keys, bundle);

        Assert.False(vm.CanUnlock);
        vm.Input = "wrong guess";
        Assert.False(await vm.UnlockAsync());
        Assert.Contains("passphrase", vm.Error);
        Assert.Null(vm.Result);

        vm.Input = Passphrase;
        Assert.True(await vm.UnlockAsync());
        Assert.Null(vm.Error);
        Assert.Equal(_server.UserId, vm.Result!.UserId);
    }

    [Fact]
    public async Task Unlock_Opens_With_The_Recovery_Code_After_The_Switch()
    {
        var (keys, bundle, code) = await SetUpAsync();
        var vm = new KeyUnlockViewModel(keys, bundle) { Input = "typed before" };

        vm.SwitchKind();
        Assert.True(vm.UsesRecoveryCode);
        Assert.Equal("", vm.Input); // a passphrase must not stay in a field that now shows a code

        vm.Input = "not a code";
        Assert.False(await vm.UnlockAsync());
        Assert.Contains("recovery code", vm.Error);

        vm.Input = code;
        Assert.True(await vm.UnlockAsync());
        Assert.NotNull(vm.Result);
    }

    // ---------------- The windows ----------------

    [Fact]
    public async Task The_Setup_Dialog_Shows_The_Passphrase_Step_First_And_Then_The_Code()
    {
        var keys = await SignInAsync();
        var vm = new KeySetupViewModel(keys);
        vm.Passphrase = Passphrase;
        vm.Confirmation = Passphrase;

        await Session.Dispatch(() =>
        {
            var dialog = new KeySetupDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();

            var shown = Descendants<TextBlock>(dialog).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Choose a passphrase", shown);
            Assert.DoesNotContain("Keep your recovery code", shown);
            Assert.All(Descendants<TextBox>(dialog).Where(b => b.IsEffectivelyVisible), b => Assert.Equal('●', b.PasswordChar));
            dialog.Close();
        }, CancellationToken.None);

        // The second step: made before a window is bound to it, as the work runs off the UI thread in this test.
        var second = new KeySetupViewModel(keys);
        second.Passphrase = Passphrase;
        second.Confirmation = Passphrase;
        await second.ContinueAsync();
        vm = second;

        await Session.Dispatch(() =>
        {
            var dialog = new KeySetupDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();

            var shown = Descendants<TextBlock>(dialog).Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
            Assert.Contains("Keep your recovery code", shown);
            Assert.Contains(vm.RecoveryCodeText, shown);
            Assert.DoesNotContain("Choose a passphrase", shown);

            var finish = Descendants<Button>(dialog).Single(b => b.Content as string == "Finish");
            Assert.False(finish.IsEnabled);
            vm.CodeIsKept = true;
            Assert.True(finish.IsEnabled);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Unlock_Dialog_Opens_With_A_Hidden_Passphrase_Field()
    {
        var (keys, bundle, _) = await SetUpAsync();

        await Session.Dispatch(() =>
        {
            var dialog = new KeyUnlockDialog(new KeyUnlockViewModel(keys, bundle));
            dialog.Show();
            dialog.UpdateLayout();

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("Unlock your keys", texts);
            Assert.Contains("Passphrase", texts);
            Assert.Equal('●', Descendants<TextBox>(dialog).Single().PasswordChar);
            dialog.Close();
        }, CancellationToken.None);
    }

    [Fact]
    public async Task The_Login_Dialog_Shows_Server_And_User_With_A_Hidden_Password_Field()
    {
        var api = new ServerApi(_server.CreateClient(), FakeOloopServer.BaseUrl);
        var vm = new LoginViewModel(api, "https://oloop.example", "timo", "Test Mac");

        await Session.Dispatch(() =>
        {
            var dialog = new LoginDialog(vm);
            dialog.Show();
            dialog.UpdateLayout();

            var texts = Descendants<TextBlock>(dialog).Select(t => t.Text).ToList();
            Assert.Contains("Sign in to the server", texts);
            Assert.Contains("https://oloop.example", texts);
            Assert.Contains("timo", texts);

            var boxes = Descendants<TextBox>(dialog).ToList();
            Assert.Equal('●', boxes.Single(b => b.Name == "PasswordBox").PasswordChar);
            Assert.Contains(boxes, b => b.Text == "Test Mac");

            var signIn = Descendants<Button>(dialog).Single(b => b.Content as string == "Sign in");
            Assert.False(signIn.IsEnabled);
            vm.Password = "secret";
            Assert.True(signIn.IsEnabled);
            dialog.Close();
        }, CancellationToken.None);
    }
}
