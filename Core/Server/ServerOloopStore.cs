using System.Globalization;
using System.Text;
using System.Text.Json;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Models;
using Sodium;

namespace OloopApp.Core.Server;

/// <summary>The user of a signed-in device whose key bundle has been opened.</summary>
public sealed record UnlockedUser(Guid UserId, byte[] SecretKey, byte[] PublicKey);

/// <summary>
/// The data set on an oloop server (SERVER.md). Every document is encrypted here
/// before it leaves and decrypted after it arrives; the server sees only what the
/// spec lists. The rules of the writes are those of <see cref="OloopStoreBase"/>.
///
/// Online only: there is no local copy. A write that cannot reach the server fails
/// with an <see cref="IOException"/>.
///
/// Not done yet: images (<c>blob:</c> paths), keys of other principals (this store
/// opens only the keys it granted itself), and the pins of SERVER.md.
/// </summary>
public sealed class ServerOloopStore : OloopStoreBase
{
    private const int PageSize = 200;

    private readonly ServerApi _api;
    private readonly UnlockedUser _user;
    private readonly IRevisionLedger _ledger;

    // The keys of the projects, by project and key version. Filled by every load.
    private readonly Dictionary<Guid, Dictionary<int, byte[]>> _projectKeys = [];

    // The revision of each document as this store last saw it. A change is written on top of it.
    // It starts from the ledger of this device, so that what was seen in an earlier run counts too.
    private readonly Dictionary<Guid, int> _revisions;
    private bool _revisionsChanged;

    // The conflicts that were reported to the user, by task: the revision and the updatedAt that
    // were shown. A write that expects exactly this is the user's choice to overwrite.
    private readonly Dictionary<Guid, (int Rev, string UpdatedAt)> _conflicts = [];

    private readonly object _gate = new();
    private string? _knownHead;
    private IReadOnlyList<OloopProject> _projects = [];

    /// <param name="ledger">Where the highest revisions seen are kept between runs. Without one they live as long as this object.</param>
    public ServerOloopStore(ServerApi api, UnlockedUser user, IRevisionLedger? ledger = null)
    {
        _api = api;
        _user = user;
        _ledger = ledger ?? new MemoryRevisionLedger();
        _revisions = new Dictionary<Guid, int>(_ledger.Load());
    }

    // ---------------- Availability and change detection ----------------

    /// <inheritdoc />
    public override string? CheckAvailable()
        => _api.SignedIn ? null : "You are not signed in to the server.";

    /// <summary>
    /// The newest cursor this store has seen of the server. It moves when a load
    /// starts and when the change source finds a newer one, so two equal values mean
    /// that a reload would show the same.
    /// </summary>
    public override string Fingerprint()
    {
        lock (_gate)
            return _knownHead ?? "";
    }

    /// <summary>Polls <c>GET /v1/head</c> (SERVER.md, "Noticing changes").</summary>
    public override IChangeSource CreateChangeSource(TimeSpan debounce)
        => new ServerChangeSource(this, ServerChangeSource.DefaultInterval);

    internal async Task<string> ReadHeadAsync(CancellationToken ct)
    {
        var response = await _api.SendAsync(HttpMethod.Get, "v1/head", ct: ct).ConfigureAwait(false);
        return Text(response.Json(), "cursor");
    }

    /// <summary>Takes a cursor the poller has seen. True when it is a new one.</summary>
    internal bool NoteHead(string cursor)
    {
        lock (_gate)
        {
            if (_knownHead == cursor)
                return false;

            _knownHead = cursor;
            return true;
        }
    }

    // ---------------- Reading ----------------

    /// <inheritdoc />
    public override async Task<OloopSnapshot> LoadAsync(CancellationToken ct = default)
    {
        // The head before everything else: what changes after it makes the head
        // differ, so the change source sees it.
        NoteHead(await ReadHeadAsync(ct).ConfigureAwait(false));

        await LoadProjectKeysAsync(ct).ConfigureAwait(false);

        var projects = new List<OloopProject>();
        var taskTexts = new List<(Guid Id, int KeyVersion, int Rev, Guid ProjectId, byte[] Data)>();
        var unreadable = new List<UnreadableFile>();

        var cursor = "";
        bool hasMore;
        do
        {
            ct.ThrowIfCancellationRequested();
            var path = "v1/documents?cursor=" + Uri.EscapeDataString(cursor)
                + "&limit=" + PageSize.ToString(CultureInfo.InvariantCulture);
            var page = (await _api.SendAsync(HttpMethod.Get, path, ct: ct).ConfigureAwait(false)).Json();

            foreach (var item in page.GetProperty("items").EnumerateArray())
            {
                var id = Guid.Parse(Text(item, "id"));
                try
                {
                    var document = OpenDocument(item);
                    if (document.Kind == "project")
                    {
                        var project = ProjectIo.Read(document.Text);
                        if (!Guid.TryParse(project.Id, out var projectId) || projectId != id)
                            throw new OloopFormatException("the id of the project is not the id of its document");
                        projects.Add(project);
                    }
                    else
                    {
                        taskTexts.Add((id, document.KeyVersion, document.Rev, document.ProjectId!.Value, document.Bytes));
                    }

                    lock (_gate)
                        Remember(id, document.Rev);
                }
                catch (Exception ex) when (ex is OloopFormatException or OloopCryptoException or JsonException
                                               or KeyNotFoundException or InvalidOperationException or FormatException)
                {
                    unreadable.Add(new UnreadableFile("server:" + id.ToString("D"), ex.Message));
                }
            }

            cursor = Text(page, "nextCursor");
            hasMore = page.GetProperty("hasMore").GetBoolean();
        }
        while (hasMore);

        var byId = projects.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var tasks = new List<OloopTask>();
        foreach (var item in taskTexts)
        {
            var projectId = item.ProjectId.ToString("D");
            if (!byId.TryGetValue(projectId, out var project))
            {
                unreadable.Add(new UnreadableFile("server:" + item.Id.ToString("D"),
                    "the task belongs to a project that was not delivered"));
                continue;
            }

            try
            {
                var task = TaskIo.Read(item.Id.ToString("D"), Encoding.UTF8.GetString(item.Data), project.Id);
                task.ProjectName = project.Name;
                tasks.Add(task);
            }
            catch (OloopFormatException ex)
            {
                unreadable.Add(new UnreadableFile("server:" + item.Id.ToString("D"), ex.Message));
            }
        }

        foreach (var project in projects)
        {
            var own = tasks.Where(t => t.ProjectId == project.Id).ToList();
            project.TaskCount = own.Count;
            project.OpenTaskCount = own.Count(t => !t.IsClosed);
            project.LatestUpdatedAt = own.Count == 0
                ? null
                : own.OrderByDescending(t => t.UpdatedAt, StringComparer.Ordinal).First().UpdatedAt;
        }

        var ordered = projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        lock (_gate)
            _projects = ordered;

        PersistRevisions();

        return new OloopSnapshot
        {
            Projects = ordered,
            Tasks = tasks.OrderBy(t => t.Id, StringComparer.Ordinal).ToList(),
            Unreadable = unreadable,
        };
    }

    /// <summary>
    /// Reads <c>GET /v1/projects</c> and opens the keys. Only keys that this user
    /// granted to itself are opened: for a key of another principal the sender's
    /// public key would have to be a pinned one (SERVER.md, "Handing a project
    /// key"), and there is no pin store yet.
    /// </summary>
    private async Task LoadProjectKeysAsync(CancellationToken ct)
    {
        var answer = (await _api.SendAsync(HttpMethod.Get, "v1/projects", ct: ct).ConfigureAwait(false)).Json();
        var keys = new Dictionary<Guid, Dictionary<int, byte[]>>();

        foreach (var project in answer.EnumerateArray())
        {
            var projectId = Guid.Parse(Text(project, "id"));
            var opened = new Dictionary<int, byte[]>();
            foreach (var entry in project.GetProperty("keys").EnumerateArray())
            {
                if (!Guid.TryParse(Text(entry, "grantedBy"), out var grantedBy) || grantedBy != _user.UserId)
                    continue;

                var keyVersion = entry.GetProperty("keyVersion").GetInt32();
                try
                {
                    opened[keyVersion] = ProjectKeyBox.Unwrap(
                        Base64Url.Decode(Text(entry, "wrapped")), _user.SecretKey, _user.PublicKey,
                        projectId, keyVersion);
                }
                catch (OloopCryptoException)
                {
                    // The documents of this key version come back as unreadable.
                }
            }

            keys[projectId] = opened;
        }

        lock (_gate)
        {
            _projectKeys.Clear();
            foreach (var (id, opened) in keys)
                _projectKeys[id] = opened;
        }
    }

    private sealed record OpenedDocument(string Kind, int KeyVersion, int Rev, Guid? ProjectId, byte[] Bytes)
    {
        public string Text => Encoding.UTF8.GetString(Bytes);
    }

    /// <summary>Decrypts a <c>Document</c> of the API.</summary>
    private OpenedDocument OpenDocument(JsonElement item)
    {
        var id = Guid.Parse(Text(item, "id"));
        var kind = Text(item, "kind");
        var keyVersion = item.GetProperty("keyVersion").GetInt32();
        var rev = item.GetProperty("rev").GetInt32();

        Guid? projectId = kind == "project"
            ? id
            : Guid.Parse(Text(item, "projectId"));

        byte[] key;
        lock (_gate)
        {
            if (!_projectKeys.TryGetValue(projectId!.Value, out var versions) || !versions.TryGetValue(keyVersion, out key!))
                throw new OloopCryptoException($"there is no key for version {keyVersion} of this project");

            if (_revisions.TryGetValue(id, out var seen) && rev < seen)
                throw new OloopCryptoException(
                    $"the server shows revision {rev} of this document, after revision {seen} was seen: an older state. "
                    + "If the server was restored from a backup on purpose, use \"Forget seen revisions\" in the settings.");
        }

        var bytes = Envelope.Open(key, Base64Url.Decode(Text(item, "data")),
            AssociatedData.Document(kind, id, keyVersion, rev));
        return new OpenedDocument(kind, keyVersion, rev, projectId, bytes);
    }

    private void Remember(Guid id, int rev)
    {
        if (!_revisions.TryGetValue(id, out var seen) || rev > seen)
        {
            _revisions[id] = rev;
            _revisionsChanged = true;
        }
    }

    /// <summary>
    /// Writes the ledger when something was added to it. A ledger that cannot be written costs
    /// the protection for the next run, not this one: it is not a reason to fail a load or a write.
    /// </summary>
    private void PersistRevisions()
    {
        Dictionary<Guid, int> snapshot;
        lock (_gate)
        {
            if (!_revisionsChanged)
                return;

            snapshot = new Dictionary<Guid, int>(_revisions);
            _revisionsChanged = false;
        }

        try
        {
            _ledger.Save(snapshot);
        }
        catch (IOException)
        {
            lock (_gate)
                _revisionsChanged = true; // the next load or write tries again
        }
    }

    /// <summary>
    /// Forgets which revisions were seen, here and on the device. For a server that was restored
    /// from a backup: it shows older states on purpose, and they would be refused for ever.
    /// </summary>
    public void ForgetRevisions()
    {
        lock (_gate)
        {
            _revisions.Clear();
            _revisionsChanged = false;
        }

        _ledger.Clear();
    }

    /// <inheritdoc />
    public override ImageOpenResult OpenImage(OloopProject project, string? imagePath)
        => new(null, "An image on the server is fetched over the network: use OpenImageAsync.");

    // ---------------- Images ----------------

    private const string BlobPrefix = "blob:";

    // The most an image may weigh (SERVER.md, "Limits"), and the envelope around it.
    private const int MaxBlobBytes = 10 * 1024 * 1024 + 1024;

    // The decrypted images that were shown. A blob never changes, so the same image is not fetched
    // and decrypted again at every reload of the data; the oldest go when the total is too big.
    private const long ImageCacheBytes = 64L * 1024 * 1024;
    private readonly Dictionary<(Guid Blob, Guid Task), byte[]> _images = [];
    private readonly Queue<(Guid Blob, Guid Task)> _imageOrder = new();
    private long _imageBytes;

    private void KeepImage((Guid Blob, Guid Task) key, byte[] bytes)
    {
        lock (_gate)
        {
            if (_images.ContainsKey(key) || bytes.Length > ImageCacheBytes)
                return;

            _images[key] = bytes;
            _imageOrder.Enqueue(key);
            _imageBytes += bytes.Length;
            while (_imageBytes > ImageCacheBytes && _imageOrder.TryDequeue(out var oldest))
            {
                if (_images.Remove(oldest, out var gone))
                    _imageBytes -= gone.Length;
            }
        }
    }

    /// <summary>The blob id of an <c>imagePath</c> of the form <c>blob:&lt;id&gt;</c>, or null for anything else.</summary>
    private static Guid? BlobOf(string? imagePath)
        => imagePath is not null
           && imagePath.StartsWith(BlobPrefix, StringComparison.Ordinal)
           && Guid.TryParse(imagePath[BlobPrefix.Length..], out var id)
            ? id
            : null;

    /// <summary>
    /// Fetches and decrypts the image of a task (SERVER.md, "Images"). The blob is bound to its task
    /// and to a key version by the associated data. The key version is not on the blob, so the
    /// versions of the project key that this client holds are tried, the newest first; the tag
    /// tells the right one. A blob that is not there, or not an image of a known kind, is reported
    /// and is not an error of the app.
    /// </summary>
    public override async Task<ImageOpenResult> OpenImageAsync(
        OloopProject project, OloopTask task, CancellationToken ct = default)
    {
        if (BlobOf(task.ImagePath) is not { } blobId
            || !Guid.TryParse(task.Id, out var taskId)
            || !Guid.TryParse(project.Id, out var projectId))
        {
            return new ImageOpenResult(null, "The image path of the task is not valid.");
        }

        lock (_gate)
        {
            if (_images.TryGetValue((blobId, taskId), out var kept))
                return new ImageOpenResult(new MemoryStream(kept, writable: false), null);
        }

        byte[] envelope;
        try
        {
            envelope = (await _api.SendAsync(HttpMethod.Get, "v1/blobs/" + blobId.ToString("D"), ct: ct)
                .ConfigureAwait(false)).Body;
        }
        catch (ServerApiException ex) when (ex.IsNotFound)
        {
            return new ImageOpenResult(null, "The image of the task is missing.");
        }
        catch (IOException ex)
        {
            return new ImageOpenResult(null, "The image of the task could not be loaded: " + ex.Message);
        }

        if (envelope.Length > MaxBlobBytes)
            return new ImageOpenResult(null, "The image of the task is larger than the server allows.");

        List<KeyValuePair<int, byte[]>> keys;
        lock (_gate)
        {
            keys = _projectKeys.TryGetValue(projectId, out var versions)
                ? versions.OrderByDescending(v => v.Key).ToList()
                : [];
        }

        if (keys.Count == 0)
            return new ImageOpenResult(null, "There is no key for the project of this image. Reload the data.");

        foreach (var (version, key) in keys)
        {
            byte[] bytes;
            try
            {
                bytes = Envelope.Open(key, envelope, AssociatedData.Blob(blobId, taskId, version));
            }
            catch (UnknownEnvelopeException)
            {
                return new ImageOpenResult(null, "The image of the task is in a format this version of oloop does not know.");
            }
            catch (OloopCryptoException)
            {
                continue; // another key version, or an image that is not this task's
            }

            if (!ImageKind.IsKnown(bytes))
                return new ImageOpenResult(null, "The image of the task is not a PNG, JPEG, GIF or WebP picture.");

            KeepImage((blobId, taskId), bytes);
            return new ImageOpenResult(new MemoryStream(bytes, writable: false), null);
        }

        return new ImageOpenResult(null, "The image of the task could not be decrypted. It may have been changed on the server.");
    }

    // ---------------- Writing ----------------

    /// <inheritdoc />
    public override async Task<OloopProject> CreateProjectAsync(
        string name,
        string? referencePath = null,
        string? referenceHost = null,
        CancellationToken ct = default)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
            throw new OloopFormatException("project name must not be empty");
        if (trimmed.EnumerateRunes().Count() > 200)
            throw new OloopFormatException("project name must be at most 200 characters");

        // The server cannot compare names: it does not know them. Two devices that create
        // the same name at the same moment make two projects (SERVER.md, "What is stored where").
        var known = _projects;
        if (known.Count == 0)
            known = (await LoadAsync(ct).ConfigureAwait(false)).Projects;
        if (known.Any(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            throw new OloopFormatException($"project already exists: {trimmed}");

        var id = Guid.NewGuid();
        var project = new OloopProject
        {
            FormatVersion = SupportedFormatVersion,
            Id = id.ToString("D"),
            Name = trimmed,
            CreatedAt = NowUtc(),
            ReferencePath = referencePath,
            ReferenceHost = referenceHost,
        };

        var text = ProjectIo.Write(project);
        // Never write something this client would refuse to read back.
        ProjectIo.Read(text);

        const int keyVersion = 1;
        var projectKey = SodiumCore.GetRandomBytes(ProjectKeyBox.KeyBytes);
        var wrapped = ProjectKeyBox.Wrap(projectKey, id, keyVersion, _user.PublicKey, _user.SecretKey);

        var rev = await PutAsync(id, "project", null, keyVersion, 1, projectKey, Encoding.UTF8.GetBytes(text),
            baseRev: null,
            key: new { keyVersion, wrapped = Base64Url.Encode(wrapped) }, ct).ConfigureAwait(false);

        lock (_gate)
        {
            _projectKeys[id] = new Dictionary<int, byte[]> { [keyVersion] = projectKey };
            Remember(id, rev);
            _projects = [.. _projects, project];
        }

        return project;
    }

    /// <inheritdoc />
    public override async Task WriteTaskAsync(
        OloopProject project,
        OloopTask task,
        string? expectedUpdatedAt,
        CancellationToken ct = default)
    {
        var taskId = Guid.Parse(task.Id);
        var projectId = Guid.Parse(project.Id);

        var text = TaskIo.Write(task);
        // Never write something this client would refuse to read back.
        TaskIo.Read(task.Id, text, project.Id);
        var bytes = Encoding.UTF8.GetBytes(text);
        var blobRef = BlobOf(task.ImagePath);

        int keyVersion;
        byte[] key;
        lock (_gate)
        {
            if (!_projectKeys.TryGetValue(projectId, out var versions) || versions.Count == 0)
                throw new OloopCryptoException("there is no key for this project: load the data again");

            keyVersion = versions.Keys.Max();
            key = versions[keyVersion];
        }

        if (expectedUpdatedAt is null)
        {
            try
            {
                await PutAsync(taskId, "task", projectId, keyVersion, 1, key, bytes, baseRev: null, key: null, ct, blobRef)
                    .ConfigureAwait(false);
            }
            catch (ServerApiException ex) when (ex.IsConflict)
            {
                throw new OloopFormatException("a task with this id exists already", ex);
            }

            return;
        }

        int baseRev;
        lock (_gate)
        {
            if (!_revisions.TryGetValue(taskId, out baseRev))
                baseRev = 0;
        }

        if (baseRev > 0)
        {
            try
            {
                await PutAsync(taskId, "task", projectId, keyVersion, baseRev + 1, key, bytes, baseRev, key: null, ct, blobRef)
                    .ConfigureAwait(false);
                ForgetConflict(taskId);
                return;
            }
            catch (ServerApiException ex) when (ex.IsConflict)
            {
                // Somebody wrote since this store read the task.
            }
        }

        // The revision of this store stays where it was: only a load moves it, so a second try
        // of the same stale change is a conflict again and not a silent overwrite.
        var current = await ReadCurrentAsync(taskId, ct).ConfigureAwait(false);
        var currentTask = TaskIo.Read(task.Id, current.Text, project.Id);

        // updatedAt has a resolution of one second, so it cannot tell a change from the same
        // second. The first time, every change of the task is a conflict; the user then
        // decides, and an overwrite expects exactly what the conflict showed.
        bool chosen;
        lock (_gate)
            chosen = _conflicts.TryGetValue(taskId, out var shown)
                     && shown.Rev == current.Rev && shown.UpdatedAt == expectedUpdatedAt
                     && currentTask.UpdatedAt == expectedUpdatedAt;
        if (!chosen)
            throw ReportConflict(taskId, current.Rev, currentTask.UpdatedAt, expectedUpdatedAt);

        try
        {
            await PutAsync(taskId, "task", projectId, keyVersion, current.Rev + 1, key, bytes, current.Rev, key: null, ct, blobRef)
                .ConfigureAwait(false);
            ForgetConflict(taskId);
        }
        catch (ServerApiException ex) when (ex.IsConflict)
        {
            // It changed again in the moments since: the user has to look at it again.
            var again = await ReadCurrentAsync(taskId, ct).ConfigureAwait(false);
            var againTask = TaskIo.Read(task.Id, again.Text, project.Id);
            throw ReportConflict(taskId, again.Rev, againTask.UpdatedAt, expectedUpdatedAt);
        }
    }

    private OloopConflictException ReportConflict(Guid taskId, int rev, string currentUpdatedAt, string expectedUpdatedAt)
    {
        lock (_gate)
            _conflicts[taskId] = (rev, currentUpdatedAt);

        return new OloopConflictException(
            $"the task changed on the server since it was loaded (on the server: {currentUpdatedAt}, loaded: {expectedUpdatedAt})",
            currentUpdatedAt);
    }

    private void ForgetConflict(Guid taskId)
    {
        lock (_gate)
            _conflicts.Remove(taskId);
    }

    private async Task<OpenedDocument> ReadCurrentAsync(Guid id, CancellationToken ct)
    {
        var item = (await _api.SendAsync(HttpMethod.Get, "v1/documents/" + id.ToString("D"), ct: ct)
            .ConfigureAwait(false)).Json();
        return OpenDocument(item);
    }

    /// <summary>
    /// Encrypts and writes one revision. <paramref name="baseRev"/> null creates the
    /// document (<c>If-None-Match: *</c>), otherwise it is the revision this one
    /// is written on top of (<c>If-Match</c>). Returns the revision that is stored.
    /// </summary>
    private async Task<int> PutAsync(
        Guid id,
        string kind,
        Guid? projectId,
        int keyVersion,
        int rev,
        byte[] projectKey,
        byte[] payload,
        int? baseRev,
        object? key,
        CancellationToken ct,
        Guid? blobRef = null)
    {
        var envelope = Envelope.Seal(projectKey, payload, AssociatedData.Document(kind, id, keyVersion, rev));

        var body = new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["keyVersion"] = keyVersion,
            ["rev"] = rev,
            ["data"] = Base64Url.Encode(envelope),
        };
        if (projectId is not null)
            body["projectId"] = projectId.Value.ToString("D");
        if (key is not null)
            body["key"] = key;

        // A write of a task that has an image must name the blob again. A revision without the
        // reference lets the server drop it, and the blob is cleaned up after 30 days.
        if (blobRef is not null)
            body["blobRef"] = blobRef.Value.ToString("D");

        var headers = new Dictionary<string, string>
        {
            [baseRev is null ? "If-None-Match" : "If-Match"] = baseRev is null
                ? "*"
                : "\"" + baseRev.Value.ToString(CultureInfo.InvariantCulture) + "\"",
        };

        var answer = (await _api.SendJsonAsync(HttpMethod.Put, "v1/documents/" + id.ToString("D"), body, headers, ct)
            .ConfigureAwait(false)).Json();

        var stored = answer.GetProperty("rev").GetInt32();
        lock (_gate)
            Remember(id, stored);
        PersistRevisions();
        return stored;
    }

    private static string Text(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new JsonException($"the answer of the server has no {name}");
}
