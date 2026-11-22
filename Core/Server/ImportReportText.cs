using System.Text;

namespace OloopApp.Core.Server;

/// <summary>
/// The plan and the report of an import as plain text, for the dialog and for saving
/// (docs/SPEC-import-folder.md, "The report"). Ids and titles only: no keys, no tokens and nothing of
/// the files beyond the title.
/// </summary>
public static class ImportReportText
{
    public static string ReasonText(SkipReason reason) => reason switch
    {
        SkipReason.Exists => "exists on the server",
        SkipReason.ExistsElsewhere => "exists on the server in another project",
        SkipReason.HasImage => "has an image",
        SkipReason.TooLarge => "too large for the server",
        _ => reason.ToString(),
    };

    public static string ActionText(ImportProjectPlan plan) => plan.Action switch
    {
        ImportAction.Create => "Create",
        ImportAction.AddMissing => "Add missing tasks",
        ImportAction.Conflict => $"Same name on the server ({plan.ConflictWith}): tick to add the tasks to that project",
        _ => "Nothing to do",
    };

    public static string Plan(string folder, ImportPlan plan)
    {
        var text = new StringBuilder();
        text.AppendLine($"Import plan for {folder}");
        text.AppendLine();
        foreach (var project in plan.Projects)
        {
            text.AppendLine($"{project.Project.Name} ({project.Project.Id}): {ActionText(project)}");
            text.AppendLine($"  {project.TaskCount} tasks in the folder, {project.ToWrite.Count} to write, {project.Skipped.Count} skipped");
            AppendSkipped(text, project.Skipped);
        }

        AppendUnreadable(text, plan);
        text.AppendLine();
        text.AppendLine($"Chosen: {plan.ProjectsToWrite} projects, {plan.TasksToWrite} tasks.");
        return text.ToString();
    }

    public static string Report(string folder, ImportPlan plan, ImportReport report)
    {
        var text = new StringBuilder();
        text.AppendLine($"Import of {folder}{(report.Cancelled ? " (cancelled)" : "")}");
        text.AppendLine();
        foreach (var project in report.Projects)
        {
            text.AppendLine($"{project.Name} ({project.ProjectId})");
            if (project.Action == ImportAction.Create)
                text.AppendLine(project.Created ? "  project created" : $"  project NOT created: {project.ProjectError}");
            else if (project.Action == ImportAction.Conflict)
                text.AppendLine("  tasks added to the project of the server with the same name");
            text.AppendLine($"  {project.Written} tasks written, {project.AlreadyThere} were already there, {project.Failed.Count} failed, {project.Skipped.Count} skipped");
            foreach (var failed in project.Failed)
                text.AppendLine($"  FAILED {failed.TaskId} {failed.Title}: {failed.Reason}");
            AppendSkipped(text, project.Skipped);
        }

        foreach (var other in plan.Projects.Where(p => !p.HasWork || !p.Selected))
        {
            if (report.Projects.Any(p => p.ProjectId == other.Project.Id))
                continue;
            text.AppendLine($"{other.Project.Name} ({other.Project.Id}): not written ({(other.HasWork ? "not chosen" : ActionText(other))})");
            AppendSkipped(text, other.Skipped);
        }

        AppendUnreadable(text, plan);
        text.AppendLine();
        text.AppendLine($"Total: {report.Written} tasks written, {report.Failed} failures.");
        text.AppendLine("The folder was not changed.");
        text.AppendLine("Agents do not see the imported projects yet: give them to an agent in Settings, Security, Manage agents.");
        return text.ToString();
    }

    private static void AppendSkipped(StringBuilder text, IReadOnlyList<SkippedTask> skipped)
    {
        foreach (var group in skipped.GroupBy(s => s.Reason))
        {
            text.AppendLine($"  skipped, {ReasonText(group.Key)}: {group.Count()}");
            foreach (var task in group.Where(t => t.Reason != SkipReason.Exists))
                text.AppendLine($"    {task.TaskId} {task.Title}{(task.Detail is null ? "" : $" ({task.Detail})")}");
        }
    }

    private static void AppendUnreadable(StringBuilder text, ImportPlan plan)
    {
        if (plan.Unreadable.Count == 0)
            return;

        text.AppendLine();
        text.AppendLine("Files that could not be read, not imported:");
        foreach (var file in plan.Unreadable)
            text.AppendLine($"  {file.Path}: {file.Reason}");
    }
}
