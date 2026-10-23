# Changelog

All notable changes to the oloop desktop app are listed here, newest first.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.5.1] - 2026-10-23

### Added

- **Project cards show more.** Each card lists the open tasks by type as
  coloured chips, a red chip with the number of overdue tasks, a progress bar
  (the share of closed tasks) next to "N open of M", and the repository of the
  project as `host:path` under the name. The columns line up across all cards.
- **Collapsible groups.** In the task list and in the "Silent since" view a
  group can be collapsed and expanded by clicking its header, which shows the
  number of tasks even when collapsed. "Collapse all" and "Expand all" are in
  the menu next to the grouping. The state survives reloads, new searches and
  changes in the data folder, and is reset when the grouping changes. Selecting
  a task in a collapsed group (for example from the task search) expands it.
  The rows of a collapsed group are not built, which also helps long lists.
- **Label suggestions.** While you type in the label field of the new task
  dialog or of the in-place edit, a list shows the existing labels that fit the
  label at the cursor, most used first, with the number of tasks. Up and Down
  choose, Tab or Enter take the suggestion, Escape closes the list, a click
  takes it. A new label is always possible.

### Changed

- **One spelling per label.** A new label that only differs in case from an
  existing one is saved in the spelling of the existing one (`bug` becomes
  `Bug`), and the status line says so. Labels a task already carried keep their
  spelling when you edit it.
- **Closed tasks look closed.** In the task list, the "Silent since" view and
  the dashboard task search a closed task has a struck-through title in normal
  weight and a muted colour, a muted meta line, and a grey type icon. This
  matches the Android app.

### Fixed

- **Rows opened only at the front.** The button of a project card, of a task
  hit on the dashboard, of a task in the list and of a task in the "Silent
  since" view did not fill the row, so only its front part reacted to the
  mouse. The whole row is clickable now.

## [0.5.0] - 2026-10-22

### Added

- **Due dates.** A task can have a due date. It is shown in the task row and in
  the detail, with an "overdue" badge for open tasks whose date has passed. It
  is picked with a calendar in the edit mode and in the new task dialog, and
  removed with a Clear button. The task list can be grouped by due date,
  earliest first and tasks without a date last.
- **Search over the tasks of all projects.** A Projects | Tasks switch next to
  the search field of the dashboard. In the task mode the search looks into
  title, description and labels of all projects, shows the project in every
  result, and has a "Show closed" switch. A click opens the task in its
  project.
- **Filters for the task search.** A due date filter (any, overdue, today, this
  week, with date, without date) with a sort by due date, and a label filter:
  every label of the loaded tasks is listed once with the number of tasks,
  several labels can be picked, and the picked labels show as chips. Labels are
  compared without regard to case.
- **The view reloads by itself.** The app watches the data folder and reloads
  when the CLI, another client or the sync writes into it. Changes are merged
  into one reload, the app's own writes do not reload twice, and the selection,
  a typed comment and an edit that is under way survive. The Reload button
  stays as the manual way.
- **Text size.** A "Text size (%)" setting (50 to 300) scales the whole window.
  It is stored in the settings file of the machine, not in the data folder.
- **First task selected.** A project opened from the project list shows the
  detail of its first task instead of an empty pane.

### Changed

- **Edit in place.** Title, description and labels turn into input fields in
  the task detail, with an explicit Save and Cancel. The edit dialog is gone.
  Input that is not written yet is kept as a draft: it survives selecting
  another task, a reload, a search and leaving the project, and the window asks
  before it closes with unsaved input. A conflict on save keeps what you typed.
- **Unknown fields are kept.** Fields in a task, project or comment that the
  app does not know no longer make a file unreadable. They are written back
  unchanged, in the order of the file.
- The search placeholder is English and says what the search covers.
- The app follows the updated spec: due dates, unknown fields, the label
  comparison rule and the task image rules.

### Fixed

- The text size of the detail stays the same while a task is edited.

### Documentation

- The README shows the performance measurements, the macOS installation
  instructions are revised, and there is a security policy.

[0.5.1]: https://github.com/greebo72/oloop-desktop/compare/v0.5.0...v0.5.1
[0.5.0]: https://github.com/greebo72/oloop-desktop/releases/tag/v0.5.0
