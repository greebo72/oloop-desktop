# oloop-desktop

## Markdown renderer and the instructions for agents

The rules that agents follow when they write task files live in `spec/agent-instructions/task-markdown.md` (repo `oloop-spec`). Some of them depend on what the Markdown renderer of this app can show today (for example that HTML is shown as text, or how table columns are sized).

When you change what the renderer supports, change `task-markdown.md` in `oloop-spec` in the same step, and commit and push it there.
