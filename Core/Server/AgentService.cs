using System.Text.Json;
using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using Sodium;

namespace OloopApp.Core.Server;

/// <summary>One rotation of the key pair of an agent: the new public key and the statement that vouches for it.</summary>
public sealed record AgentKeyChainEntry(byte[] PublicKey, byte[] Statement);

/// <summary>An agent as the server lists it for its owner (AGENTS.md, "API").</summary>
/// <param name="PublicKey">The current public key the server shows for the agent. Not to be trusted before <see cref="AgentService.VerifyKeyChain"/>.</param>
/// <param name="Registration">The registration made when the agent was created.</param>
/// <param name="KeyChain">The rotations since, oldest first.</param>
public sealed record AgentInfo(
    Guid Id,
    string Name,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RenewUntil,
    DateTimeOffset? LastUsedAt,
    bool Revoked,
    byte[]? PublicKey = null,
    byte[]? Registration = null,
    IReadOnlyList<AgentKeyChainEntry>? KeyChain = null)
{
    /// <summary>"active", "expired" or "revoked", for the list.</summary>
    public string StatusAt(DateTimeOffset now)
        => Revoked ? "revoked" : ExpiresAt is { } end && end <= now ? "expired" : "active";
}

/// <summary>
/// What came of giving an existing agent more projects: the ones it has now, and for each of the
/// others the reason. A failure of one project does not undo the others (each hand-over is complete).
/// </summary>
public sealed record GrantResult(IReadOnlyList<Guid> Granted, IReadOnlyList<(Guid ProjectId, string Reason)> Failed);

/// <summary>What the user asks for when an agent is created.</summary>
public sealed record NewAgent(
    string Name,
    IReadOnlyList<Guid> ProjectIds,
    bool ReadOnly,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RenewUntil);

/// <summary>
/// Creating an agent failed. <see cref="RolledBack"/> tells whether the agent that was already made on
/// the server was revoked again; when it is false the agent exists and the user has to revoke it.
/// </summary>
public sealed class AgentCreateException(string message, bool rolledBack, Exception? inner = null)
    : IOException(message, inner)
{
    public bool RolledBack { get; } = rolledBack;
}

/// <summary>
/// The agents of the signed-in user: list, create, revoke, issue a new token (AGENTS.md). Creating
/// needs the secret key of the user, because the registration and the hand-over of the project keys
/// are made with it.
/// </summary>
public sealed class AgentService
{
    internal const int NameLimit = 100;
    private const int MaxYears = 10;
    private const int IdAttempts = 3;

    private readonly ServerApi _api;
    private readonly UnlockedUser _user;
    private readonly string _serverUrl;

    /// <param name="serverUrl">The normalized address, as it goes into the file of the agent.</param>
    public AgentService(ServerApi api, UnlockedUser user, string serverUrl)
    {
        _api = api;
        _user = user;
        _serverUrl = serverUrl.TrimEnd('/');
    }

    public async Task<IReadOnlyList<AgentInfo>> ListAsync(CancellationToken ct = default)
    {
        var answer = (await _api.SendAsync(HttpMethod.Get, "v1/agents", ct: ct).ConfigureAwait(false)).Json();
        return [.. answer.EnumerateArray().Select(Parse)];
    }

    /// <summary>
    /// Makes the agent and hands it the projects, and returns what goes into its file. Nothing is
    /// written to disk here: the caller writes the file and, when that fails, calls
    /// <see cref="RevokeAsync"/>. When a step after the registration fails, the agent is revoked again
    /// so that it never has fewer projects than the user asked for.
    /// </summary>
    /// <exception cref="AgentCreateException">Something failed after the agent was made on the server.</exception>
    public async Task<AgentCredentials> CreateAsync(NewAgent request, CancellationToken ct = default)
    {
        var name = Validate(request);

        // A name that is taken answers 409, as an id that is taken does; the list tells them apart.
        var known = await ListAsync(ct).ConfigureAwait(false);
        if (known.Any(a => !a.Revoked && string.Equals(a.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw new OloopFormatException($"an agent with this name exists already: {name}");

        var projects = await OpenProjectKeysAsync(request.ProjectIds, ct).ConfigureAwait(false);

        var agentKeys = PublicKeyBox.GenerateKeyPair();
        Guid agentId = Guid.Empty;
        JsonElement created = default;
        for (var attempt = 1; ; attempt++)
        {
            agentId = Guid.NewGuid();
            var registration = AgentCrypto.CreateRegistration(
                agentKeys.PublicKey, agentId, _user.SecretKey, _user.PublicKey);

            var body = new JsonObject
            {
                ["id"] = agentId.ToString("D"),
                ["name"] = name,
                ["publicKey"] = Base64Url.Encode(agentKeys.PublicKey),
                ["registration"] = Base64Url.Encode(registration),
                ["expiresAt"] = AgentCredentialsFile.Format(request.ExpiresAt),
                ["renewUntil"] = AgentCredentialsFile.Format(request.RenewUntil),
            };

            try
            {
                created = (await _api.SendJsonAsync(HttpMethod.Post, "v1/agents", body, ct: ct).ConfigureAwait(false)).Json();
                break;
            }
            catch (ServerApiException ex) when (ex.IsConflict && attempt < IdAttempts)
            {
                // The id is taken (the name was checked above): a new id and a new registration.
            }
        }

        var token = Text(created, "token");
        var expiresAt = DateTimeOffset.Parse(Text(created, "expiresAt"), System.Globalization.CultureInfo.InvariantCulture);

        try
        {
            var role = request.ReadOnly ? "read" : "write";
            foreach (var (projectId, keys) in projects)
                await GrantAsync(projectId, agentId, agentKeys.PublicKey, role, keys, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var revoked = await TryRevokeAsync(agentId).ConfigureAwait(false);
            throw new AgentCreateException(
                revoked
                    ? $"the agent could not be given its projects and was revoked again: {ex.Message}"
                    : $"the agent could not be given its projects and could not be revoked again: {ex.Message}",
                revoked, ex);
        }

        return new AgentCredentials(
            _serverUrl, agentId, name, token, agentKeys.PublicKey, agentKeys.PrivateKey,
            _user.UserId, _user.PublicKey, expiresAt);
    }

    /// <summary>Ends the token and the memberships of the agent. Can be repeated (AGENTS.md, "API").</summary>
    public Task RevokeAsync(Guid agentId, CancellationToken ct = default)
        => _api.SendAsync(HttpMethod.Post, $"v1/agents/{agentId:D}/revoke", ct: ct);

    /// <summary>
    /// A new token for the agent; the tokens that it has end at once. Answers <c>409</c> for a revoked agent.
    /// </summary>
    public async Task<(string Token, DateTimeOffset ExpiresAt)> NewTokenAsync(Guid agentId, CancellationToken ct = default)
    {
        var answer = (await _api.SendAsync(HttpMethod.Post, $"v1/agents/{agentId:D}/token", ct: ct).ConfigureAwait(false)).Json();
        return (Text(answer, "token"),
            DateTimeOffset.Parse(Text(answer, "expiresAt"), System.Globalization.CultureInfo.InvariantCulture));
    }

    // ---------------- More projects for an existing agent ----------------

    /// <summary>
    /// Checks that the public key the server shows for the agent is the one the user's own key vouches
    /// for (AGENTS.md, "Rotating the key pair"): the registration opens with the user's keys and names
    /// this agent, every statement opens with the key before it, names this agent and counts one more,
    /// and the chain ends at the key the server shows. Without this a server could put its own key in
    /// place of the agent's and so receive every project key that is handed over.
    /// </summary>
    /// <returns>The verified public key of the agent.</returns>
    /// <exception cref="OloopCryptoException">The chain does not check out.</exception>
    public byte[] VerifyKeyChain(AgentInfo agent)
    {
        if (agent.PublicKey is null || agent.Registration is null)
            throw new OloopCryptoException("the server did not send the keys of the agent");

        var (key, registeredId) = AgentCrypto.OpenRegistration(agent.Registration, _user.SecretKey, _user.PublicKey);
        if (registeredId != agent.Id)
            throw new OloopCryptoException("the registration names another agent");

        var number = 1;
        foreach (var entry in agent.KeyChain ?? [])
        {
            var (next, statedId, statedNumber) = AgentCrypto.OpenStatement(entry.Statement, _user.SecretKey, key);
            if (statedId != agent.Id)
                throw new OloopCryptoException("a statement of the key chain names another agent");
            if (statedNumber != number + 1)
                throw new OloopCryptoException("the key chain of the agent skips a key");
            if (!next.AsSpan().SequenceEqual(entry.PublicKey))
                throw new OloopCryptoException("a statement of the key chain vouches for another key than the one listed");

            key = next;
            number = statedNumber;
        }

        if (!key.AsSpan().SequenceEqual(agent.PublicKey))
            throw new OloopCryptoException("the key the server shows for the agent is not the end of its key chain");

        return key;
    }

    /// <summary>
    /// The role the agent has in each of the given projects, for those it is a member of. One request
    /// per project (<c>GET /v1/projects/{id}/members</c>, owner only).
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, string>> RolesOfAsync(
        Guid agentId, IEnumerable<Guid> projectIds, CancellationToken ct = default)
    {
        var roles = new Dictionary<Guid, string>();
        foreach (var projectId in projectIds.Distinct())
        {
            var members = (await _api.SendAsync(HttpMethod.Get, $"v1/projects/{projectId:D}/members", ct: ct)
                .ConfigureAwait(false)).Json();
            foreach (var member in members.EnumerateArray())
            {
                if (Guid.TryParse(Text(member, "principalId"), out var id) && id == agentId)
                    roles[projectId] = Text(member, "role");
            }
        }

        return roles;
    }

    /// <summary>
    /// Gives an existing agent more projects. The key chain is checked first and the keys of all
    /// projects are opened before the first hand-over, so nothing happens when one of them cannot be
    /// handed over. The keys are wrapped for the verified key of the agent. Each project is a request
    /// of its own and can be repeated; a failed one does not undo the ones before it.
    /// </summary>
    /// <exception cref="OloopCryptoException">The key chain of the agent does not check out; nothing was handed over.</exception>
    /// <exception cref="OloopFormatException">The agent is revoked, no project was named, or a project cannot be handed over.</exception>
    public async Task<GrantResult> GrantProjectsAsync(
        AgentInfo agent, IReadOnlyList<Guid> projectIds, bool readOnly, CancellationToken ct = default)
    {
        if (agent.Revoked)
            throw new OloopFormatException("the agent is revoked");
        if (projectIds.Count == 0)
            throw new OloopFormatException("choose at least one project");

        var agentKey = VerifyKeyChain(agent);
        var projects = await OpenProjectKeysAsync(projectIds, ct).ConfigureAwait(false);

        var role = readOnly ? "read" : "write";
        var granted = new List<Guid>();
        var failed = new List<(Guid, string)>();
        for (var i = 0; i < projects.Count; i++)
        {
            var (projectId, keys) = projects[i];
            try
            {
                await GrantAsync(projectId, agent.Id, agentKey, role, keys, ct).ConfigureAwait(false);
                granted.Add(projectId);
            }
            catch (ServerApiException ex) when (ex.Status == 404)
            {
                // The agent is gone (revoked meanwhile) or the project is not the user's: none of the
                // rest can work either.
                foreach (var (restId, _) in projects.Skip(i))
                    failed.Add((restId, "the agent was revoked or the project is gone"));
                break;
            }
            catch (IOException ex)
            {
                failed.Add((projectId, ex.Message));
            }
        }

        return new GrantResult(granted, failed);
    }

    // ---------------- Hand-over of the project keys ----------------

    private async Task<List<(Guid ProjectId, Dictionary<int, byte[]> Keys)>> OpenProjectKeysAsync(
        IReadOnlyList<Guid> projectIds, CancellationToken ct)
    {
        var answer = (await _api.SendAsync(HttpMethod.Get, "v1/projects", ct: ct).ConfigureAwait(false)).Json();
        var byId = answer.EnumerateArray().ToDictionary(p => Guid.Parse(Text(p, "id")));

        var result = new List<(Guid, Dictionary<int, byte[]>)>();
        foreach (var projectId in projectIds.Distinct())
        {
            if (!byId.TryGetValue(projectId, out var project))
                throw new OloopFormatException("a selected project does not exist on the server");
            if (Text(project, "role") != "owner")
                throw new OloopFormatException("only the owner of a project can give an agent access to it");

            // Only keys that the user handed to itself: for another sender there is no pin to check.
            var opened = new Dictionary<int, byte[]>();
            foreach (var entry in project.GetProperty("keys").EnumerateArray())
            {
                if (!Guid.TryParse(Text(entry, "grantedBy"), out var grantedBy) || grantedBy != _user.UserId)
                    continue;

                var keyVersion = entry.GetProperty("keyVersion").GetInt32();
                try
                {
                    opened[keyVersion] = ProjectKeyBox.Unwrap(
                        Base64Url.Decode(Text(entry, "wrapped")), _user.SecretKey, _user.PublicKey, projectId, keyVersion);
                }
                catch (OloopCryptoException)
                {
                    // Not handed over: the agent cannot read what this version encrypted either.
                }
            }

            var newest = project.GetProperty("keys").EnumerateArray()
                .Select(e => e.GetProperty("keyVersion").GetInt32()).DefaultIfEmpty(0).Max();
            if (!opened.ContainsKey(newest))
                throw new OloopFormatException("the current key of a selected project cannot be opened");

            result.Add((projectId, opened));
        }

        return result;
    }

    private Task GrantAsync(
        Guid projectId, Guid agentId, byte[] agentPublicKey, string role, Dictionary<int, byte[]> keys, CancellationToken ct)
    {
        var wrapped = new JsonArray();
        foreach (var (keyVersion, key) in keys.OrderBy(k => k.Key))
        {
            wrapped.Add(new JsonObject
            {
                ["keyVersion"] = keyVersion,
                ["wrapped"] = Base64Url.Encode(
                    ProjectKeyBox.Wrap(key, projectId, keyVersion, agentPublicKey, _user.SecretKey)),
            });
        }

        return _api.SendJsonAsync(
            HttpMethod.Put, $"v1/projects/{projectId:D}/members/{agentId:D}",
            new JsonObject { ["role"] = role, ["keys"] = wrapped }, ct: ct);
    }

    private async Task<bool> TryRevokeAsync(Guid agentId)
    {
        try
        {
            await RevokeAsync(agentId).ConfigureAwait(false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    // ---------------- Input and answers ----------------

    private static string Validate(NewAgent request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0)
            throw new OloopFormatException("the name of the agent must not be empty");
        if (name.EnumerateRunes().Count() > NameLimit)
            throw new OloopFormatException($"the name of the agent must be at most {NameLimit} characters");
        if (request.ProjectIds.Count == 0)
            throw new OloopFormatException("choose at least one project for the agent");

        var now = DateTimeOffset.UtcNow;
        var limit = now.AddYears(MaxYears);
        if (request.ExpiresAt <= now || request.ExpiresAt > limit)
            throw new OloopFormatException($"the token must be valid for a time in the future, at most {MaxYears} years");
        if (request.RenewUntil < request.ExpiresAt || request.RenewUntil > limit)
            throw new OloopFormatException($"the renewal period must not end before the token and at most {MaxYears} years from now");

        return name;
    }

    private static AgentInfo Parse(JsonElement e) => new(
        Guid.Parse(Text(e, "id")),
        Text(e, "name"),
        Moment(e, "createdAt"),
        Moment(e, "expiresAt"),
        Moment(e, "renewUntil"),
        Moment(e, "lastUsedAt"),
        e.TryGetProperty("revoked", out var revoked) && revoked.ValueKind == JsonValueKind.True,
        Bytes(e, "publicKey"),
        Bytes(e, "registration"),
        e.TryGetProperty("keyChain", out var chain) && chain.ValueKind == JsonValueKind.Array
            ? [.. chain.EnumerateArray().Select(c => new AgentKeyChainEntry(
                Bytes(c, "publicKey") ?? [], Bytes(c, "statement") ?? []))]
            : []);

    private static byte[]? Bytes(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return null;

        try
        {
            return Base64Url.Decode(value.GetString()!);
        }
        catch (OloopCryptoException)
        {
            return null;
        }
    }

    private static DateTimeOffset? Moment(JsonElement e, string name)
        => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal, out var moment)
                ? moment
                : null;

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new JsonException($"the answer of the server has no {name}");
}
