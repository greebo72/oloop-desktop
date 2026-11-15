using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;

namespace OloopApp.Core.Server;

/// <summary>A key bundle that has been made but not yet stored: the recovery code is to be shown first.</summary>
public sealed class PendingKeySetup
{
    internal PendingKeySetup(KeyBundle bundle, byte[] secretKey, byte[] recoveryCode, Guid userId)
    {
        Bundle = bundle;
        SecretKey = secretKey;
        RecoveryCode = recoveryCode;
        UserId = userId;
    }

    internal KeyBundle Bundle { get; }
    internal byte[] SecretKey { get; }
    internal Guid UserId { get; }
    public byte[] RecoveryCode { get; }

    /// <summary>The recovery code as the user is to see it.</summary>
    public string RecoveryCodeText => Crypto.RecoveryCode.Format(RecoveryCode);
}

/// <summary>
/// Setup and unlock of the key bundle of the signed-in user (SERVER.md, "Key bundle"). The
/// passphrase and the recovery code never leave this class except as the keys they make.
/// </summary>
public sealed class KeyService
{
    private readonly ServerApi _api;

    public KeyService(ServerApi api) => _api = api;

    /// <summary>The bundle of the user, or null when none was set up yet (a first device).</summary>
    public async Task<KeyBundle?> FetchBundleAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _api.SendAsync(HttpMethod.Get, "v1/keys", ct: ct).ConfigureAwait(false);
            return KeyBundle.FromJson(response.Json().GetProperty("bundle").GetRawText());
        }
        catch (ServerApiException ex) when (ex.IsNotFound)
        {
            return null;
        }
    }

    /// <summary>
    /// Makes the key pair and the two wrapped copies of the secret key. Nothing is sent: the
    /// user has to see the recovery code and confirm that it is kept before
    /// <see cref="CommitSetupAsync"/>. Takes a second or so (Argon2id, 64 MiB).
    /// </summary>
    public Task<PendingKeySetup> PrepareSetupAsync(string passphrase, CancellationToken ct = default)
    {
        var passphraseBytes = Passphrase.ToKeyBytes(passphrase);
        var userId = RequireUserId();
        return Task.Run(() =>
        {
            var recoveryCode = Crypto.RecoveryCode.Create();
            var (bundle, secretKey) = KeyBundle.Create(passphraseBytes, recoveryCode, userId);
            return new PendingKeySetup(bundle, secretKey, recoveryCode, userId);
        }, ct);
    }

    /// <summary>
    /// Stores the bundle (<c>If-None-Match: *</c>) and returns the unlocked user. When another
    /// device made a bundle in the meantime, that one is the user's and this one is dropped:
    /// throws a <see cref="ServerApiException"/> with a conflict.
    /// </summary>
    public async Task<UnlockedUser> CommitSetupAsync(PendingKeySetup pending, CancellationToken ct = default)
    {
        await _api.SendJsonAsync(
            HttpMethod.Put, "v1/keys", new JsonObject { ["bundle"] = JsonNode.Parse(pending.Bundle.ToJson()) },
            new Dictionary<string, string> { ["If-None-Match"] = "*" }, ct).ConfigureAwait(false);
        return new UnlockedUser(pending.UserId, pending.SecretKey, pending.Bundle.PublicKey);
    }

    /// <summary>Opens the bundle with the passphrase. Throws <see cref="OloopCryptoException"/> when it does not fit.</summary>
    public Task<UnlockedUser> UnlockWithPassphraseAsync(KeyBundle bundle, string passphrase, CancellationToken ct = default)
    {
        var passphraseBytes = Passphrase.ToKeyBytes(passphrase);
        var userId = RequireUserId();
        return Task.Run(() =>
        {
            var key = KeyDerivation.DerivePassphraseKey(passphraseBytes, bundle.Salt, bundle.MemoryBytes, bundle.Passes);
            return new UnlockedUser(userId, bundle.UnlockWithPassphraseKey(key, userId), bundle.PublicKey);
        }, ct);
    }

    /// <summary>Opens the bundle with the recovery code as typed. Throws <see cref="OloopCryptoException"/> for a wrong one.</summary>
    public UnlockedUser UnlockWithRecoveryCode(KeyBundle bundle, string typed)
    {
        var code = Crypto.RecoveryCode.TryParse(typed)
            ?? throw new OloopCryptoException("that is not a recovery code");
        var userId = RequireUserId();
        return new UnlockedUser(userId, bundle.UnlockWithRecoveryCode(code, userId), bundle.PublicKey);
    }

    private Guid RequireUserId()
        => _api.UserId != Guid.Empty ? _api.UserId : throw new InvalidOperationException("not signed in");
}
