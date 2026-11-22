# SPEC: Take a project away from an agent

**Status:** draft (2026-11-22). Builds on `docs/SPEC-agent-grant-projects.md` (the dialog this feature extends), `docs/SPEC-agent-management.md`, `spec/AGENTS.md` ("Revoking") and `spec/SERVER.md` ("Projects and members"), which stay authoritative for the protocol. This document only says what the desktop app has to do.

## Purpose

An agent can be given projects one by one (`SPEC-agent-grant-projects.md`), but it can only lose them all at once: "Revoke" in the list of agents ends the token and every membership. A user who wants an agent to keep `oloop-desktop` and lose `oloop-ios` has to revoke it, make a new agent and a new credentials file.

The protocol has the call for it: `POST /v1/projects/{id}/revoke` with the id of the agent removes that one member and its keys (`SERVER.md`, "Projects and members"). The app has no way to make it.

## Scope

In scope, server backend only:

1. A button "Remove" for each project that the agent has, in the dialog "Projects…" of the agent.
2. The question that says what removing does and does not do.

Not in scope:

- **Rotating the project key** (`newKey`) and re-encrypting the documents of the project. That is what makes a removal thorough, see "What a removal does not do" and "Open questions". This version removes the membership only and says so.
- Changing the role of a project the agent has.
- Removing a project from several agents at once, or from all agents of a project.
- Removing a project for a revoked agent: it has none.
- The folder backend, which has no agents.

## Preconditions

- The app is signed in and the user key is unlocked, as for the dialog of `SPEC-agent-grant-projects.md`. Locked: the app asks for the passphrase first.
- The agent is not revoked. The dialog is not offered for a revoked agent.
- The user owns the project (the app has one user, so all its projects).

The check of the key chain is **not** needed here: nothing is handed over to the agent, so there is no key to protect from a substituted public key.

## User flow

Entry: the dialog "Projects…" of an agent (settings, tab "Security", "Manage agents…", row action "Projects…"). In its list, a project that the agent has shows its role ("has it (write)") and now also a button **Remove**. A project the agent lacks shows the tick box as before and no button.

Steps on "Remove":

1. The app asks for confirmation (see the text below). Cancelling does nothing.
2. `POST /v1/projects/{projectId}/revoke` with `{ "principalId": "<agentId>" }` and no `newKey`. The answer is `204`.
3. The dialog marks the project as one the agent no longer has: the role text goes, the tick box is enabled again (the project can be given back), and a note says "<project> was taken away from <agent>."

The question:

> **Take "<project>" away from "<agent>"?**
> The agent loses access to this project on the server at once. It does not lose the key it already has: it can still read what it saved, and anything of this project that reaches it as ciphertext. If the credentials file of the agent may have leaked, this is not enough; revoke the agent and rotate the project key instead.
> [Cancel] [Take away]

The safe answer is the primary button (cancel), as in `ConfirmDialog`.

### Failure handling

- `404` for the agent or the project: the agent was revoked meanwhile, or the project is gone. The dialog says so and reloads which projects the agent has.
- Any other error: shown as the error of the dialog. The project keeps its state; the user can try again. The call is repeatable: removing a member that is gone is answered as the server answers it, and the dialog reloads afterwards, so what it shows is what the server says.
- A removal never touches other projects: one request per click.

## What a removal does not do

Stated in the question and in the notes of the dialog, because the user may think otherwise:

- The agent keeps every project key version it was given. Without a new key version (`newKey`), a change that is written afterwards is encrypted with the same key the agent knows. The server no longer shows the project to the agent, so the agent cannot fetch it; but ciphertext that the agent kept, or that someone else gives it, stays readable to it.
- Tasks the agent wrote stay, with `updatedBy` and `writtenBy` as they are.

For a removal that also protects what is written afterwards, the project key has to be rotated and the documents re-encrypted (`AGENTS.md`, "Revoking"). That is the second stage; see "Open questions".

## Code

| Part | Where | Notes |
| --- | --- | --- |
| Remove | `Core/Server/AgentService.cs` | `RemoveProjectAsync(Guid agentId, Guid projectId, CancellationToken)`: `POST v1/projects/{id}/revoke` with `{ principalId }`. No `newKey`. Throws `ServerApiException` as `GrantAsync` does. |
| View model | `ViewModels/GrantProjectsViewModel.cs` | `RemoveAsync(GrantProjectChoice)`; the model gets the `IAgentUi` for the question. `GrantProjectChoice` gets `CanRemove` (`ExistingRole is not null`). |
| Window services | `ViewModels/IAgentUi.cs` | `ConfirmAsync` is there already; no new method. |
| View | `Views/GrantProjectsDialog.axaml` | The button in each row of the list, next to the role text. |

The dialog and its view model keep their names: they are the place for "which projects has this agent", not only for giving.

## Tests

Unit tests in `OloopApp.Tests`, in the style of `GrantProjectsTests` with `FakeOloopServer`. The fake server needs `POST /v1/projects/{id}/revoke` for a member: it removes the member and its keys and answers `204`, as the spec says.

1. **Removes one project.** The agent has two; after the removal it has one, the other keeps its role and keys, and the call is exactly one `POST` with the agent id and no `newKey`.
2. **Question first.** With "cancel", no request is sent. The question names the project and the agent, and says that the agent keeps its key.
3. **State after removal.** The project shows no role, can be ticked again, and giving it back works (a hand-over after a removal).
4. **Does not touch other agents.** A second agent in the same project keeps it.
5. **Server errors.** A `500` leaves the project as it was and shows the error; a `404` reloads the roles and says the agent is gone.
6. **Repeat.** Removing a project that is already gone leaves the dialog in the state the server shows.
7. **Revoked agent.** The dialog is not offered; `RemoveProjectAsync` for a revoked agent is a `404` of the server, not a crash.
8. **View model.** `CanRemove` follows `ExistingRole`; the button is disabled while a request runs.

## Security notes

- Honesty is the feature: the question says that the agent keeps its key. A "Remove" that sounds like "the agent can no longer read the project" would be false.
- No key is handed over, so there is no new input from the server to check. The key chain check of the grant dialog is not part of this flow.
- The app does not log, show or keep any key.

## Open questions

1. **Second stage: rotate the project key.** `POST .../revoke` with `newKey` raises `keyVersion` by one and needs a `wrapped` key for **every remaining member** (`SERVER.md`); the client then writes each document of the project again with the new `keyVersion` (a normal change, `rev` plus one). It protects what is written afterwards and what is re-encrypted; what the agent already holds stays readable (`SERVER.md`, "Threat model"). It needs: the keys of all remaining agents (so the chain of each has to be checked as in the grant dialog), a conflict strategy for a write that races the rotation (`409`), and a way to resume when the rewrite stops half-way (documents that are not rewritten stay readable with the older key). Proposal: its own spec, after this one is in use.
2. **Whether to offer "Remove" without the key rotation at all.** The alternative is to offer only the thorough variant. Proposal: offer the simple one with the honest question, because for a user who only wants the agent to stop seeing a project in its list it is what they need, and the question names the limit.
3. **A view of the agents of a project.** The members call (`GET /v1/projects/{id}/members`) makes "which agents can see this project" possible from the project side. Proposal: not in this version; the agent side is enough for the case.
