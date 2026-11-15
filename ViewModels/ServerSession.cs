using System.Text;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Secrets;
using OloopApp.Core.Server;

namespace OloopApp.ViewModels;

/// <summary>
/// The way from "the backend is the server" to a store that can be used: sign in, set up or
/// unlock the keys, and keep what lets the next start skip these steps in the secret store of the
/// system (SERVER.md, "Authentication" and "Key hierarchy").
///
/// <list type="bullet">
/// <item>The refresh token is kept as soon as the server hands one out and again at every rotation.</item>
/// <item>The secret key is kept after an unlock, so the passphrase is asked once per device.</item>
/// <item><see cref="Lock"/> forgets the secret key only: the device stays signed in.</item>
/// <item><see cref="SignOutAsync"/> cuts the device off at the server and forgets everything.</item>
/// </list>
///
/// There is one store per session: <see cref="Store"/> is the instance to hand out, because it
/// holds the keys of the projects and the revisions it has seen. The methods are for the thread
/// of the window: the dialogs are shown from them.
/// </summary>
public sealed class ServerSession : IDisposable
{
    private readonly HttpClient _http;
    private readonly ISecretStore _secrets;
    private readonly Func<IDialogService> _dialogs;
    private readonly SemaphoreSlim _connecting = new(1, 1);

    private readonly Func<string, Guid, IRevisionLedger> _ledgers;

    private ServerApi? _api;
    private string? _endpoint;
    private string? _userName;

    /// <param name="dialogs">
    /// Asked for at every dialog: the app sets its dialog service only after the window exists,
    /// and a session is made before.
    /// </param>
    /// <param name="ledgers">
    /// Makes the ledger of the revisions seen for a server and a user. The default keeps a file next
    /// to the settings of the user.
    /// </param>
    public ServerSession(
        HttpClient http, ISecretStore secrets, Func<IDialogService> dialogs,
        Func<string, Guid, IRevisionLedger>? ledgers = null)
    {
        _http = http;
        _secrets = secrets;
        _dialogs = dialogs;
        _ledgers = ledgers ?? DefaultLedger;
    }

    private static IRevisionLedger DefaultLedger(string serverUrl, Guid userId)
        => FileRevisionLedger.For(Path.GetDirectoryName(OloopSettings.DefaultPath)!, serverUrl, userId);

    /// <summary>The store of the unlocked session, or null while the session is not open or is locked.</summary>
    public ServerOloopStore? Store { get; private set; }

    /// <summary>True while this device is signed in, whether or not the keys are open.</summary>
    public bool IsSignedIn => _api is { SignedIn: true };

    /// <summary>True when the secret key is kept on this device, so the next start does not ask for the passphrase.</summary>
    public bool KeyIsKept { get; private set; }

    /// <summary>
    /// Opens the session. Asks the user only for what is missing: the password when there is no
    /// token that still works, the passphrase when there is no kept key. Returns the store, or null
    /// when the user cancelled a dialog.
    /// </summary>
    /// <exception cref="IOException">The server could not be reached or refused something other than a login.</exception>
    public async Task<ServerOloopStore?> ConnectAsync(string serverUrl, string userName, CancellationToken ct = default)
    {
        // One at a time: a reload that the change source starts while a dialog is open must not
        // open a second dialog, it waits and finds the store.
        await _connecting.WaitAsync(ct);
        try
        {
            return await ConnectCoreAsync(serverUrl, userName, ct);
        }
        finally
        {
            _connecting.Release();
        }
    }

    private async Task<ServerOloopStore?> ConnectCoreAsync(string serverUrl, string userName, CancellationToken ct)
    {
        if (Store is not null && IsFor(serverUrl, userName))
            return Store;

        // Another server or user than the open one: the old session goes first.
        if (_api is not null && !IsFor(serverUrl, userName))
            ForgetSession();

        _endpoint = serverUrl;
        _userName = userName;
        _api ??= CreateApi(serverUrl);

        string? loginPassword = null;
        if (!_api.SignedIn && !await TryResumeAsync(ct))
        {
            var login = new LoginViewModel(_api, serverUrl, userName);
            if (!await _dialogs().ShowLoginAsync(login))
                return null;

            loginPassword = login.TakePassword();
        }

        var keys = new KeyService(_api);
        var bundle = await keys.FetchBundleAsync(ct);

        UnlockedUser? user;
        if (bundle is null)
        {
            user = await _dialogs().ShowKeySetupAsync(new KeySetupViewModel(keys, loginPassword));
        }
        else
        {
            user = TryKeptKey(keys, bundle)
                ?? await _dialogs().ShowKeyUnlockAsync(new KeyUnlockViewModel(keys, bundle));
        }

        if (user is null)
            return null;

        Remember(KeyName, user.SecretKey);
        KeyIsKept = _secrets.IsPersistent;
        Store = new ServerOloopStore(_api, user, _ledgers(serverUrl, user.UserId));
        return Store;
    }

    /// <summary>
    /// Forgets the revisions that this device has seen of the documents of the signed-in user. For a
    /// server that was restored from a backup. Works while the keys are locked, too.
    /// </summary>
    public void ForgetSeenRevisions()
    {
        if (_api is not { SignedIn: true } || _endpoint is null)
            return;

        if (Store is not null)
            Store.ForgetRevisions();
        else
            _ledgers(_endpoint, _api.UserId).Clear();
    }

    /// <summary>Forgets the secret key, here and on the device. The sign-in stays; the next <see cref="ConnectAsync"/> asks for the passphrase.</summary>
    public void Lock()
    {
        Store = null;
        KeyIsKept = false;
        if (_endpoint is not null)
            _secrets.Delete(KeyName);
    }

    /// <summary>
    /// Cuts this device off at the server and forgets the token and the key. A server that cannot be
    /// reached does not keep the device signed in: what is kept here is deleted all the same.
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        try
        {
            if (_api is { SignedIn: true })
                await _api.LogoutAsync(ct);
        }
        catch (IOException)
        {
            // The device is not revoked on the server then, but nothing on this device can sign in any more.
        }

        if (_endpoint is not null)
        {
            _secrets.Delete(KeyName);
            _secrets.Delete(RefreshName);
        }

        ForgetSession();
    }

    public void Dispose() => ForgetSession();

    // ---------------- Steps ----------------

    private ServerApi CreateApi(string serverUrl)
    {
        var api = new ServerApi(_http, serverUrl);

        // A refresh token is used once: the new one has to be kept before anything else is done.
        // A write that fails here costs a new sign-in at the next start, nothing more.
        api.RefreshTokenChanged += token => Remember(RefreshName, Encoding.UTF8.GetBytes(token));
        return api;
    }

    /// <summary>Signs in with the kept token. False when there is none or the server no longer accepts it.</summary>
    private async Task<bool> TryResumeAsync(CancellationToken ct)
    {
        byte[]? kept;
        try
        {
            kept = _secrets.Get(RefreshName);
        }
        catch (IOException)
        {
            return false;
        }

        if (kept is null)
            return false;

        try
        {
            await _api!.ResumeAsync(Encoding.UTF8.GetString(kept), ct);
            return true;
        }
        catch (ServerApiException ex) when (ex.Status == 401)
        {
            // Revoked, expired or used twice: it is of no use any more.
            _secrets.Delete(RefreshName);
            return false;
        }
    }

    private UnlockedUser? TryKeptKey(KeyService keys, KeyBundle bundle)
    {
        byte[]? kept;
        try
        {
            kept = _secrets.Get(KeyName);
        }
        catch (IOException)
        {
            return null;
        }

        if (kept is null)
            return null;

        try
        {
            return keys.UnlockWithKeptKey(bundle, kept);
        }
        catch (OloopCryptoException)
        {
            // Not the key of this bundle (the keys were set up anew): ask for the passphrase.
            _secrets.Delete(KeyName);
            return null;
        }
    }

    private void Remember(string name, byte[] value)
    {
        try
        {
            _secrets.Set(name, value);
        }
        catch (IOException)
        {
            // A keychain that does not take it: the next start asks again.
        }
    }

    private void ForgetSession()
    {
        Store = null;
        KeyIsKept = false;
        _api?.Dispose();
        _api = null;
        _endpoint = null;
        _userName = null;
    }

    private bool IsFor(string serverUrl, string userName)
        => _endpoint == serverUrl
           && string.Equals(_userName, userName, StringComparison.OrdinalIgnoreCase);

    // One item per server and user: two accounts on one computer do not meet. The server compares
    // user names without regard to case, so the name here does too.
    private string Scope => $"{_endpoint}|{_userName!.Trim().ToLowerInvariant()}";

    private string RefreshName => Scope + "|refresh";

    private string KeyName => Scope + "|key";
}
