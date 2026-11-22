using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using OloopApp.Core.Crypto;
using Sodium;

namespace OloopApp.Tests;

/// <summary>
/// A small oloop server in memory, for the tests of the client. It keeps what the
/// real server keeps: documents with their revision, the keys of the projects, the
/// preconditions of SERVER.md ("Concurrency"), short-lived access tokens and
/// refresh tokens that rotate. It cannot read a payload, like the real one, so
/// <see cref="Stored"/> is what a passive server would see.
/// </summary>
internal sealed class FakeOloopServer : HttpMessageHandler
{
    public const string BaseUrl = "http://localhost:8000";

    private readonly object _gate = new();
    private readonly Dictionary<Guid, StoredDocument> _documents = [];
    private readonly Dictionary<Guid, JsonObject> _projectKeys = [];
    private readonly Dictionary<Guid, (Guid TaskId, byte[] Envelope)> _blobs = [];
    private readonly HashSet<string> _validAccess = [];
    private readonly HashSet<string> _validRefresh = [];
    private int _counter;

    public Guid UserId { get; } = Guid.NewGuid();
    public KeyPair UserKeys { get; } = PublicKeyBox.GenerateKeyPair();

    /// <summary>Set by a test to make every request fail like a server that is down.</summary>
    public bool Down { get; set; }

    /// <summary>The revisions to show in place of the stored one, by document: a server that rolls back.</summary>
    public Dictionary<Guid, StoredDocument> Shown { get; } = [];

    public int Requests { get; private set; }
    public int Refreshes { get; private set; }

    public sealed record StoredDocument(
        Guid Id, string Kind, Guid? ProjectId, int KeyVersion, int Rev, byte[] Envelope, long Seq, Guid? BlobRef = null);

    public IReadOnlyList<StoredDocument> Stored
    {
        get
        {
            lock (_gate)
                return _documents.Values.ToList();
        }
    }

    /// <summary>Makes every access token invalid, as if 15 minutes had passed.</summary>
    public void ExpireAccessTokens()
    {
        lock (_gate)
            _validAccess.Clear();
    }

    /// <summary>Makes every refresh token invalid, as if the device had been revoked.</summary>
    public void RevokeRefreshTokens()
    {
        lock (_gate)
            _validRefresh.Clear();
    }

    /// <summary>Replaces the envelope of a document without changing its revision.</summary>
    public void Tamper(Guid id)
    {
        lock (_gate)
        {
            var doc = _documents[id];
            var bytes = (byte[])doc.Envelope.Clone();
            bytes[^1] ^= 0xFF;
            _documents[id] = doc with { Envelope = bytes };
        }
    }

    /// <summary>Puts a blob on the server as an upload for that task would, for a test that needs an image.</summary>
    public void AddBlob(Guid blobId, Guid taskId, byte[] envelope)
    {
        lock (_gate)
            _blobs[blobId] = (taskId, envelope);
    }

    /// <summary>Takes a blob away, as when it was cleaned up.</summary>
    public void RemoveBlob(Guid blobId)
    {
        lock (_gate)
            _blobs.Remove(blobId);
    }

    /// <summary>The number of times a blob was fetched.</summary>
    public int BlobFetches { get; private set; }

    /// <summary>
    /// The project key of a project, opened with the keys of a user: what a test needs to make an
    /// image. Without keys it is the fixed pair of this server; a test whose keys came from a real
    /// setup gives them.
    /// </summary>
    public byte[] ProjectKey(Guid projectId, byte[]? secretKey = null, byte[]? publicKey = null)
    {
        lock (_gate)
        {
            var entry = _projectKeys[projectId];
            return ProjectKeyBox.Unwrap(
                Base64Url.Decode(entry["wrapped"]!.GetValue<string>()),
                secretKey ?? UserKeys.PrivateKey, publicKey ?? UserKeys.PublicKey,
                projectId, entry["keyVersion"]!.GetValue<int>());
        }
    }

    /// <summary>Changes who granted the key of a project, as a server that invents a sender would.</summary>
    public void SetGrantedBy(Guid projectId, Guid grantedBy)
    {
        lock (_gate)
            _projectKeys[projectId]["grantedBy"] = grantedBy.ToString("D");
    }

    public HttpClient CreateClient() => new(this) { BaseAddress = new Uri(BaseUrl) };

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(Handle(request));

    private HttpResponseMessage Handle(HttpRequestMessage request)
    {
        lock (_gate)
        {
            Requests++;
            if (Down)
                throw new HttpRequestException("connection refused");

            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content?.ReadAsByteArrayAsync().GetAwaiter().GetResult() ?? [];

            if (path == "/v1/auth/login")
                return Login(body);
            if (path == "/v1/auth/refresh")
                return Refresh(body);

            var token = request.Headers.Authorization?.Parameter;
            if (token is null || !_validAccess.Contains(token))
                return Error(HttpStatusCode.Unauthorized, "unauthorized", "missing, expired or revoked token");

            if (path == "/v1/auth/logout")
            {
                _validAccess.Remove(token);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (path == "/v1/keys")
                return Keys(request, body);
            if (path.StartsWith("/v1/blobs/", StringComparison.Ordinal) && request.Method == HttpMethod.Get
                && Guid.TryParse(path["/v1/blobs/".Length..], out var blobId))
            {
                BlobFetches++;
                return _blobs.TryGetValue(blobId, out var blob)
                    ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(blob.Envelope) }
                    : Error(HttpStatusCode.NotFound, "not_found", "no such blob");
            }

            if (path == "/v1/head" && request.Method == HttpMethod.Get)
                return Json(new JsonObject { ["cursor"] = _counter.ToString() });
            if (path == "/v1/projects" && request.Method == HttpMethod.Get)
                return Projects();
            if (path == "/v1/documents" && request.Method == HttpMethod.Get)
                return Feed(request.RequestUri.Query);

            if (path.StartsWith("/v1/documents/", StringComparison.Ordinal)
                && Guid.TryParse(path["/v1/documents/".Length..], out var id))
            {
                if (request.Method == HttpMethod.Get)
                    return _documents.TryGetValue(id, out var doc)
                        ? Json(DocumentJson(Shown.GetValueOrDefault(id) ?? doc))
                        : Error(HttpStatusCode.NotFound, "not_found", "no such document");
                if (request.Method == HttpMethod.Put)
                {
                    DocumentPuts++;
                    if (DocumentPuts == FailDocumentPutNumber)
                        return Error(HttpStatusCode.InternalServerError, "internal", "the server failed");
                    return Put(id, request, body);
                }
            }

            if (path == "/v1/agents")
                return request.Method == HttpMethod.Post ? CreateAgent(body) : ListAgents();
            if (path.StartsWith("/v1/agents/", StringComparison.Ordinal))
            {
                var parts = path["/v1/agents/".Length..].Split('/');
                if (parts.Length == 2 && Guid.TryParse(parts[0], out var agentId) && request.Method == HttpMethod.Post)
                    return parts[1] == "revoke" ? RevokeAgent(agentId) : parts[1] == "token" ? NewAgentToken(agentId) : Error(HttpStatusCode.NotFound, "not_found", "no such route");
            }

            if (path.StartsWith("/v1/projects/", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
            {
                var parts = path["/v1/projects/".Length..].Split('/');
                if (parts.Length == 3 && parts[1] == "members"
                    && Guid.TryParse(parts[0], out var memberProject) && Guid.TryParse(parts[2], out var memberId))
                    return PutMember(memberProject, memberId, body);
            }

            return Error(HttpStatusCode.NotFound, "not_found", "no such route");
        }
    }

    /// <summary>The key bundle as it is stored; the server checks the form only (SERVER.md, "Key bundle").</summary>
    public JsonNode? Bundle { get; private set; }

    private int _keyRev;

    private HttpResponseMessage Keys(HttpRequestMessage request, byte[] body)
    {
        if (request.Method == HttpMethod.Get)
            return Bundle is null
                ? Error(HttpStatusCode.NotFound, "not_found", "no key bundle yet")
                : Json(new JsonObject { ["keyRev"] = _keyRev, ["bundle"] = Bundle.DeepClone() });

        var first = request.Headers.IfNoneMatch.Any(t => t.Tag == "*");
        if (first && Bundle is not null)
            return Error(HttpStatusCode.Conflict, "conflict", "a bundle exists");
        if (!first)
            return Error(HttpStatusCode.BadRequest, "bad_request", "only the first setup is supported here");

        Bundle = JsonNode.Parse(body)!["bundle"]!.DeepClone();
        _keyRev = 1;
        return Json(new JsonObject { ["keyRev"] = _keyRev });
    }

    private HttpResponseMessage Login(byte[] body)
    {
        var json = JsonNode.Parse(body)!;
        if (json["username"]?.GetValue<string>() != "timo" || json["password"]?.GetValue<string>() != "secret")
            return Error(HttpStatusCode.Unauthorized, "unauthorized", "wrong user name or password");

        return Tokens();
    }

    private HttpResponseMessage Refresh(byte[] body)
    {
        var token = JsonNode.Parse(body)!["refreshToken"]?.GetValue<string>();
        // A refresh token that is used twice is the end of the device (SERVER.md, "Authentication").
        if (token is null || !_validRefresh.Remove(token))
        {
            _validRefresh.Clear();
            _validAccess.Clear();
            return Error(HttpStatusCode.Unauthorized, "unauthorized", "the refresh token is not valid");
        }

        Refreshes++;
        return Tokens();
    }

    private HttpResponseMessage Tokens()
    {
        var access = "access-" + Guid.NewGuid().ToString("N");
        var refresh = "refresh-" + Guid.NewGuid().ToString("N");
        _validAccess.Add(access);
        _validRefresh.Add(refresh);
        return Json(new JsonObject
        {
            ["accessToken"] = access,
            ["expiresIn"] = 900,
            ["refreshToken"] = refresh,
            ["userId"] = UserId.ToString("D"),
        });
    }

    // ---------------- Agents (AGENTS.md, "API") ----------------

    public sealed class AgentRecord
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = "";
        public byte[] PublicKey { get; init; } = [];
        public byte[] Registration { get; init; } = [];
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset RenewUntil { get; init; }
        public bool Revoked { get; set; }
        public string Token { get; set; } = "";

        /// <summary>The projects of the agent: role and the keys that were handed over, by project.</summary>
        public Dictionary<Guid, (string Role, JsonArray Keys)> Members { get; } = [];
    }

    private readonly List<AgentRecord> _agents = [];
    private int _memberPuts;

    public IReadOnlyList<AgentRecord> Agents
    {
        get
        {
            lock (_gate)
                return _agents.ToList();
        }
    }

    /// <summary>The first n requests to create an agent answer 409, as for an id that is taken.</summary>
    public int RejectAgentIds { get; set; }

    /// <summary>The n-th request to hand a project to an agent (counting from 1) fails with a 500.</summary>
    public int FailMemberPutNumber { get; set; }

    /// <summary>How many requests to write a document (<c>PUT /v1/documents/{id}</c>) came in so far.</summary>
    public int DocumentPuts { get; private set; }

    /// <summary>The write of a document with this number (counting from 1, see <see cref="DocumentPuts"/>) fails with a 500.</summary>
    public int FailDocumentPutNumber { get; set; }

    private HttpResponseMessage CreateAgent(byte[] body)
    {
        var request = JsonNode.Parse(body)!.AsObject();
        var allowed = new HashSet<string> { "id", "name", "publicKey", "registration", "expiresAt", "renewUntil" };
        if (request.Any(p => !allowed.Contains(p.Key)))
            return Error(HttpStatusCode.BadRequest, "bad_request", "unknown field");
        if (!Guid.TryParse(request["id"]?.GetValue<string>(), out var id))
            return Error(HttpStatusCode.BadRequest, "bad_request", "id must be a UUID");
        var name = request["name"]!.GetValue<string>();
        var registration = Base64Url.Decode(request["registration"]!.GetValue<string>());
        if (registration.Length != 88)
            return Error(HttpStatusCode.BadRequest, "bad_request", "registration must be 88 bytes");
        if (RejectAgentIds > 0)
        {
            RejectAgentIds--;
            return Error(HttpStatusCode.Conflict, "conflict", "an agent with this id exists already");
        }

        if (_agents.Any(a => a.Id == id))
            return Error(HttpStatusCode.Conflict, "conflict", "an agent with this id exists already");
        if (_agents.Any(a => !a.Revoked && string.Equals(a.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Error(HttpStatusCode.Conflict, "conflict", "an agent of this name exists already");

        var agent = new AgentRecord
        {
            Id = id,
            Name = name.Trim(),
            PublicKey = Base64Url.Decode(request["publicKey"]!.GetValue<string>()),
            Registration = registration,
            ExpiresAt = DateTimeOffset.Parse(request["expiresAt"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
            RenewUntil = DateTimeOffset.Parse(request["renewUntil"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture),
            Token = "agent-token-" + Guid.NewGuid().ToString("N"),
        };
        _agents.Add(agent);
        return Json(new JsonObject
        {
            ["agentId"] = id.ToString("D"),
            ["token"] = agent.Token,
            ["expiresAt"] = agent.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    private HttpResponseMessage ListAgents()
    {
        var array = new JsonArray();
        foreach (var a in _agents)
        {
            array.Add(new JsonObject
            {
                ["id"] = a.Id.ToString("D"),
                ["name"] = a.Name,
                ["publicKey"] = Base64Url.Encode(a.PublicKey),
                ["registration"] = Base64Url.Encode(a.Registration),
                ["keyChain"] = new JsonArray(),
                ["createdAt"] = "2026-11-01T10:00:00Z",
                ["expiresAt"] = a.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                ["renewUntil"] = a.RenewUntil.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
                ["lastUsedAt"] = null,
                ["revoked"] = a.Revoked,
            });
        }

        return Json(array);
    }

    private HttpResponseMessage RevokeAgent(Guid id)
    {
        var agent = _agents.FirstOrDefault(a => a.Id == id);
        if (agent is null)
            return Error(HttpStatusCode.NotFound, "not_found", "not found");
        agent.Revoked = true;
        agent.Members.Clear();
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private HttpResponseMessage NewAgentToken(Guid id)
    {
        var agent = _agents.FirstOrDefault(a => a.Id == id);
        if (agent is null)
            return Error(HttpStatusCode.NotFound, "not_found", "not found");
        if (agent.Revoked)
            return Error(HttpStatusCode.Conflict, "conflict", "the agent is revoked");
        agent.Token = "agent-token-" + Guid.NewGuid().ToString("N");
        agent.ExpiresAt = agent.ExpiresAt.AddDays(90);
        return Json(new JsonObject
        {
            ["token"] = agent.Token,
            ["expiresAt"] = agent.ExpiresAt.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        });
    }

    private HttpResponseMessage PutMember(Guid projectId, Guid agentId, byte[] body)
    {
        _memberPuts++;
        if (_memberPuts == FailMemberPutNumber)
            return Error(HttpStatusCode.InternalServerError, "internal", "the server failed");

        var agent = _agents.FirstOrDefault(a => a.Id == agentId && !a.Revoked);
        if (agent is null || !_projectKeys.ContainsKey(projectId))
            return Error(HttpStatusCode.NotFound, "not_found", "not found");

        var request = JsonNode.Parse(body)!.AsObject();
        var role = request["role"]!.GetValue<string>();
        var keys = request["keys"]!.AsArray();
        if (role is not ("write" or "read") || keys.Any(k => Base64Url.Decode(k!["wrapped"]!.GetValue<string>()).Length != 92))
            return Error(HttpStatusCode.BadRequest, "bad_request", "role or keys are wrong");

        agent.Members[projectId] = (role, JsonNode.Parse(keys.ToJsonString())!.AsArray());
        return new HttpResponseMessage(HttpStatusCode.NoContent);
    }

    private HttpResponseMessage Projects()
    {
        var array = new JsonArray();
        foreach (var doc in _documents.Values.Where(d => d.Kind == "project"))
        {
            array.Add(new JsonObject
            {
                ["id"] = doc.Id.ToString("D"),
                ["role"] = "owner",
                ["keys"] = new JsonArray(_projectKeys[doc.Id].DeepClone()),
            });
        }

        return Json(array);
    }

    private HttpResponseMessage Feed(string query)
    {
        var parameters = System.Web.HttpUtility.ParseQueryString(query);
        var after = long.TryParse(parameters["cursor"], out var parsed) ? parsed : 0;
        var limit = int.TryParse(parameters["limit"], out var l) ? l : 100;

        var items = _documents.Values.Where(d => d.Seq > after).OrderBy(d => d.Seq).ToList();
        var page = items.Take(limit).ToList();

        var array = new JsonArray();
        foreach (var doc in page)
            array.Add(DocumentJson(Shown.GetValueOrDefault(doc.Id) ?? doc));

        return Json(new JsonObject
        {
            ["items"] = array,
            ["nextCursor"] = (page.Count == 0 ? after : page[^1].Seq).ToString(),
            ["hasMore"] = items.Count > page.Count,
        });
    }

    private HttpResponseMessage Put(Guid id, HttpRequestMessage request, byte[] body)
    {
        var json = JsonNode.Parse(body)!;
        var kind = json["kind"]!.GetValue<string>();
        var rev = json["rev"]!.GetValue<int>();
        var keyVersion = json["keyVersion"]!.GetValue<int>();
        var envelope = Base64Url.Decode(json["data"]!.GetValue<string>());

        var ifNoneMatch = request.Headers.IfNoneMatch.Any(t => t.Tag == "*");
        var ifMatch = request.Headers.IfMatch.FirstOrDefault()?.Tag.Trim('"');
        if (ifNoneMatch == (ifMatch is not null))
            return Error(HttpStatusCode.BadRequest, "bad_request", "exactly one of If-None-Match and If-Match");

        _documents.TryGetValue(id, out var existing);
        if (ifNoneMatch ? existing is not null : existing is null || existing.Rev.ToString() != ifMatch)
            return Error(HttpStatusCode.Conflict, "conflict", "the revision does not match");
        if (rev != (existing?.Rev ?? 0) + 1)
            return Error(HttpStatusCode.BadRequest, "bad_request", "rev must be the next revision");

        Guid? projectId = kind == "project" ? null : Guid.Parse(json["projectId"]!.GetValue<string>());
        if (kind == "project" && existing is null)
        {
            var key = json["key"]!.AsObject();
            _projectKeys[id] = new JsonObject
            {
                ["keyVersion"] = key["keyVersion"]!.GetValue<int>(),
                ["grantedBy"] = UserId.ToString("D"),
                ["wrapped"] = key["wrapped"]!.GetValue<string>(),
            };
        }

        // As the real server: a reference is only for a task, and only to a blob uploaded for it.
        Guid? blobRef = null;
        if (json["blobRef"] is { } reference)
        {
            blobRef = Guid.Parse(reference.GetValue<string>());
            if (kind != "task" || !_blobs.TryGetValue(blobRef.Value, out var blob) || blob.TaskId != id)
                return Error(HttpStatusCode.BadRequest, "bad_request", "blobRef is not a blob that was uploaded for this task");
        }

        _counter++;
        _documents[id] = new StoredDocument(id, kind, projectId, keyVersion, rev, envelope, _counter, blobRef);
        return Json(new JsonObject { ["rev"] = rev, ["cursor"] = _counter.ToString() });
    }

    private static JsonObject DocumentJson(StoredDocument doc)
    {
        var json = new JsonObject
        {
            ["id"] = doc.Id.ToString("D"),
            ["kind"] = doc.Kind,
            ["keyVersion"] = doc.KeyVersion,
            ["rev"] = doc.Rev,
            ["size"] = doc.Envelope.Length,
            ["writtenBy"] = Guid.Empty.ToString("D"),
            ["updatedAt"] = "2026-11-15T12:00:00Z",
            ["data"] = Base64Url.Encode(doc.Envelope),
        };
        if (doc.ProjectId is { } projectId)
            json["projectId"] = projectId.ToString("D");
        json["blobRef"] = doc.BlobRef?.ToString("D");
        return json;
    }

    private static HttpResponseMessage Json(JsonNode node) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(node.ToJsonString(), Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage Error(HttpStatusCode status, string code, string message) => new(status)
    {
        Content = new StringContent(
            new JsonObject { ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString(),
            Encoding.UTF8, "application/json"),
    };
}
