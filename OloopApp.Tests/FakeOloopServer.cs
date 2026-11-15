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
        Guid Id, string Kind, Guid? ProjectId, int KeyVersion, int Rev, byte[] Envelope, long Seq);

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
                    return Put(id, request, body);
            }

            return Error(HttpStatusCode.NotFound, "not_found", "no such route");
        }
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

        _counter++;
        _documents[id] = new StoredDocument(id, kind, projectId, keyVersion, rev, envelope, _counter);
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
