using System.Text.RegularExpressions;
using OloopApp.Models;
using OloopApp.Services;

namespace OloopApp.Services;

/// <summary>
/// Writes oloop data files following SPEC.md:
/// - before every write, copy the affected file to `.backup/&lt;name&gt;.bak` (one generation)
/// - atomic write: temp file + replace
/// - re-read before writing; if <c>updatedAt</c> changed since the caller read
///   the task, throw <see cref="OloopConflictException"/> instead of overwriting
/// - writers always set <c>formatVersion</c>
/// </summary>
public static class OloopWriter
{
    public static string NowUtc() => DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

    // ---------------- Add ----------------

    public static OloopTask AddTask(string dataRoot, string projectName, string title,
                                    TaskType type, string actor, string? description = null,
                                    IReadOnlyList<string>? labels = null)
    {
        var projectDir = FindProjectDir(dataRoot, projectName);
        var project = OloopReader.ReadProject(
            Path.Combine(projectDir, "project.md"), Path.GetFileName(projectDir));

        var now = NowUtc();
        var id = Guid.NewGuid().ToString();
        var task = new OloopTask
        {
            FormatVersion = OloopReader.MaxFormatVersion,
            Id = id,
            ProjectId = project.Id,
            Type = type,
            Title = title,
            Status = TaskStatus.open,
            ImagePath = null,
            Labels = labels ?? Array.Empty<string>(),
            CreatedAt = now,
            CreatedBy = actor,
            UpdatedAt = now,
            UpdatedBy = actor,
            Comments = Array.Empty<TaskComment>(),
            Description = description ?? "",
            ProjectName = project.Name,
        };

        var tasksDir = Path.Combine(projectDir, "tasks");
        Directory.CreateDirectory(tasksDir);
        AtomicWrite(Path.Combine(tasksDir, id + ".md"), RenderTask(task));
        return task;
    }

    // ---------------- Status flip ----------------

    public static OloopTask SetStatus(string dataRoot, string taskFileStemOrId, TaskStatus status,
                                      string actor, string expectedUpdatedAt)
    {
        var (task, path) = LocateTask(dataRoot, taskFileStemOrId);
        if (!string.Equals(task.UpdatedAt, expectedUpdatedAt, StringComparison.Ordinal))
            throw new OloopConflictException(
                $"task {task.Id} changed since you read it (was {expectedUpdatedAt}, now {task.UpdatedAt}); re-read and retry");

        var now = NowUtc();
        var updated = new OloopTask
        {
            FormatVersion = task.FormatVersion,
            Id = task.Id,
            ProjectId = task.ProjectId,
            Type = task.Type,
            Title = task.Title,
            Status = status,
            ImagePath = task.ImagePath,
            Labels = task.Labels,
            CreatedAt = task.CreatedAt,
            CreatedBy = task.CreatedBy,
            UpdatedAt = now,
            UpdatedBy = actor,
            Comments = task.Comments,
            Description = task.Description,
            ProjectName = task.ProjectName,
        };
        WriteWithBackup(path, RenderTask(updated));
        return updated;
    }

    // ---------------- Edit (title / description / labels) ----------------

    public static OloopTask EditTask(string dataRoot, string taskFileStemOrId,
                                     string? newTitle, string? newDescription,
                                     IReadOnlyList<string>? newLabels, string actor,
                                     string expectedUpdatedAt)
    {
        var (task, path) = LocateTask(dataRoot, taskFileStemOrId);
        if (!string.Equals(task.UpdatedAt, expectedUpdatedAt, StringComparison.Ordinal))
            throw new OloopConflictException(
                $"task {task.Id} changed since you read it (was {expectedUpdatedAt}, now {task.UpdatedAt}); re-read and retry");

        var now = NowUtc();
        var updated = new OloopTask
        {
            FormatVersion = task.FormatVersion,
            Id = task.Id,
            ProjectId = task.ProjectId,
            Type = task.Type, // not editable in V1
            Title = newTitle ?? task.Title,
            Status = task.Status,
            ImagePath = task.ImagePath,
            Labels = newLabels ?? task.Labels,
            CreatedAt = task.CreatedAt,
            CreatedBy = task.CreatedBy, // immutable
            UpdatedAt = now,
            UpdatedBy = actor,
            Comments = task.Comments,
            Description = newDescription ?? task.Description,
            ProjectName = task.ProjectName,
        };
        WriteWithBackup(path, RenderTask(updated));
        return updated;
    }

    // ---------------- Comment ----------------

    public static OloopTask AddComment(string dataRoot, string taskFileStemOrId, string body,
                                       string actor, string expectedUpdatedAt)
    {
        var (task, path) = LocateTask(dataRoot, taskFileStemOrId);
        if (!string.Equals(task.UpdatedAt, expectedUpdatedAt, StringComparison.Ordinal))
            throw new OloopConflictException(
                $"task {task.Id} changed since you read it (was {expectedUpdatedAt}, now {task.UpdatedAt}); re-read and retry");

        var now = NowUtc();
        var comments = task.Comments.Append(new TaskComment { Body = body, CreatedAt = now }).ToList();
        var updated = new OloopTask
        {
            FormatVersion = task.FormatVersion,
            Id = task.Id,
            ProjectId = task.ProjectId,
            Type = task.Type,
            Title = task.Title,
            Status = task.Status,
            ImagePath = task.ImagePath,
            Labels = task.Labels,
            CreatedAt = task.CreatedAt,
            CreatedBy = task.CreatedBy,
            UpdatedAt = now,
            UpdatedBy = actor,
            Comments = comments,
            Description = task.Description,
            ProjectName = task.ProjectName,
        };
        WriteWithBackup(path, RenderTask(updated));
        return updated;
    }

    // ---------------- Project ----------------

    public static OloopProject AddProject(string dataRoot, string name, string? referencePath,
                                          string? referenceHost, string actor)
    {
        var now = NowUtc();
        var project = new OloopProject
        {
            FormatVersion = OloopReader.MaxFormatVersion,
            Id = Guid.NewGuid().ToString(),
            Name = name,
            CreatedAt = now,
            ReferencePath = referencePath,
            ReferenceHost = referenceHost,
            Description = "",
            FolderName = Slugify(name),
        };

        var dir = Path.Combine(dataRoot, project.FolderName);
        Directory.CreateDirectory(dir);
        var projectPath = Path.Combine(dir, "project.md");
        AtomicWrite(projectPath, RenderProject(project));
        return project;
    }

    // ---------------- Render ----------------

    private static string RenderTask(OloopTask t)
    {
        var doc = new FrontmatterDocument();
        doc.Fields.Add(new("formatVersion", t.FormatVersion));
        doc.Fields.Add(new("id", t.Id));
        doc.Fields.Add(new("projectId", t.ProjectId));
        doc.Fields.Add(new("type", t.Type.ToString()));
        doc.Fields.Add(new("title", t.Title));
        doc.Fields.Add(new("status", t.Status.ToString()));
        doc.Fields.Add(new("imagePath", (object?)t.ImagePath));
        doc.Fields.Add(new("labels", new List<string>(t.Labels)));
        doc.Fields.Add(new("createdAt", t.CreatedAt));
        doc.Fields.Add(new("createdBy", t.CreatedBy));
        doc.Fields.Add(new("updatedAt", t.UpdatedAt));
        doc.Fields.Add(new("updatedBy", t.UpdatedBy));
        doc.Fields.Add(new("comments", t.Comments.Count == 0
            ? (object)new List<FrontmatterDocument>()
            : t.Comments.Select(c => {
                var cd = new FrontmatterDocument();
                cd.Fields.Add(new("body", (object)c.Body));
                cd.Fields.Add(new("createdAt", c.CreatedAt));
                return cd;
              }).ToList()));
        doc.Description = t.Description;
        return OloopYaml.Render(doc);
    }

    private static string RenderProject(OloopProject p)
    {
        var doc = new FrontmatterDocument();
        doc.Fields.Add(new("formatVersion", p.FormatVersion));
        doc.Fields.Add(new("id", p.Id));
        doc.Fields.Add(new("name", p.Name));
        doc.Fields.Add(new("createdAt", p.CreatedAt));
        if (p.ReferencePath is not null)
            doc.Fields.Add(new("referencePath", (object)p.ReferencePath));
        if (p.ReferenceHost is not null)
            doc.Fields.Add(new("referenceHost", p.ReferenceHost));
        doc.Description = p.Description;
        return OloopYaml.Render(doc);
    }

    // ---------------- I/O helpers ----------------

    private static (OloopTask Task, string Path) LocateTask(string dataRoot, string identifier)
    {
        foreach (var projectDir in Directory.GetDirectories(dataRoot)
                     .Where(d => !Path.GetFileName(d).StartsWith('.')))
        {
            var candidate = Path.Combine(projectDir, "tasks", identifier + ".md");
            if (File.Exists(candidate))
                return (OloopReader.ReadTask(candidate), candidate);
        }
        throw new OloopDataException($"task file '{identifier}.md' not found under data root");
    }

    private static string FindProjectDir(string dataRoot, string name)
    {
        foreach (var projectDir in Directory.GetDirectories(dataRoot)
                     .Where(d => !Path.GetFileName(d).StartsWith('.')))
        {
            var projectFile = Path.Combine(projectDir, "project.md");
            if (!File.Exists(projectFile)) continue;
            var project = OloopReader.ReadProject(projectFile, Path.GetFileName(projectDir));
            if (string.Equals(project.Name, name, StringComparison.OrdinalIgnoreCase))
                return projectDir;
        }
        throw new OloopDataException($"project '{name}' not found under data root");
    }

    private static string Slugify(string name)
    {
        var slug = name.Trim().ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c))
            .GroupBy(c => c)
            .Select(g => g.Key.ToString())
            .Aggregate(new StringBuilder(),
                (sb, s) => sb.Append(s.Length > 1 && s != sb.ToString().Suffix(1) ? $" {s}" : s))
            .ToString();
        // Simpler, robust slug: replace non-alphanumeric with '-', collapse repeats.
        slug = System.Text.RegularExpressions.Regex.Replace(name.Trim().ToLowerInvariant(),
            "[^a-z0-9]+", "-");
        return slug.Trim('-');
    }

    /// <summary>One-generation backup, then atomic replace (temp file + rename).</summary>
    private static void WriteWithBackup(string filePath, string content)
    {
        if (File.Exists(filePath))
        {
            var backupDir = Path.Combine(Path.GetDirectoryName(filePath)!, ".backup");
            Directory.CreateDirectory(backupDir);
            File.Copy(filePath, Path.Combine(backupDir, Path.GetFileName(filePath) + ".bak"), true);
        }
        AtomicWrite(filePath, content);
    }

    private static void AtomicWrite(string filePath, string content)
    {
        var tmp = filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tmp, content);
        File.Move(tmp, filePath, overwrite: true);
    }
}

public sealed class OloopConflictException : OloopDataException
{
    public OloopConflictException(string message) : base(message) { }
}
