using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace OloopApp.Core.Server;

/// <summary>
/// The server answered with an error (SERVER.md, "API v1"), or could not be
/// reached. It is an <see cref="IOException"/>, so the view models handle it like
/// any other failure of a store.
/// </summary>
public sealed class ServerApiException : IOException
{
    public ServerApiException(int status, string code, string message, TimeSpan? retryAfter = null)
        : base(message)
    {
        Status = status;
        Code = code;
        RetryAfter = retryAfter;
    }

    /// <summary>The HTTP status, or 0 when there was no answer.</summary>
    public int Status { get; }

    /// <summary>The <c>code</c> of the error, such as <c>conflict</c>; empty when there was none.</summary>
    public string Code { get; }

    /// <summary>The wait that <c>429</c> asks for.</summary>
    public TimeSpan? RetryAfter { get; }

    public bool IsConflict => Status == (int)HttpStatusCode.Conflict;
    public bool IsNotFound => Status == (int)HttpStatusCode.NotFound;

    public static ServerApiException Unreachable(string message, Exception inner)
        => new(0, "", message, inner);

    private ServerApiException(int status, string code, string message, Exception inner)
        : base(message, inner)
    {
        Status = status;
        Code = code;
    }
}

/// <summary>An answer of the server with a status below 400.</summary>
public sealed record ServerResponse(int Status, byte[] Body, string? ETag)
{
    public JsonElement Json()
    {
        try
        {
            return JsonDocument.Parse(Body).RootElement;
        }
        catch (JsonException ex)
        {
            throw new ServerApiException(Status, "", $"The server's answer is not JSON: {ex.Message}");
        }
    }
}

/// <summary>
/// One signed-in device on an oloop server: the tokens, and every request with
/// them (SERVER.md, "Authentication"). An access token lasts 15 minutes; a request
/// that is answered with 401 is repeated once after the refresh token has made a
/// new one. The refresh token <b>rotates on every use</b> and one that is used twice
/// revokes the device, so <see cref="RefreshTokenChanged"/> has to be saved at once
/// and requests are never made in parallel with a refresh.
/// </summary>
public sealed class ServerApi : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private string? _accessToken;
    private string? _refreshToken;

    /// <param name="http">The client. Not disposed with this object when it is handed in.</param>
    /// <param name="baseUrl">A normalized address, see <see cref="OloopApp.Core.Io.ServerEndpoint"/>.</param>
    public ServerApi(HttpClient http, string baseUrl)
    {
        _http = http;
        _baseUri = new Uri(baseUrl.TrimEnd('/') + "/");
    }

    /// <summary>The user id that login or refresh returned. Empty before the first sign-in.</summary>
    public Guid UserId { get; private set; }

    public bool SignedIn => _accessToken is not null;

    /// <summary>The refresh token of this device, the one to keep in the secret store.</summary>
    public string? RefreshToken => _refreshToken;

    /// <summary>Raised with the new refresh token each time it rotates.</summary>
    public event Action<string>? RefreshTokenChanged;

    // ---------------- Sign in ----------------

    public async Task LoginAsync(string username, string password, string deviceName, CancellationToken ct = default)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["deviceName"] = deviceName,
        });

        var response = await SendRawAsync(HttpMethod.Post, "v1/auth/login", body, null, null, ct).ConfigureAwait(false);
        TakeTokens(response.Json());
    }

    /// <summary>Signs in with the refresh token of an earlier session.</summary>
    public async Task ResumeAsync(string refreshToken, CancellationToken ct = default)
    {
        _refreshToken = refreshToken;
        await RefreshAsync(staleAccessToken: null, ct).ConfigureAwait(false);
    }

    /// <summary>Revokes this device. The tokens are forgotten, whatever the server says.</summary>
    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            if (_accessToken is not null)
                await SendAsync(HttpMethod.Post, "v1/auth/logout", ct: ct).ConfigureAwait(false);
        }
        finally
        {
            _accessToken = null;
            _refreshToken = null;
        }
    }

    // ---------------- Requests ----------------

    /// <summary>
    /// A request with the bearer token. Throws <see cref="ServerApiException"/> for
    /// every status from 400 and for a server that does not answer.
    /// </summary>
    /// <param name="headers">Extra headers such as <c>If-Match</c>.</param>
    public async Task<ServerResponse> SendAsync(
        HttpMethod method,
        string path,
        byte[]? body = null,
        string contentType = "application/json",
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        var token = _accessToken ?? throw new ServerApiException(401, "unauthorized", "You are not signed in to the server.");

        try
        {
            return await SendRawAsync(method, path, body, contentType, headers, ct, token).ConfigureAwait(false);
        }
        catch (ServerApiException ex) when (ex.Status == (int)HttpStatusCode.Unauthorized && _refreshToken is not null)
        {
            await RefreshAsync(token, ct).ConfigureAwait(false);
        }

        return await SendRawAsync(method, path, body, contentType, headers, ct, _accessToken).ConfigureAwait(false);
    }

    public Task<ServerResponse> SendJsonAsync(
        HttpMethod method,
        string path,
        object payload,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken ct = default)
        => SendAsync(method, path, JsonSerializer.SerializeToUtf8Bytes(payload), "application/json", headers, ct);

    // ---------------- Internals ----------------

    private async Task RefreshAsync(string? staleAccessToken, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another request has refreshed while this one waited: its token is good.
            if (staleAccessToken is not null && _accessToken != staleAccessToken && _accessToken is not null)
                return;

            var body = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
            {
                ["refreshToken"] = _refreshToken ?? throw new ServerApiException(401, "unauthorized", "You are not signed in to the server."),
            });

            try
            {
                var response = await SendRawAsync(HttpMethod.Post, "v1/auth/refresh", body, null, null, ct).ConfigureAwait(false);
                TakeTokens(response.Json());
            }
            catch (ServerApiException ex) when (ex.Status == (int)HttpStatusCode.Unauthorized)
            {
                // Revoked or used twice: this device has to sign in again.
                _accessToken = null;
                _refreshToken = null;
                throw new ServerApiException(401, "unauthorized", "The sign-in to the server has expired. Sign in again.");
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private void TakeTokens(JsonElement answer)
    {
        string Text(string name) => answer.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()!
            : throw new ServerApiException(200, "", $"The server's answer has no {name}.");

        var userId = Guid.TryParse(Text("userId"), out var parsed)
            ? parsed
            : throw new ServerApiException(200, "", "The server's answer has no valid userId.");

        _accessToken = Text("accessToken");
        _refreshToken = Text("refreshToken");
        UserId = userId;
        RefreshTokenChanged?.Invoke(_refreshToken);
    }

    private async Task<ServerResponse> SendRawAsync(
        HttpMethod method,
        string path,
        byte[]? body,
        string? contentType,
        IReadOnlyDictionary<string, string>? headers,
        CancellationToken ct,
        string? bearer = null)
    {
        using var request = new HttpRequestMessage(method, new Uri(_baseUri, path));
        if (bearer is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/json");
        }

        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw ServerApiException.Unreachable($"The server cannot be reached: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw ServerApiException.Unreachable("The server did not answer in time.", ex);
        }

        using (response)
        {
            var data = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            if (status >= 400)
                throw Error(status, data, response);

            return new ServerResponse(status, data, response.Headers.ETag?.Tag);
        }
    }

    private static ServerApiException Error(int status, byte[] body, HttpResponseMessage response)
    {
        var code = "";
        var message = "";
        try
        {
            var error = JsonDocument.Parse(body).RootElement.GetProperty("error");
            code = error.GetProperty("code").GetString() ?? "";
            message = error.GetProperty("message").GetString() ?? "";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // A proxy's own error page, or an empty body.
        }

        TimeSpan? retryAfter = null;
        if (response.Headers.RetryAfter?.Delta is { } delta)
            retryAfter = delta;

        if (message.Length == 0)
            message = $"The server answered {status.ToString(CultureInfo.InvariantCulture)} {response.ReasonPhrase}".TrimEnd();

        if (status == (int)HttpStatusCode.TooManyRequests && retryAfter is { } wait)
            message += $" Try again in {Math.Ceiling(wait.TotalSeconds).ToString(CultureInfo.InvariantCulture)} seconds.";

        return new ServerApiException(status, code, message, retryAfter);
    }

    public void Dispose() => _refreshLock.Dispose();
}
