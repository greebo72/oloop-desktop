using System.Globalization;
using OloopApp.Core.Models;
using TaskStatus = OloopApp.Core.Models.TaskStatus;

namespace OloopApp.Core.Io;

/// <summary>
/// Access to one oloop data root: scans projects and tasks and performs every
/// write (create / edit / close / reopen / comment).
///
/// Write path (SPEC.md, "Backup/recovery" and "Concurrent writes"):
/// re-read and compare <c>updatedAt</c>, refuse on conflict, copy the old file
/// to <c>.backup/&lt;name&gt;.bak</c> (one generation), then write atomically
/// via a temp file in the same directory.
/// </summary>
public sealed class OloopStore
{
    /// <summary>The only <c>formatVersion</c> this client knows.</summary>
    public const int SupportedFormatVersion = 1;

    private readonly string _root;

    public OloopStore(string dataRoot)
    {
        _root = dataRoot;
    }

    /// <summary>The data root this store works on.</summary>
    public string Root => _root;

    // ---------------- Scanning ----------------

    /// <summary>
    /// Reads the whole data root. Files that violate the format do not abort
    /// the scan: they come back as <see cref="OloopSnapshot.Unreadable"/> and
    /// are never written to.
    /// </summary>
    public async Task<OloopSnapshot> LoadAsync(CancellationToken ct = default)
    {
        var projects = new List<OloopProject>();
        var tasks = new List<OloopTask>();
        var unreadable = new List<UnreadableFile>();

        if (!Directory.Exists(_root))
            return OloopSnapshot.Empty;

        foreach (var folder in Directory.EnumerateDirectories(_root).OrderBy(p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var folderName = Path.GetFileName(folder);
            if (folderName.StartsWith('.'))
                continue;

            var projectFile = Path.Combine(folder, "project.md");
            if (!File.Exists(projectFile))
                continue;

            OloopProject project;
            try
            {
                var text = await ReadAllTextAsync(projectFile, ct).ConfigureAwait(false);
                project = ProjectIo.Read(text);
            }
            catch (OloopFormatException ex)
            {
                unreadable.Add(new UnreadableFile(projectFile, ex.Message));
                continue;
            }
            catch (IOException ex)
            {
                unreadable.Add(new UnreadableFile(projectFile, ex.Message));
                continue;
            }

            project.FolderName = folderName;
            projects.Add(project);

            var tasksFolder = Path.Combine(folder, "tasks");
            if (!Directory.Exists(tasksFolder))
                continue;

            foreach (var file in Directory.EnumerateFiles(tasksFolder, "*.md").OrderBy(p => p, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();

                var fileName = Path.GetFileNameWithoutExtension(file);
                try
                {
                    var text = await ReadAllTextAsync(file, ct).ConfigureAwait(false);
                    var task = TaskIo.Read(fileName, text, project.Id);
                    task.ProjectName = project.Name;
                    tasks.Add(task);
                }
                catch (OloopFormatException ex)
                {
                    unreadable.Add(new UnreadableFile(file, ex.Message));
                }
                catch (IOException ex)
                {
                    unreadable.Add(new UnreadableFile(file, ex.Message));
                }
            }
        }

        // Statistics per project.
        foreach (var project in projects)
        {
            var own = tasks.Where(t => t.ProjectId == project.Id).ToList();
            project.TaskCount = own.Count;
            project.OpenTaskCount = own.Count(t => !t.IsClosed);
            project.LatestUpdatedAt = own.Count == 0
                ? null
                : own.OrderByDescending(t => t.UpdatedAt, StringComparer.Ordinal).First().UpdatedAt;
        }

        return new OloopSnapshot
        {
            Projects = projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Tasks = tasks.OrderBy(t => t.Id, StringComparer.Ordinal).ToList(),
            Unreadable = unreadable,
        };
    }

    // ---------------- File locations ----------------

    /// <summary>The folder of a project, found by the id in its <c>project.md</c>.</summary>
    public string ProjectFolder(OloopProject project)
    {
        foreach (var folder in Directory.EnumerateDirectories(_root).OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(folder);
            if (name.StartsWith('.'))
                continue;

            var projectFile = Path.Combine(folder, "project.md");
            if (!File.Exists(projectFile))
                continue;

            try
            {
                var text = File.ReadAllText(projectFile);
                if (ProjectIo.Read(text).Id == project.Id)
                    return folder;
            }
            catch (OloopFormatException)
            {
                // Unreadable project files are skipped; the caller gets no match.
            }
        }

        return Path.Combine(_root, project.FolderName);
    }

    private static string TaskFilePath(string projectFolder, string taskId)
        => Path.Combine(projectFolder, "tasks", taskId + ".md");

    /// <summary>
    /// The absolute path of the image of a task, or null when the task has no
    /// image. The path in the file is relative to the project folder
    /// (SPEC.md, "Relative imagePath").
    ///
    /// A path that points outside the project folder is refused — the path
    /// comes from a file and must not reach the rest of the disk.
    /// </summary>
    public string? ResolveImagePath(OloopProject project, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // The folder name comes from the scan. It is only unknown for a
        // project that was never read from disk.
        var folder = project.FolderName.Length > 0
            ? Path.Combine(_root, project.FolderName)
            : ProjectFolder(project);

        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(folder, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException
                                       or NotSupportedException)
        {
            return null;
        }

        var root = Path.GetFullPath(folder);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return null;

        return full;
    }

    // ---------------- Mutations ----------------

    /// <summary>
    /// Creates a task: fresh UUID4, status open, imagePath null,
    /// createdAt = updatedAt = now, createdBy = updatedBy = actor.
    /// </summary>
    public async Task<OloopTask> CreateTaskAsync(
        OloopProject project,
        TaskType type,
        string title,
        string description,
        IEnumerable<string>? labels,
        string actor,
        CancellationToken ct = default)
    {
        var trimmedTitle = title.Trim();
        if (trimmedTitle.Length == 0)
            throw new OloopFormatException("title must not be empty");

        var now = NowUtc();
        var task = new OloopTask
        {
            FormatVersion = SupportedFormatVersion,
            Id = Guid.NewGuid().ToString("D"),
            ProjectId = project.Id,
            Type = type,
            Title = trimmedTitle,
            Status = TaskStatus.open,
            ImagePath = null,
            Labels = NormalizeLabels(labels),
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor,
            Comments = [],
            Description = description ?? "",
            ProjectName = project.Name,
        };

        await WriteTaskAsync(project, task, expectedUpdatedAt: null, ct).ConfigureAwait(false);
        return task;
    }

    /// <summary>
    /// Edits title, description and labels. Never touches id, type, createdAt,
    /// createdBy or the file name.
    /// </summary>
    public async Task<OloopTask> EditTaskAsync(
        OloopTask task,
        OloopProject project,
        string? title,
        string? description,
        IEnumerable<string>? labels,
        string actor,
        CancellationToken ct = default)
    {
        var updated = task.With(
            title: title is null ? null : title.Trim(),
            description: description,
            labels: labels is null ? null : NormalizeLabels(labels),
            updatedAt: NowUtc(),
            updatedBy: actor);

        await WriteTaskAsync(project, updated, task.UpdatedAt, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Flips the status open ↔ closed. Closing is the only completion
    /// mechanism — there is no delete anywhere.
    /// </summary>
    public async Task<OloopTask> SetStatusAsync(
        OloopTask task,
        OloopProject project,
        bool closed,
        string actor,
        CancellationToken ct = default)
    {
        var updated = task.With(
            status: closed ? TaskStatus.closed : TaskStatus.open,
            updatedAt: NowUtc(),
            updatedBy: actor);

        await WriteTaskAsync(project, updated, task.UpdatedAt, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>Appends a comment (append-only, no edits, no deletes).</summary>
    public async Task<OloopTask> AddCommentAsync(
        OloopTask task,
        OloopProject project,
        string body,
        string actor,
        CancellationToken ct = default)
    {
        var trimmed = body.Trim();
        if (trimmed.Length == 0)
            throw new OloopFormatException("comment body must not be empty");

        var comments = task.Comments.ToList();
        comments.Add(new TaskComment { Body = trimmed, CreatedAt = NowUtc() });

        var updated = task.With(
            comments: comments,
            updatedAt: NowUtc(),
            updatedBy: actor);

        await WriteTaskAsync(project, updated, task.UpdatedAt, ct).ConfigureAwait(false);
        return updated;
    }

    /// <summary>
    /// Creates <c>&lt;slug&gt;/project.md</c> and the empty <c>&lt;slug&gt;/tasks/</c>
    /// folder. Refuses when the name (case-insensitive) or the slug folder is
    /// already taken.
    /// </summary>
    public async Task<OloopProject> CreateProjectAsync(
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

        var slug = Slugify(trimmed);

        if (Directory.Exists(_root))
        {
            foreach (var folder in Directory.EnumerateDirectories(_root))
            {
                var folderName = Path.GetFileName(folder);
                if (folderName.StartsWith('.'))
                    continue;

                var projectFile = Path.Combine(folder, "project.md");
                if (!File.Exists(projectFile))
                    continue;

                OloopProject existing;
                try
                {
                    existing = ProjectIo.Read(await ReadAllTextAsync(projectFile, ct).ConfigureAwait(false));
                }
                catch (OloopFormatException ex)
                {
                    throw new OloopFormatException(
                        $"{projectFile} cannot be read, so the name cannot be checked: {ex.Message}", ex);
                }

                if (string.Equals(existing.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                    throw new OloopFormatException($"project already exists: {trimmed}");
            }
        }

        var folderPath = Path.Combine(_root, slug);
        if (Directory.Exists(folderPath))
            throw new OloopFormatException($"folder {slug} already exists");

        var project = new OloopProject
        {
            FormatVersion = SupportedFormatVersion,
            Id = Guid.NewGuid().ToString("D"),
            Name = trimmed,
            CreatedAt = NowUtc(),
            ReferencePath = referencePath,
            ReferenceHost = referenceHost,
            FolderName = slug,
        };

        Directory.CreateDirectory(folderPath);
        Directory.CreateDirectory(Path.Combine(folderPath, "tasks"));

        var text = ProjectIo.Write(project);
        // Never write something this client would refuse to read back.
        ProjectIo.Read(text);
        await WriteAtomicAsync(Path.Combine(folderPath, "project.md"), text, ct).ConfigureAwait(false);

        return project;
    }

    // ---------------- Write path ----------------

    /// <summary>
    /// Writes a task file: re-read and compare <c>updatedAt</c>, back up the
    /// old file, then write atomically.
    /// </summary>
    public async Task WriteTaskAsync(
        OloopProject project,
        OloopTask task,
        string? expectedUpdatedAt,
        CancellationToken ct = default)
    {
        var folder = ProjectFolder(project);
        var fileName = task.Id + ".md";
        var path = TaskFilePath(folder, task.Id);

        var text = TaskIo.Write(task);
        // Never write something this client would refuse to read back.
        TaskIo.Read(fileName, text, project.Id);

        if (File.Exists(path))
        {
            var diskText = await ReadAllTextAsync(path, ct).ConfigureAwait(false);
            OloopTask disk;
            try
            {
                disk = TaskIo.Read(fileName, diskText, project.Id);
            }
            catch (OloopFormatException ex)
            {
                throw new OloopFormatException(
                    $"file on disk is unreadable and is left untouched: {ex.Message}", ex);
            }

            if (expectedUpdatedAt is not null && disk.UpdatedAt != expectedUpdatedAt)
                throw new OloopConflictException(
                    $"the task changed on disk since it was loaded (on disk: {disk.UpdatedAt}, loaded: {expectedUpdatedAt})",
                    disk.UpdatedAt);

            // One-generation backup before the old file is replaced.
            var backupFolder = Path.Combine(folder, ".backup");
            Directory.CreateDirectory(backupFolder);
            var backupPath = Path.Combine(backupFolder, fileName + ".bak");
            File.Copy(path, backupPath, overwrite: true);
        }

        Directory.CreateDirectory(Path.Combine(folder, "tasks"));
        await WriteAtomicAsync(path, text, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes a file atomically: a temp file in the same directory, then a
    /// move that replaces the target.
    /// </summary>
    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);

        var temp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await File.WriteAllTextAsync(temp, content, ct).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            try { File.Delete(temp); } catch (IOException) { /* best effort */ }
            throw;
        }
    }

    private static async Task<string> ReadAllTextAsync(string path, CancellationToken ct)
        => await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);

    // ---------------- Helpers ----------------

    /// <summary>The current time as ISO 8601 UTC with a trailing Z.</summary>
    public static string NowUtc()
        => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Trims labels, drops empty ones and removes duplicates (first wins).
    /// </summary>
    public static IReadOnlyList<string> NormalizeLabels(IEnumerable<string>? labels)
    {
        if (labels is null)
            return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var label in labels)
        {
            var trimmed = label.Trim();
            if (trimmed.Length == 0 || !seen.Add(trimmed))
                continue;
            result.Add(trimmed);
        }
        return result;
    }

    /// <summary>
    /// The folder name of a project: lowercase, ASCII letters, digits and
    /// dashes. Non-ASCII characters are folded to their ASCII base where
    /// possible, everything else becomes a dash.
    /// </summary>
    public static string Slugify(string name)
    {
        var folded = name.Trim().ToLowerInvariant().Replace("ß", "ss");
        var normalized = folded.Normalize(System.Text.NormalizationForm.FormD);
        var ascii = new string(normalized.Where(c => c < 128).ToArray());
        var slug = System.Text.RegularExpressions.Regex.Replace(ascii, "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length == 0)
            throw new OloopFormatException($"project name yields no valid slug: '{name}'");
        return slug;
    }
}
