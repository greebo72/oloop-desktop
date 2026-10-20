# UI contract (Schritt 3–6)

Shared by the parallel implementation streams. Everything in here is binding:
if a stream needs something that is not defined here, it adds it itself and
mentions it in the report — but it must not change an existing signature that
another stream already coded against.

> **Status 2026-10-20:** The three parallel streams are done and merged into
> `master` (commit `bf8e2cd`). The branches `feature/stream-a-tasklist`,
> `feature/stream-b-settings` and `feature/stream-c-dialogs` and their
> worktrees under `.claude/worktrees/` have been deleted — their content is in
> `master`. The table below is kept as a record of who owned what; the
> ownership rules apply again to the next round of parallel streams.

## File ownership

| Stream | Owns (may create/modify) | Must not touch |
|---|---|---|
| A — Task list + detail | `ViewModels/TaskListViewModel.cs`, `ViewModels/TaskGroupViewModel.cs`, `ViewModels/TaskDetailViewModel.cs`, `ViewModels/CommentViewModel.cs`, `Views/TaskListView.axaml(.cs)`, `Views/TaskDetailView.axaml(.cs)`, `Views/ProjectSplitView.axaml(.cs)`, `Controls/MarkdownRenderer.cs`, `Controls/CodeBlockView.axaml(.cs)`, `Views/MainWindow.axaml`, `ViewModels/ProjectSplitViewModel.cs` | anything of B and C |
| B — Settings + new project | `ViewModels/SettingsViewModel.cs`, `ViewModels/NewProjectViewModel.cs`, `Views/SettingsDialog.axaml(.cs)`, `Views/NewProjectDialog.axaml(.cs)`, `Core/Io/OloopSettings.cs` | `Views/MainWindow.axaml`, `ViewModels/MainViewModel.cs` |
| C — Task dialogs | `ViewModels/NewTaskViewModel.cs`, `ViewModels/EditTaskViewModel.cs`, `ViewModels/ConflictViewModel.cs`, `Views/NewTaskDialog.axaml(.cs)`, `Views/EditTaskDialog.axaml(.cs)`, `Views/ConflictDialog.axaml(.cs)` | `Views/MainWindow.axaml`, `ViewModels/MainViewModel.cs` |

`ViewModels/MainViewModel.cs`, `ViewModels/TaskRowViewModel.cs`,
`Views/DashboardView.axaml` and everything under `Core/` are already
committed and shared. `MainViewModel.cs` and the wiring of the dialogs into
the shell are **not** owned by any stream — the integrator does that after the
merge.

## Model types (already committed)

`OloopApp.Core.Models`:

- `OloopTask` — `Id`, `ProjectId`, `Type` (`TaskType`), `Title`, `Status`
  (`TaskStatus`), `ImagePath`, `Labels` (`IReadOnlyList<string>`), `CreatedAt`,
  `CreatedBy`, `UpdatedAt`, `UpdatedBy`, `Comments` (`IReadOnlyList<TaskComment>`),
  `Description` (Markdown), `ProjectName`, `IsClosed`, `With(...)`.
- `OloopProject` — `Id`, `Name`, `CreatedAt`, `ReferencePath`, `ReferenceHost`,
  `FolderName`, `TaskCount`, `OpenTaskCount`, `LatestUpdatedAt`.
- `TaskComment` — `Body`, `CreatedAt`.
- `TaskType` — `todo | question | decision | feature | bug`, with
  `TaskTypeExtensions.DisplayName()`, `.ClosedLabel()` (Done/Answered/Decided/
  Implemented/Fixed) and `.OpenLabel()` ("Open").

`OloopApp.Core.Grouping`:

- `TaskGrouping.Group(tasks, GroupingMode, now?, zone?)` → `IReadOnlyList<TaskGroup>`
  where `TaskGroup` is `(string? Title, IReadOnlyList<OloopTask> Tasks)`.
  `GroupingMode` = `None | Created | Modified | Type | Status`.
- `RelativeAge.Format(timestamp, now?)` → `"just now" | "5 min" | "3 h" | "1 day" | "12 days" | "4 months" | "2 years"`.

`OloopApp.Core.Filters`:

- `TaskFilter` — `SearchText`, `StaleDays` (double?), `Type`, `Status`, `Label`,
  `ShowClosed`, `Apply(tasks, now?)`.

`OloopApp.Core.Io`:

- `OloopStore(root)` — `LoadAsync(ct)`, `CreateTaskAsync(project, type, title,
  description, labels, actor, ct)`, `EditTaskAsync(task, project, title,
  description, labels, actor, ct)`, `SetStatusAsync(task, project, closed, actor, ct)`,
  `AddCommentAsync(task, project, body, actor, ct)`, `CreateProjectAsync(name,
  referencePath, referenceHost, ct)`.
- `OloopSettings` — `DataRoot`, `Actor`, `ShowClosed`, `Grouping`, `StaleDays`,
  `ResolveDataRoot()`, `ResolveActor()`, `Load()`, `Save()`, `DefaultPath`.

## View models the streams must provide

### Stream A

```csharp
public partial class TaskListViewModel : ViewModelBase
{
    public TaskListViewModel(OloopProject project, MainViewModel shell);
    public OloopProject Project { get; }
    public MainViewModel Shell { get; }
    public string Name { get; }                       // project name
    public int OpenCount { get; }                     // open tasks after filtering
    public int TotalCount { get; }                    // tasks before filtering
    public ObservableCollection<TaskGroupViewModel> Groups { get; }
    public TaskDetailViewModel? Selected { get; set; }
    public GroupingMode Grouping { get; set; }
    public bool ShowClosed { get; set; }
    public bool IsEmpty { get; }
    public void Refresh();                            // re-applies filter + grouping
}

public partial class TaskGroupViewModel : ViewModelBase
{
    public string Title { get; }                      // null when grouping is None
    public bool HasTitle { get; }
    public ObservableCollection<TaskRowViewModel> Rows { get; }
}

public partial class TaskDetailViewModel : ViewModelBase
{
    public TaskDetailViewModel(OloopTask task, OloopProject project, MainViewModel shell);
    public OloopTask Task { get; }
    public string Title { get; }
    public string TypeName { get; }                   // "Todo", "Question", ...
    public string StatusLabel { get; }                // "Open" / "Done" / ...
    public bool IsClosed { get; }
    public string IconKey { get; }                    // theme resource key
    public string BackgroundKey { get; }              // theme resource key
    public string ForegroundKey { get; }              // theme resource key
    public string LabelsText { get; }                 // "a, b" (empty when none)
    public bool HasLabels { get; }
    public string Description { get; }                // raw Markdown
    public Control DescriptionView { get; }           // rendered Markdown tree
    public bool HasDescription { get; }
    public string CreatedText { get; }                // "Created 2 h ago by timo"
    public string UpdatedText { get; }                // "Updated 2 h ago by timo"
    public ObservableCollection<CommentViewModel> Comments { get; }
    public bool HasComments { get; }
}

public partial class CommentViewModel : ViewModelBase
{
    public CommentViewModel(TaskComment comment);
    public string Body { get; }
    public string CreatedText { get; }                // "2 h ago"
}
```

`TaskRowViewModel` is already committed (see `ViewModels/TaskRowViewModel.cs`)
and carries `Task`, `Title`, `TypeName`, `Age`, `LabelsText`, `HasLabels`,
`IsClosed`, `StatusLabel`, `IconKey`, `BackgroundKey`, `ForegroundKey`.

### Stream B

```csharp
public partial class SettingsViewModel : ViewModelBase
{
    public SettingsViewModel(OloopSettings settings);
    public string DataRoot { get; set; }
    public string Actor { get; set; }
    public bool ShowClosed { get; set; }
    public GroupingMode Grouping { get; set; }
    public double? StaleDays { get; set; }
    public bool CanSave { get; }
    public string? Error { get; }                     // set when the root is unusable
    public void Save();
}

public partial class NewProjectViewModel : ViewModelBase
{
    public NewProjectViewModel(OloopStore store, string actor);
    public string Name { get; set; }
    public string ReferencePath { get; set; }
    public bool CanSave { get; }
    public string? Error { get; }
    public Task<OloopProject?> SaveAsync(CancellationToken ct);
}
```

### Stream C

```csharp
public partial class NewTaskViewModel : ViewModelBase
{
    public NewTaskViewModel(OloopProject project);
    public string Title { get; set; }
    public string Description { get; set; }
    public string LabelsText { get; set; }            // comma separated
    public TaskType Type { get; set; }
    public IReadOnlyList<TaskType> Types { get; }
    public bool CanSave { get; }
}

public partial class EditTaskViewModel : ViewModelBase
{
    public EditTaskViewModel(OloopTask task);
    public string Title { get; set; }
    public string Description { get; set; }
    public string LabelsText { get; set; }
    public bool CanSave { get; }
    public bool IsDirty { get; }
}

public partial class ConflictViewModel : ViewModelBase
{
    public ConflictViewModel(OloopTask task, string reason);
    public string Message { get; }
    public string Reason { get; }
}
```

## Theme resource keys (already committed)

`Themes/Theme.axaml`:

- Brushes: `SurfaceBrush`, `SurfaceContainerLowBrush`, `SurfaceContainerBrush`,
  `SurfaceContainerHighBrush`, `SurfaceContainerHighestBrush`, `PrimaryBrush`,
  `OnPrimaryBrush`, `PrimaryContainerBrush`, `OnPrimaryContainerBrush`,
  `SecondaryContainerBrush`, `OnSecondaryContainerBrush`, `TertiaryContainerBrush`,
  `OnTertiaryContainerBrush`, `OnSurfaceBrush`, `OnSurfaceVariantBrush`,
  `OutlineBrush`, `OutlineVariantBrush`, `StatusOpenBrush`, `CodeBlockBgBrush`,
  `TypeTodoBrush` / `TypeTodoBgBrush` … `TypeBugBrush` / `TypeBugBgBrush`,
  `TypeClosedBrush`, `TypeClosedBgBrush`.
- Geometries: `IconChecklist`, `IconHelp`, `IconGavel`, `IconBuild`,
  `IconBugReport`, `IconFolder`, `IconSearch`, `IconRefresh`, `IconSettings`,
  `IconAdd`, `IconBack`, `IconClose`, `IconCopy`, `IconSend`, `IconMoreVert`,
  `IconEdit`, `IconTaskAlt`, `IconFilter`, `IconGroup`, `IconVisibility`,
  `IconWarning`.

A resource key that depends on a runtime value cannot be bound with
`{StaticResource}`. Use `DynamicResource` with a `Binding` on the key, or
expose the brush directly from the view model.

## Style classes (already committed)

`Themes/Styles.axaml`:

- `TextBlock`: `TitleLarge` (22), `TitleMedium` (16/Medium), `HeadlineSmall` (24),
  `BodyMedium` (14, variant), `BodyLarge` (16/24), `LabelMedium` (12/Medium),
  `LabelSmall` (11), `SectionHeader` (14/Medium/primary), `Muted`.
- `Border`: `Card`, `Field`, `Chip` (+ `Outlined`, `-Todo`, `-Question`,
  `-Decision`, `-Feature`, `-Bug`, `-Closed`), `CodeBlock`, `Divider`,
  `IconContainer` (40×40, radius 12).
- `Button`: `Primary`, `Outlined`, `Text`, `Icon`, `Fab`.
- `TextBox`: `SearchBox` (pill, 44 high).

## Avalonia 12 traps

- `PathIcon` has **no** `Fill` property — use `Foreground`.
- `TextBox.Watermark` is obsolete — use `PlaceholderText`. There is no
  `PlaceholderTextForeground`.
- `Geometry.Parse` needs an `IPlatformRenderInterface`. Geometries that come
  from the theme resources are fine; parsing a path string at runtime only
  works once a render interface exists (it does in the running app, and in
  the headless tests).
- `TextBox.Text` is `string?`; two-way bindings to a non-nullable view model
  property need `Mode=TwoWay`.
- Dialogs: `await dialog.ShowDialog<TResult>(owner)`, the result is the value
  passed to `Close(result)`.
