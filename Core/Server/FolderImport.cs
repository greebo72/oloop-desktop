using System.Text;
using OloopApp.Core.Crypto;
using OloopApp.Core.Io;
using OloopApp.Core.Models;

namespace OloopApp.Core.Server;

/// <summary>What the import would do with one project of the folder.</summary>
public enum ImportAction
{
    /// <summary>The server has no such project: write it with its id, then its tasks.</summary>
    Create,

    /// <summary>The server has the project: write the tasks it lacks.</summary>
    AddMissing,

    /// <summary>The server has a project of the same name under another id. Nothing is written.</summary>
    Conflict,

    /// <summary>There is nothing to write: every task exists or has to be skipped.</summary>
    NothingToDo,
}

/// <summary>Why a task is not imported.</summary>
public enum SkipReason
{
    /// <summary>The server has a task with this id, in this project.</summary>
    Exists,

    /// <summary>The server has a task with this id, in another project.</summary>
    ExistsElsewhere,

    /// <summary>The task has an image, which the import does not carry over.</summary>
    HasImage,

    /// <summary>The task is larger than the server accepts.</summary>
    TooLarge,
}

public sealed record SkippedTask(string TaskId, string Title, SkipReason Reason, string? Detail = null);

/// <summary>One project of the folder, with what the import would do.</summary>
public sealed class ImportProjectPlan
{
    public required OloopProject Project { get; init; }
    public required ImportAction Action { get; init; }

    /// <summary>All tasks the folder has for this project.</summary>
    public required int TaskCount { get; init; }

    public required IReadOnlyList<OloopTask> ToWrite { get; init; }
    public required IReadOnlyList<SkippedTask> Skipped { get; init; }

    /// <summary>For a conflict: the project of the server that has the name.</summary>
    public string? ConflictWith { get; init; }

    /// <summary>The user can untick a project; it is then not written.</summary>
    public bool Selected { get; set; } = true;

    public bool HasWork => Action is ImportAction.Create or ImportAction.AddMissing;
}

/// <summary>The plan of an import: made before anything is written.</summary>
public sealed class ImportPlan
{
    public required IReadOnlyList<ImportProjectPlan> Projects { get; init; }

    /// <summary>Files of the folder that could not be read, and so are not imported.</summary>
    public required IReadOnlyList<UnreadableFile> Unreadable { get; init; }

    /// <summary>The projects the import would write: selected, and with something to do.</summary>
    public IEnumerable<ImportProjectPlan> Chosen => Projects.Where(p => p is { Selected: true, HasWork: true });

    public int ProjectsToWrite => Chosen.Count();
    public int TasksToWrite => Chosen.Sum(p => p.ToWrite.Count);
}

public sealed record FailedTask(string TaskId, string Title, string Reason);

/// <summary>What happened to one project.</summary>
public sealed class ImportProjectReport
{
    public required string ProjectId { get; init; }
    public required string Name { get; init; }
    public required ImportAction Action { get; init; }
    public bool Created { get; set; }
    public string? ProjectError { get; set; }
    public int Written { get; set; }

    /// <summary>Tasks that another client wrote while the import ran.</summary>
    public int AlreadyThere { get; set; }

    public List<FailedTask> Failed { get; } = [];
    public required IReadOnlyList<SkippedTask> Skipped { get; init; }
}

public sealed class ImportReport
{
    public List<ImportProjectReport> Projects { get; } = [];
    public bool Cancelled { get; set; }
    public int Written => Projects.Sum(p => p.Written);
    public int Failed => Projects.Sum(p => p.Failed.Count) + Projects.Count(p => p.ProjectError is not null);
}

/// <param name="Project">The project being written.</param>
/// <param name="Done">Tasks handled so far in the whole import.</param>
/// <param name="Total">Tasks to be written in the whole import.</param>
public sealed record ImportProgress(string Project, int Done, int Total);

/// <summary>
/// Imports a data folder (the folder format of SPEC.md) into the server as the signed-in user
/// (docs/SPEC-import-folder.md). The folder is only read. Ids, times, authors and comments stay,
/// so an import can be run again: what exists is skipped.
/// </summary>
public static class FolderImport
{
    /// <summary>The limit of the server for one document (<c>max_document_bytes</c>).</summary>
    public const int DefaultMaxDocumentBytes = 256 * 1024;

    // The envelope adds a nonce and a tag to the text.
    private const int EnvelopeOverhead = 64;

    /// <summary>
    /// Reads the folder and the server and says what an import would do. Writes nothing.
    /// </summary>
    public static async Task<ImportPlan> PlanAsync(
        string sourceFolder,
        ServerOloopStore target,
        int maxDocumentBytes = DefaultMaxDocumentBytes,
        CancellationToken ct = default)
    {
        var source = new FileOloopStore(sourceFolder);
        if (source.CheckAvailable() is { } missing)
            throw new OloopFormatException(missing);

        var folder = await source.LoadAsync(ct).ConfigureAwait(false);
        var server = await target.LoadAsync(ct).ConfigureAwait(false);

        var serverProjectById = server.Projects.ToDictionary(p => p.Id, StringComparer.OrdinalIgnoreCase);
        var serverTaskById = server.Tasks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        var plans = new List<ImportProjectPlan>();
        foreach (var project in folder.Projects)
        {
            var tasks = folder.Tasks.Where(t => string.Equals(t.ProjectId, project.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            var toWrite = new List<OloopTask>();
            var skipped = new List<SkippedTask>();

            foreach (var task in tasks)
            {
                if (serverTaskById.TryGetValue(task.Id, out var there))
                {
                    if (string.Equals(there.ProjectId, project.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        skipped.Add(new SkippedTask(task.Id, task.Title, SkipReason.Exists));
                    }
                    else
                    {
                        var elsewhere = serverProjectById.TryGetValue(there.ProjectId, out var other)
                            ? $"{other.Name} ({other.Id})"
                            : there.ProjectId;
                        skipped.Add(new SkippedTask(task.Id, task.Title, SkipReason.ExistsElsewhere, elsewhere));
                    }
                }
                else if (task.ImagePath is not null)
                {
                    skipped.Add(new SkippedTask(task.Id, task.Title, SkipReason.HasImage, task.ImagePath));
                }
                else if (Encoding.UTF8.GetByteCount(TaskIo.Write(task)) + EnvelopeOverhead > maxDocumentBytes)
                {
                    skipped.Add(new SkippedTask(task.Id, task.Title, SkipReason.TooLarge));
                }
                else
                {
                    toWrite.Add(task);
                }
            }

            ImportAction action;
            string? conflictWith = null;
            if (serverProjectById.ContainsKey(project.Id))
            {
                action = toWrite.Count > 0 ? ImportAction.AddMissing : ImportAction.NothingToDo;
            }
            else if (server.Projects.FirstOrDefault(p =>
                         string.Equals(p.Name, project.Name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } sameName)
            {
                action = ImportAction.Conflict;
                conflictWith = $"{sameName.Name} ({sameName.Id})";
                toWrite.Clear();
            }
            else
            {
                // A project whose tasks all exist elsewhere is not made empty: the tasks are there.
                action = tasks.Count == 0 || toWrite.Count > 0 ? ImportAction.Create : ImportAction.NothingToDo;
            }

            plans.Add(new ImportProjectPlan
            {
                Project = project,
                Action = action,
                TaskCount = tasks.Count,
                ToWrite = toWrite,
                Skipped = skipped,
                ConflictWith = conflictWith,
                Selected = action is ImportAction.Create or ImportAction.AddMissing,
            });
        }

        return new ImportPlan { Projects = plans, Unreadable = folder.Unreadable };
    }

    /// <summary>
    /// Writes what the plan says, project after project and task after task. A failure of one task
    /// does not stop the others; a project that could not be made gets none of its tasks. Cancelling
    /// stops after the current request and returns the report so far.
    /// </summary>
    public static async Task<ImportReport> RunAsync(
        ImportPlan plan,
        ServerOloopStore target,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var report = new ImportReport();
        var total = plan.TasksToWrite;
        var done = 0;

        foreach (var item in plan.Chosen.ToList())
        {
            var entry = new ImportProjectReport
            {
                ProjectId = item.Project.Id,
                Name = item.Project.Name,
                Action = item.Action,
                Skipped = item.Skipped,
            };
            report.Projects.Add(entry);

            if (ct.IsCancellationRequested)
            {
                report.Cancelled = true;
                return report;
            }

            if (item.Action == ImportAction.Create)
            {
                try
                {
                    await target.CreateProjectAsync(item.Project, ct).ConfigureAwait(false);
                    entry.Created = true;
                }
                catch (OperationCanceledException)
                {
                    report.Cancelled = true;
                    return report;
                }
                catch (Exception ex) when (IsFailureOfAWrite(ex))
                {
                    entry.ProjectError = ex.Message;
                    done += item.ToWrite.Count;
                    continue;
                }
            }

            foreach (var task in item.ToWrite)
            {
                if (ct.IsCancellationRequested)
                {
                    report.Cancelled = true;
                    return report;
                }

                progress?.Report(new ImportProgress(item.Project.Name, done, total));
                try
                {
                    await target.WriteTaskAsync(item.Project, task, expectedUpdatedAt: null, ct).ConfigureAwait(false);
                    entry.Written++;
                }
                catch (OperationCanceledException)
                {
                    report.Cancelled = true;
                    return report;
                }
                catch (OloopFormatException ex) when (ex.InnerException is ServerApiException { IsConflict: true })
                {
                    // Another client wrote it between the plan and now: it is there, which is the goal.
                    entry.AlreadyThere++;
                }
                catch (Exception ex) when (IsFailureOfAWrite(ex))
                {
                    entry.Failed.Add(new FailedTask(task.Id, task.Title, ex.Message));
                }

                done++;
            }
        }

        progress?.Report(new ImportProgress("", total, total));
        return report;
    }

    private static bool IsFailureOfAWrite(Exception ex)
        => ex is IOException or OloopFormatException or OloopCryptoException or UnauthorizedAccessException;
}
