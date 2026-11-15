using OloopApp.Core.Crypto;
using OloopApp.Core.Server;
using Xunit;

namespace OloopApp.Tests;

/// <summary>
/// A fact against a real oloop server. It runs only when <c>OLOOP_IT_URL</c>, <c>OLOOP_IT_USER</c>
/// and <c>OLOOP_IT_PASSWORD</c> are set in the environment (the password is never written down
/// anywhere); otherwise it is skipped.
/// </summary>
public sealed class RealServerFactAttribute : FactAttribute
{
    public RealServerFactAttribute()
    {
        if (ServerIntegrationTests.Url is null || ServerIntegrationTests.User is null || ServerIntegrationTests.Password is null)
            Skip = "set OLOOP_IT_URL, OLOOP_IT_USER and OLOOP_IT_PASSWORD to test against a real server";
    }
}

/// <summary>
/// The client against a real server. These tests only <b>read</b>: they sign in with a device of
/// their own and sign it out again. A test that writes (the setup of the keys of an account) is
/// <see cref="Setup_Of_The_Keys_On_An_Account_Without_A_Bundle"/> and needs
/// <c>OLOOP_IT_ALLOW_SETUP=1</c> besides, because a bundle is made once per account.
/// </summary>
public class ServerIntegrationTests
{
    internal static string? Url => Environment.GetEnvironmentVariable("OLOOP_IT_URL");
    internal static string? User => Environment.GetEnvironmentVariable("OLOOP_IT_USER");
    internal static string? Password => Environment.GetEnvironmentVariable("OLOOP_IT_PASSWORD");

    private static string DeviceName => "oloop-integration-test-" + Guid.NewGuid().ToString("N")[..8];

    private static ServerApi NewApi() => new(new HttpClient(), ServerEndpoint_Normalized());

    private static string ServerEndpoint_Normalized()
        => OloopApp.Core.Io.ServerEndpoint.Normalize(Url, out _) ?? throw new InvalidOperationException("OLOOP_IT_URL is not a server address");

    [RealServerFact]
    public async Task Login_Gives_A_User_Id_And_Logout_Revokes_The_Device()
    {
        var api = NewApi();
        await api.LoginAsync(User!, Password!, DeviceName);
        try
        {
            Assert.True(api.SignedIn);
            Assert.NotEqual(Guid.Empty, api.UserId);
            Assert.False(string.IsNullOrEmpty(api.RefreshToken));
        }
        finally
        {
            await api.LogoutAsync();
        }

        Assert.False(api.SignedIn);
    }

    [RealServerFact]
    public async Task A_Wrong_Password_Is_Refused_With_401()
    {
        var api = NewApi();
        var error = await Assert.ThrowsAsync<ServerApiException>(
            () => api.LoginAsync(User!, Password! + "-wrong", DeviceName));
        Assert.Equal(401, error.Status);
    }

    [RealServerFact]
    public async Task A_Device_Resumes_With_Its_Refresh_Token_And_The_Token_Rotates()
    {
        var first = NewApi();
        await first.LoginAsync(User!, Password!, DeviceName);
        var userId = first.UserId;
        var token = first.RefreshToken!;

        var second = NewApi();
        try
        {
            await second.ResumeAsync(token);
            Assert.Equal(userId, second.UserId);
            Assert.NotEqual(token, second.RefreshToken); // rotated
            Assert.True(second.SignedIn);
        }
        finally
        {
            // The resumed session is the same device (its token rotated): this logout ends it.
            await second.LogoutAsync();
        }
    }

    [RealServerFact]
    public async Task The_Head_The_Feed_And_The_Key_Bundle_Answer_As_The_Spec_Says()
    {
        var api = NewApi();
        await api.LoginAsync(User!, Password!, DeviceName);
        try
        {
            var head = (await api.SendAsync(HttpMethod.Get, "v1/head")).Json();
            Assert.False(string.IsNullOrEmpty(head.GetProperty("cursor").GetString()));

            var feed = (await api.SendAsync(HttpMethod.Get, "v1/documents?limit=5")).Json();
            Assert.Equal(System.Text.Json.JsonValueKind.Array, feed.GetProperty("items").ValueKind);

            // 404 until the keys are set up, otherwise a bundle this client can read.
            var keys = new KeyService(api);
            var bundle = await keys.FetchBundleAsync();
            if (bundle is not null)
            {
                Assert.Equal(KeyBundle.PublicKeyBytes, bundle.PublicKey.Length);
                Assert.True(bundle.MemoryBytes >= KeyDerivation.MinMemoryBytes);
            }
        }
        finally
        {
            await api.LogoutAsync();
        }
    }

    [RealServerFact]
    public async Task Setup_Of_The_Keys_On_An_Account_Without_A_Bundle()
    {
        if (Environment.GetEnvironmentVariable("OLOOP_IT_ALLOW_SETUP") != "1")
            return; // writes to the account: only on purpose

        var api = NewApi();
        await api.LoginAsync(User!, Password!, DeviceName);
        try
        {
            var keys = new KeyService(api);
            Assert.Null(await keys.FetchBundleAsync()); // never over an existing bundle

            var pending = await keys.PrepareSetupAsync("integration test passphrase");
            var made = await keys.CommitSetupAsync(pending);

            var bundle = (await keys.FetchBundleAsync())!;
            Assert.Equal(made.SecretKey, (await keys.UnlockWithPassphraseAsync(bundle, "integration test passphrase")).SecretKey);
            Assert.Equal(made.SecretKey, keys.UnlockWithRecoveryCode(bundle, pending.RecoveryCodeText).SecretKey);
        }
        finally
        {
            await api.LogoutAsync();
        }
    }
}
