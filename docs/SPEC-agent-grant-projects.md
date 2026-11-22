# SPEC: Give an existing agent more projects

**Status:** draft (2026-11-22). Builds on `docs/SPEC-agent-management.md`, `spec/AGENTS.md` and `spec/SERVER.md`, which stay authoritative for the protocol. This document only says what the desktop app has to do. It closes "Open questions", item 1, of the agent management spec.

## Purpose

An agent is created with a fixed set of projects. Later projects (new ones, or ones that were imported, see `SPEC-import-folder.md`) can only reach the agent if the user hands it their keys. The protocol allows that at any time: `PUT /v1/projects/{id}/members/{agentId}` replaces or adds keys and changes the role (`SERVER.md`, "Projects and members"). The app has no way to do it today, so the user has to create a new agent with a new credentials file.

This feature adds the missing action. The credentials file of the agent does not change: it holds the agent's own keys and token, not its projects.

## Scope

In scope, server backend only:

1. A row action "Projects…" in the list of agents, which gives the agent more projects.
2. The check of the key chain of the agent before anything is handed over (`AGENTS.md`, "Rotating the key pair").

Not in scope:

- Taking a project away from an agent, or changing its role. Revoking a member is `POST /v1/projects/{id}/revoke` and may rotate the project key; that is a feature of its own.
- A role per project. One role for all projects of one run, as in the dialog for a new agent.
- A column in the list that shows the projects of an agent (see "Open questions").
- The folder backend, which has no agents.

## Preconditions

- The app is signed in and the user key is unlocked (`ServerSession.Agents != null`). Locked: the app asks for the passphrase first, as for everything that needs the key.
- The agent is **not revoked**. Revoking removes every membership and the server answers `404` for a revoked agent as a member (`SERVER.md`, "Projects and members"). The action is disabled for revoked agents.
- An agent whose token has expired may still be given projects: the keys travel through the owner, not through the agent. The dialog says that the agent needs a new token to use them.
- Only projects the user **owns** can be handed over (`role` `owner` in `GET /v1/projects`).

## User flow

Entry: settings, tab "Security", "Manage agents…", row action "Projects…". It opens a dialog.

The dialog lists the projects of the user. For each project it shows whether the agent is a member already and with which role. This needs one `GET /v1/projects/{id}/members` per project of the user (owner only, answers `[{ principalId, kind, name, role, publicKey }]`); the list is built from the member whose `principalId` equals the agent id.

| Element | Rules |
| --- | --- |
| Project list | Projects of the user, by name. A project that the agent has already is shown with its role and cannot be selected. |
| Role | `write` (default) or `read`. Applies to the projects selected in this run only; it does not change the role of projects the agent already has. |
| Button "Give access" | Enabled while at least one project is selected and no request is running. |

If the user has no project that the agent lacks, the dialog says so and offers only "Close".

Steps on confirm, in this order. If a step fails, the later ones do not run.

1. **Check the key chain of the agent** (see below). On failure: stop, show the reason, hand over nothing.
2. **Open the project keys** of all selected projects with the user key, every key version the user handed to itself, exactly as `AgentService.CreateAsync` does. This happens for **all** projects before the first `PUT`, so a project that cannot be opened stops the run before anything changed.
3. For each selected project: `PUT /v1/projects/{projectId}/members/{agentId}` with the role and the keys wrapped for the agent's public key (`ProjectKeyBox.Wrap(projectKey, projectId, keyVersion, agentPublicKey, userSecretKey)`).
4. Show the result: the projects that were handed over, and for each failure the project and the reason.

The app does not log, display or keep the project keys.

### Failure handling

Unlike creating an agent, there is nothing to roll back: the agent exists, and each `PUT` is complete on its own and can be repeated (it replaces or adds keys). So:

- A `PUT` that fails does not undo the ones before it. The result names the projects that were handed over and the ones that were not.
- The dialog stays open after a partial result, with the failed projects still selected, so one click tries them again.
- `404` for the agent means it was revoked meanwhile; the dialog says so and closes the action.

## Checking the key chain of the agent

`SERVER.md` and `AGENTS.md` require the client to verify the public key of an agent before it hands the agent a key. The server cannot be trusted with this, because it could put its own key in place of the agent's and so receive the project key.

`GET /v1/agents` returns, per agent, `publicKey`, `registration` and `keyChain: [{ publicKey, statement }]`. The app does not read these fields today (`AgentInfo` has none of them). Steps:

1. `AgentCrypto.OpenRegistration(registration, userSecretKey, userPublicKey)` gives `(key0, agentId0)`. `agentId0` must equal the `id` of the agent. Opening fails if the registration was not made with the user's key.
2. For each entry of `keyChain`, in order: `AgentCrypto.OpenStatement(statement, userSecretKey, previousPublicKey)` gives `(newKey, agentId, keyNumber)`. `agentId` must equal the agent id, `keyNumber` must be one more than the previous one (the first rotation is number 2), and `newKey` must equal the `publicKey` of that entry.
3. The last key of the chain (the registration key when the chain is empty) must equal the `publicKey` of the agent.

Any mismatch, or an `OloopCryptoException`, means the chain is not valid. The app then shows a warning, hands over nothing, and offers no way to continue from the dialog. `AGENTS.md` says the user decides in that case; the decision is "revoke the agent and create a new one", so the dialog names that action. A "continue anyway" button is deliberately **not** offered.

The key that the app wraps for is the one it verified, never the one it fetched in a second request.

## Code

| Part | Where | Notes |
| --- | --- | --- |
| Agent data | `Core/Server/AgentService.cs` | `AgentInfo` gets `PublicKey`, `Registration`, `KeyChain`. `Parse` reads them. The existing columns do not change. |
| Chain check | `Core/Server/AgentService.cs` (or `Core/Crypto/AgentCrypto.cs`) | `VerifyKeyChain(AgentInfo, UnlockedUser)` returns the verified public key or throws `OloopCryptoException`. |
| Members | `Core/Server/AgentService.cs` | `ProjectsOfAsync(agentId)`: `GET /v1/projects` plus `GET /v1/projects/{id}/members` for the owned ones. |
| Grant | `Core/Server/AgentService.cs` | `GrantProjectsAsync(AgentInfo, IReadOnlyList<Guid>, bool readOnly)` verifies, calls the existing `OpenProjectKeysAsync` for all projects, then `GrantAsync` per project. Both stay `private`. |
| View model | `ViewModels/AgentsViewModel.cs`, new `ViewModels/GrantProjectsViewModel.cs` | The row action and the form. |
| Window services | `ViewModels/IAgentUi.cs` | One more method for the dialog. |
| View | `Views/GrantProjectsDialog.axaml` | Modelled on `NewAgentDialog`: the same project list control, without name, validity and file fields. |

## Tests

Unit tests in `OloopApp.Tests`, in the style of `AgentServiceTests` with `FakeOloopServer`:

1. **Happy path.** Two projects granted: one `PUT` each, `wrapped` opens with the agent's secret key and names the right project and key version; the body has the role.
2. **Already a member.** A project the agent has is not selectable and gets no `PUT`.
3. **Key chain valid.** A registration only; a registration with two rotations.
4. **Key chain invalid.** The server returns another `publicKey` than the chain ends with; a statement for another agent id; a skipped `keyNumber`; a registration made with another user's key. Each stops before any `PUT`.
5. **Not owner.** A project with role `write` stops the run before any `PUT`.
6. **Partial failure.** The second `PUT` fails: the first stays, the result names the second, a repeat of only the second succeeds.
7. **Revoked agent.** The action is disabled; a `404` during the run is reported.
8. **Older key versions.** A project with key versions 1 and 2 hands over both.
9. **View model.** Button state with no selection, with a selection, while running.

## Security notes

- The chain check is the only protection against a server that substitutes the agent's key. It is not optional and has a test for each way to fail.
- Hand over the least: the dialog selects nothing by default, and `read` is one click. An agent that was created for one task should not collect projects unnoticed.
- A project granted here is readable by whoever holds the agent's credentials file. The dialog repeats the note of the result page of "New agent": an LLM that can read the file can act as the agent.

## Open questions

1. **Projects in the list of agents.** With the members call per project, the list of agents could show a "Projects" column. It costs one request per project of the user each time the list loads. Proposal: not in this version; the dialog loads the members only when it opens.
2. **Taking a project away.** Needs `POST .../revoke` and the choice whether to rotate the project key. Proposal: its own spec.
3. **Role change.** `PUT` with another role works for a project the agent has. Proposal: not offered here, to keep "give access" free of changes to what exists.
