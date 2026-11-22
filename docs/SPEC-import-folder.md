# SPEC: Import a data folder into the server

**Status:** draft (2026-11-22). Builds on `spec/SPEC.md` (the folder format) and `spec/SERVER.md` (documents, keys), which stay authoritative. This document only says what the desktop app has to do.

## Purpose

oloop worked on folders of Markdown files first. A user who moves to the server backend has projects in such folders and wants them on the server with their ids, times, authors and comments intact. Today the app cannot do that: `CreateProjectAsync` always makes a new project id, and a task file names its project in `projectId`, so copying tasks into a new project means rewriting every file.

The import reads a folder and writes its projects and tasks to the server **as the user**, with the user's key. Only a client that holds the key can do this: the server sees ciphertext.

## Scope

In scope, server backend only:

1. Choose a folder in the format of `SPEC.md` and show what an import would do (**the plan**).
2. Write the projects and tasks that the plan says are new.
3. Show a report.

Not in scope:

- **Images.** A task with an `imagePath` is not imported and is listed in the plan and the report. Importing blobs is a feature of its own; skipping the task and saying so is better than dropping the picture silently.
- Merging two versions of one task. A task id that the server has already is never overwritten.
- Importing from the server into a folder, or from folder to folder.
- Handing the imported projects to an agent. That is `SPEC-agent-grant-projects.md`; the report points to it.
- The CLI. It has no user login and no secret key.

## Preconditions

- The app is signed in to a server and the user key is unlocked, as for `SPEC-agent-management.md`. Without it the entry is hidden or disabled.
- The folder is read only. The import **never writes to the source**: no backup, no changed file, no lock file.

## What is read

The folder is read with `FileOloopStore(folder).LoadAsync()`, the same code the folder backend uses, so the rules of `SPEC.md` apply unchanged. That means:

- A file without `formatVersion` is read as version 1; a higher version is refused (`TaskIo.Read`, `ProjectIo.Read`).
- `.backup/` and anything else that is not `project.md` or `tasks/*.md` is ignored.
- A file that cannot be read does not stop the read. It is listed in the plan as "unreadable" with the reason, and not imported.
- The `projectId` of a task has to match its project (the existing check of `TaskIo.Read`).

What is written is the **read object**, written back with `ProjectIo.Write` and `TaskIo.Write`: the key order and quoting may differ from the source file, the content does not. Kept as they are: `id`, `projectId`, `createdAt`, `createdBy`, `updatedAt`, `updatedBy`, `type`, `status`, `labels`, `dueDate`, the comments, the description, and fields the app does not know (`SPEC.md`: unknown fields are preserved).

A project gets `formatVersion: 1` when the source had none.

## The plan

The plan is made **before** anything is written, from the source folder and the current state of the server (`ServerOloopStore.LoadAsync()`, which decrypts what the user can read). It is shown in a dialog and can be saved as text.

One row per project in the folder:

| Column | Meaning |
| --- | --- |
| Project | Name from `project.md`. |
| Id | The project id. |
| Tasks | Number of tasks in the folder. |
| Action | See the table below. |
| New | Number of tasks that would be written. |
| Skipped | Number of tasks that would not, with a link to the reasons. |

Action per project:

| Situation | Action |
| --- | --- |
| No project with this id and none with this name (compared without regard to case) on the server | **Create** the project with this id, then write its tasks. |
| A project with this id is on the server | **Add missing**: write the tasks whose id the server does not have; the project is left as it is. |
| No project with this id, but one with the same name | **Conflict**: nothing is written for this project. The plan names the server project. The user renames one of the two and runs the plan again. |

Skipped tasks, with the reason shown in the plan and the report:

| Reason | Rule |
| --- | --- |
| Exists | A task with this id is on the server, in this project. It is not compared and not changed. |
| Exists elsewhere | A task with this id is on the server in **another** project. This happens when a project was imported earlier under a new id. The plan names that project. Document ids are global (`SERVER.md`), so writing it would fail. |
| Has an image | `imagePath` is not `null`. |
| Too large | The text is larger than the server accepts (`MAX_DOCUMENT_BYTES`, 256 KiB by default; the plan uses the limit the server reports if it does, otherwise the default). |
| Unreadable | The file did not parse; the reason of the parser is shown. |

The user can untick projects in the plan. A project that is unticked is not written. The button "Import" is enabled when at least one project has something to write, and says how many projects and tasks.

An empty plan (everything exists) says so and offers only "Close".

## The import

Steps on "Import", in this order, projects one after the other, tasks of a project one after the other. Serial writes are enough: the data is small (hundreds of tasks) and the order keeps the report simple.

For a project to **create**:

1. `ServerOloopStore.CreateProjectAsync(OloopProject project)`: a new overload that writes the **given** project instead of making one. It keeps `id`, `createdAt`, `referencePath` and `referenceHost`, makes the project key (key version 1) and hands it to the user exactly as the existing overload does, and refuses a name or id that exists. The existing overload (name only) stays and uses it.
2. If this fails, the tasks of the project are not written and the project is reported as failed.

For each task to **write**:

3. `WriteTaskAsync(project, task, expectedUpdatedAt: null)`. The id stays. A `409` ("a task with this id exists already") is not an error of the import: another client wrote it in the meantime; the task is counted as "Exists".
4. Any other failure is recorded with the task id, title and reason, and the import goes on with the next task. A failed project creation is the only reason to skip the rest of a project.

Cancelling stops after the current request. What was written stays. The report says "cancelled" and the next run of the plan shows the rest, because the plan skips what exists.

### Run it again

The import is **resumable and idempotent**: the plan of a second run skips every task that the first run wrote, and creates nothing twice, because ids are kept and a project id on the server turns "Create" into "Add missing". After a failure, fix the cause and run it again.

## The report

Shown when the import ends, and can be saved as text:

- Per project: created or not, tasks written, tasks skipped by reason, tasks failed with id, title and reason.
- The totals.
- A note that the source folder was not changed.
- A note that the projects are not yet visible to agents, with the pointer to "Projects…" in the agents list (`SPEC-agent-grant-projects.md`).

The report contains task ids and titles, no keys, no tokens and no file contents beyond the title.

## Entry in the app

Settings, tab "Data" (next to the choice of the backend): the button "Import folder…". It opens a folder chooser, then the plan. The button is disabled while the keys are locked, and for the folder backend.

## Code

| Part | Where | Notes |
| --- | --- | --- |
| Project with a given id | `Core/Server/ServerOloopStore.cs` | Overload `CreateProjectAsync(OloopProject, CancellationToken)`; the name-only overload calls it. |
| Plan and run | new `Core/Server/FolderImport.cs` | `PlanAsync(sourceFolder, targetStore)` returns the rows, the skipped tasks with reasons; `RunAsync(plan, progress, ct)` writes and returns the report. No UI types. |
| Size limit | `Core/Server/ServerApi.cs` or the store | Where the server limit is read from, if it exposes one; otherwise the constant 256 KiB. |
| View models | new `ViewModels/ImportFolderViewModel.cs` | The plan dialog, the progress, the report. |
| Window services | `ViewModels/IAgentUi.cs` or a new `IImportUi` | The folder chooser and the save-as for the report. |
| View | new `Views/ImportFolderDialog.axaml` | A table for the plan, a progress bar, a report text. |
| Settings | `Views/SettingsDialog.axaml` | The button. |

`FolderImport` talks to `IOloopStore` for the source and to `ServerOloopStore` for the target, so it can be tested with a folder in a temp directory and `FakeOloopServer`.

## Tests

Unit tests in `OloopApp.Tests`, with a temp folder as source and `FakeOloopServer` as target:

1. **New project.** A project with three tasks: plan says Create and 3 new; after the run the server has the project **with the same id** and the three tasks with their ids, `createdAt`, `createdBy`, `updatedAt`, labels and comments unchanged.
2. **Run twice.** The second plan has nothing to write, and the second run changes nothing.
3. **Add missing.** The project exists with two of three tasks: only the third is written.
4. **Exists elsewhere.** A task id that the server has in another project is skipped with that project named; no write is tried.
5. **Name conflict.** Another project id with the same name (also in another case) gives "Conflict" and writes nothing.
6. **Image.** A task with `imagePath` is skipped with the reason; the other tasks of the project are written.
7. **Legacy files.** A project and a task without `formatVersion` are imported and read back as version 1.
8. **Unreadable file.** A broken task file is listed with its reason and does not stop the others.
9. **Too large.** A task over the limit is skipped; no request is sent for it.
10. **Failure in the middle.** The server refuses the second task: it is in the report, the third is still written, a later run writes the second.
11. **Failed project.** `CreateProjectAsync` fails: no task of that project is tried.
12. **409 on a task.** Counted as "Exists", not as a failure.
13. **Source untouched.** The files of the source folder are byte for byte the same after the plan and after the run.
14. **Plan makes no write.** The plan sends no `PUT`.
15. **View model.** "Import" disabled with nothing to write, unticking a project changes the counts, cancelling stops after the current task.

## Security notes

- The import runs with the user's key. It must not be offered while the keys are locked and must not cache the project keys after the run.
- The source folder is untrusted input: task files may have been written by someone else. They are read with the normal readers, which refuse what `SPEC.md` forbids, and shown as text only.
- Titles in the plan and the report are data; they never become part of a command, a path or a URL.

## Open questions

1. **Project names that differ.** The task folder `oloop-cli` holds a project named `lagebericht`. The import goes by the name in `project.md`, not by the folder name. Proposal: show both in the plan, so the user can see why a folder has another name.
2. **Importing into an existing project of another id.** The server already has a project (`oloop-desktop`) with the same tasks as a folder (`OloopApp`) under another project id. "Exists elsewhere" skips all its tasks. Whether to offer "move the tasks to the project of the folder" is open; it would need a new revision of each task with a changed `projectId`, which the server forbids (`SERVER.md`). Proposal: not supported; the user keeps the server project.
3. **Images.** A later version could upload the blob after the task is written (`SERVER.md`, "Blobs": an image is added to a task that exists). Proposal: its own spec.
4. **Many folders.** The app supports one data root at a time. Importing several folders is several runs. Proposal: enough for the first version.
