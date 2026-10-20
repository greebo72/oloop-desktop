using OloopApp.Models;
using OloopApp.Services;

namespace OloopApp.Services;

/// <summary>
/// Reads an oloop data root and validates it against the schemas and the
/// conformance rules (SPEC.md). Refuses files with a newer
/// <c>formatVersion</c>, files whose name differs from <c>id</c>, and
/// structurally invalid frontmatter.
/// </summary>
public static class OloopReader
{
    /// <summary>Highest format version this reader understands.</summary>
    public const int MaxFormatVersion = 1;

    private static readonly Guid EmptyGuid = Guid.Empty;

    public sealed record LoadResult(IReadOnlyList<OloopProject> Projects, IReadOnlyList<OloopTask> Tasks);

    public static LoadResult Load(string dataRoot)
    {
        if (!Directory.Exists(dataRoot))
            throw new OloopDataException($"data root not found: {dataRoot}");

        var projects = new List<OloopProject>();
        var tasks = new List<OloopTask>();

        foreach (var projectDir in Directory.GetDirectories(dataRoot)
                     .Where(d => !Path.GetFileName(d).StartsWith('.')))
        {
            string folderName = Path.GetFileName(projectDir);
            string projectFile = Path.Combine(projectDir, "project.md");
            if (!File.Exists(projectFile))
                continue;

            var project = ReadProject(projectFile, folderName);
            projects.Add(project);

            string tasksDir = Path.Combine(projectDir, "tasks");
            if (Directory.Exists(tasksDir))
            {
                foreach (var taskFile in Directory.GetFiles(tasksDir, "*.md"))
                {
                    var task = ReadTask(taskFile);
                    task.ProjectName = project.Name;
                    tasks.Add(task);
                }
            }
        }

        // Cross-file rules and stats.
        var projectIds = projects.Select(p => p.Id).ToHashSet();
        foreach (var task in tasks)
        {
            if (!projectIds.Contains(task.ProjectId))
                throw new OloopDataException(
                    $"task {task.Id} references unknown projectId {task.ProjectId}");
        }

        foreach (var p in projects)
        {
            p.TaskCount = tasks.Count(t => t.ProjectId == p.Id);
            p.OpenTaskCount = tasks.Count(t => t.ProjectId == p.Id && t.Status == TaskStatus.open);
        }

        tasks = tasks.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        projects = projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return new LoadResult(projects, tasks);
    }

    // ---------------- Project ----------------

    public static OloopProject ReadProject(string filePath, string folderName)
    {
        string raw;
        try { raw = File.ReadAllText(filePath); }
        catch (Exception ex) { throw new OloopDataException($"cannot read {filePath}: {ex.Message}"); }

        var (fm, body) = OloopYaml.Split(raw);
        var doc = OloopYaml.Parse(fm!);
        var project = new OloopProject
        {
            FormatVersion = doc.GetInt("formatVersion") ?? 1,
            Id = doc.GetString("id") ?? "",
            Name = doc.GetString("name") ?? "",
            CreatedAt = doc.GetString("createdAt") ?? "",
            ReferencePath = doc.GetNullableString("referencePath"),
            ReferenceHost = doc.GetNullableString("referenceHost"),
            Description = body,
            FolderName = folderName,
        };

        if (project.FormatVersion > MaxFormatVersion)
            throw new OloopDataException($"project.md: formatVersion {project.FormatVersion} is newer than supported ({MaxFormatVersion})");
        if (project.Id.Length == 0)
            throw new OloopDataException($"project.md: missing required field 'id'");
        if (!Guid.TryParse(project.Id, out _))
            throw new OloopDataException($"project.md: id '{project.Id}' is not a UUID");
        if (project.Name.Length == 0)
            throw new OloopDataException("project.md: missing required field 'name'");
        if (project.CreatedAt.Length == 0)
            throw new OloopDataException("project.md: missing required field 'createdAt'");
        if (!IsIso8601Z(project.CreatedAt))
            throw new OloopDataException($"project.md: createdAt '{project.CreatedAt}' is not ISO 8601 UTC (…Z)");
        if (project.ReferencePath is not null && project.ReferencePath.Length == 0)
            throw new OloopDataException("project.md: referencePath must not be empty");
        if (project.ReferenceHost is not null && project.ReferenceHost.Length == 0)
            throw new OloopDataException("project.md: referenceHost must not be empty");

        return project;
    }

    // ---------------- Task ----------------

    public static OloopTask ReadTask(string filePath)
    {
        string raw;
        try { raw = File.ReadAllText(filePath); }
        catch (Exception ex) { throw new OloopDataException($"cannot read {filePath}: {ex.Message}"); }

        string fileStem = Path.GetFileNameWithoutExtension(filePath);

        var (fm, body) = OloopYaml.Split(raw);
        var doc = OloopYaml.Parse(fm!);

        string id = doc.GetString("id") ?? "";
        if (string.Equals(id, fileStem, StringComparison.OrdinalIgnoreCase))
        {
            // fine
        }
        else if (id.Length == 0)
        {
            throw new OloopDataException($"task file '{fileStem}.md': missing required field 'id'");
        }
        else
        {
            throw new OloopDataException(
                $"task file name '{fileStem}' does not match frontmatter id '{id}'");
        }

        var task = new OloopTask
        {
            FormatVersion = doc.GetInt("formatVersion") ?? 1,
            Id = id,
            ProjectId = doc.GetString("projectId") ?? "",
            Type = ParseTaskType(doc.GetString("type"), fileStem),
            Title = doc.GetString("title") ?? "",
            Status = ParseTaskStatus(doc.GetString("status"), fileStem),
            ImagePath = doc.GetNullableString("imagePath"),
            Labels = doc.GetStringList("label" + "s") ?? Array.Empty<string>(),
            CreatedAt = doc.GetString("createdAt") ?? "",
            CreatedBy = doc.GetString("createdBy") ?? "",
            UpdatedAt = doc.GetString("updatedAt") ?? "",
            UpdatedBy = doc.GetString("updatedBy") ?? "",
            Comments = (doc.GetObjectList("comments") ?? Array.Empty<FrontmatterDocument>())
                .Select(c => new TaskComment
                {
                    Body = c.GetString("body") ?? "",
                    CreatedAt = c.GetString("createdAt") ?? "",
                }).ToList(),
            Description = body,
        };

        if (task.FormatVersion > MaxFormatVersion)
            throw new OloopDataException($"task {fileStem}: formatVersion {task.FormatVersion} is newer than supported ({MaxFormatVersion})");
        if (!Guid.TryParse(task.ProjectId, out _))
            throw new OloopDataException($"task {fileStem}: projectId '{task.ProjectId}' is not a UUID");
        if (task.Title.Length == 0)
            throw new OloopDataException($"task {fileStem}: missing required field 'title'");
        if (task.Title.Length > 300)
            throw new OloopDataException($"task {fileStem}: title exceeds 300 characters");
        if (task.CreatedAt.Length == 0)
            throw new OloopDataException($"task {fileStem}: missing required field 'createdAt'");
        if (task.CreatedBy.Length == 0)
            throw new OloopDataException($"task {fileStem}: missing required field 'createdBy'");
        if (task.UpdatedAt.Length == 0)
            throw new OloopDataException($"task {fileStem}: missing required field 'updatedAt'");
        if (task.UpdatedBy.Length == 0)
            throw new OloopDataException($"task {fileStem}: missing required field 'updatedBy'");
        if (!IsIso8601Z(task.CreatedAt))
            throw new OloopDataException($"task {fileStem}: createdAt '{task.CreatedAt}' is not ISO 8601 UTC (…Z)");
        if (!IsIso8601Z(task.UpdatedAt))
            throw new OloopDataException($"task {fileStem}: updatedAt '{task.UpdatedAt}' is not ISO 8601 UTC (…Z)");

        return task;
    }

    private static TaskType ParseTaskType(string? value, string fileStem)
    {
        if (value is null || value.Length == 0)
            throw new OloopDataException($"task {fileStem}: missing required field 'type'");
        if (Enum.TryParse<TaskType>(value, true, out var type))
            return type;
        throw new OloopDataException($"task {fileStem}: unknown type '{value}' (expected todo|question|decision|feature|bug)");
    }

    private static TaskStatus ParseTaskStatus(string? value, string fileStem)
    {
        if (value is null || value.Length == 0)
            throw new OloopDataException($"task {fileStem}: missing required field 'status'");
        if (Enum.TryParse<TaskStatus>(value, true, out var status))
            return status;
        throw new OloopDataException($"task {fileStem}: unknown status '{value}' (expected open|closed)");
    }

    private static bool IsIso8601Z(string value)
    {
        if (!value.EndsWith("Z", StringComparison.Ordinal))
            return false;
        return DateTimeOffset.TryParse(value, out _);
    }
}
