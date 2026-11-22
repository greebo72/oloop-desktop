# SPEC: Agent management in the desktop app

**Status:** implemented (2026-11-21), with the differences listed under "As built". Builds on `spec/AGENTS.md` and `spec/SERVER.md`, which stay authoritative for the protocol. This document only says what the desktop app has to do and how it looks.

## Purpose

An agent is the identity with which the CLI works on behalf of a user, for example when an LLM uses it. Creating one needs the secret key of the user (`AGENTS.md`, "Creating an agent"). The desktop app is the only client that has the sign-in, the passphrase and the unlocked key, so it is the place where agents are created and revoked.

The app writes the **credentials file**. The CLI (`oloop-cli`, `oloop --credentials FILE` or `$OLOOP_AGENT_CREDENTIALS`) reads it and needs nothing else.

## Scope

In scope, server backend only (the folder backend has no agents):

1. Create an agent and write its credentials file.
2. List the agents of the signed-in user.
3. Issue a new token for an agent (for example after the token expired).
4. Revoke an agent.

Not in scope:

- Renewing a token or rotating the key pair. The agent does that itself (`oloop agent renew [--keypair]`).
- Changing the projects or the role of an existing agent. See "Open questions".
- Anything for the folder backend.

## Preconditions

- The app is signed in to a server and the user key is unlocked (`ServerSession.IsSignedIn`, `ServerSession.Store != null`). If it is locked, the app asks for the passphrase first, as it does for everything that needs the key.
- Without a server session the whole feature is hidden or disabled.

## User flows

### Create an agent

Entry: settings, tab "Security", button "Manage agents…", which opens the list of agents; there the button "New agent…". The button is enabled while the keys are open.

Input:

| Field | Rules |
| --- | --- |
| Name | Required, 1 to 100 characters after trimming. Unique among the agents of the user that are not revoked, compared without regard to case. The server answers `409` for a duplicate; the dialog also checks against the loaded list. |
| Projects | At least one. Multi-select of the projects of the user. The agent gets nothing that is not selected. |
| Role | `write` (default) or `read`. One role for all selected projects. |
| Token valid for | Default 90 days. Choice of 30 days, 90 days, 1 year, 2 years. |
| Renewable for | Default 1 year. Choice of "Not renewable", 1 year, 2 years, counted from now. "Not renewable" sends `renewUntil` equal to `expiresAt`. |
| Save as | The file chooser of the system, asked when the user confirms. The suggested name is made from the agent name (letters, digits, dash and underscore; the rest becomes a dash). The chooser asks before it replaces a file. Cancelling it makes nothing. |

Steps on confirm, in this order. If a step fails, the later ones do not run and the dialog shows the error.

1. Generate a key pair for the agent (`crypto_box_keypair`, X25519).
2. Generate the agent id: a random UUID (version 4, lowercase, with dashes). The client chooses it because the registration contains it (`AGENTS.md`, "Creating an agent").
3. Build the **registration** with `AgentCrypto`: `nonce | crypto_box_easy(agentPublicKey | agentId, nonce, userPublicKey, userSecretKey)`. 88 bytes, base64url without padding.
4. `POST /v1/agents` with `{ id, name, publicKey, registration, expiresAt, renewUntil }`. The times are UTC with `Z`. The answer is `{ agentId, token, expiresAt }`, with `agentId` equal to the `id` that was sent. On `409` for the id (not for the name), start again at step 1 with a new id and a new key pair; the dialog does not show this to the user. The token is shown once; the server keeps only its hash.
5. For each selected project: open the project key with the user key, wrap it for the agent with `ProjectKeyBox.Wrap(projectKey, projectId, keyVersion, agentPublicKey, userSecretKey)`, and `PUT /v1/projects/{projectId}/members/{agentId}` with the role. Use the current key version of the project.
6. Write the credentials file (see below).
7. Show a result page: the file path, the command `export OLOOP_AGENT_CREDENTIALS=<path>`, the expiry date, and the note that the file is the only copy of the secret key.

Failure handling:

- If step 4 succeeded but step 5 or 6 failed, the agent exists on the server without a usable file. The app revokes it again (`POST /v1/agents/{id}/revoke`) and says so. If the revoke fails too, the app names the agent and tells the user to revoke it in the list.
- Partial project grants are not kept: if one project fails, the agent is revoked as above, so an agent never has fewer projects than the user asked for without the user knowing.

### List agents

`GET /v1/agents`. One row per agent:

| Column | Source |
| --- | --- |
| Name | `name` |
| Status | "revoked", "expired" when `expiresAt` is in the past, otherwise "active" |
| Token valid until | `expiresAt` |
| Renewable until | `renewUntil` |
| Last used | `lastUsedAt`, or "never" |
| Renewable until | `renewUntil`, shown in the row data but not as a column |

Revoked agents are shown greyed out and below the active ones. They stay in the list, because the app keeps `writtenBy` readable.

The list is reloaded when the section opens and after every action. It does not poll.

### New token

Row action "New token". Needs a confirmation that names the effect: the tokens of the agent end **at once** (no grace), and the old credentials file stops working. Then `POST /v1/agents/{id}/token` and a file chooser.

The new file has to contain the **secret key of the agent** (and keeps `name` and all other fields of the old file), which the app does not keep. So the app cannot write a complete file for an existing agent. This action is therefore only offered with a file the user picks that is the existing credentials file of this agent: the app reads it, checks that `agentId` matches, replaces `token` and `expiresAt`, keeps every other field (including `name` and fields it does not know), and writes it back atomically. Without such a file the app says that the agent has to be created again (this matches "Lost credentials file" in `AGENTS.md`). A revoked agent answers `409`; the action is disabled for it.

### Revoke

Row action "Revoke" with a confirmation that names the agent. `POST /v1/agents/{id}/revoke` (`204`, can be repeated). Revoking ends the token and removes every membership of the agent. The agent is not deleted and the name is free again afterwards.

The button is labelled "Revoke", not "Delete", because an agent is never deleted (`AGENTS.md`, "What an agent is").

The confirmation also says what revoking does **not** do: project keys the agent already knew stay known to it. If the file may have leaked, the user also has to rotate the project keys. (A key rotation is not part of this feature.)

## The credentials file

JSON, UTF-8, mode `0600` on macOS and Linux, written atomically (temporary file in the same folder, `fsync`, replace). On Windows the file gets an ACL for the current user only. The shape is the contract with `oloop-cli` (`AgentCredentials.from_dict`):

```json
{
  "server": "https://oloop.example.com",
  "agentId": "<UUID>",
  "name": "claude",
  "token": "<token>",
  "publicKey": "<base64url, 32 bytes>",
  "secretKey": "<base64url, 32 bytes>",
  "owner": {
    "id": "<user UUID>",
    "publicKey": "<base64url, 32 bytes>"
  },
  "expiresAt": "2027-02-19T10:00:00Z"
}
```

Rules:

- All base64url values are without padding.
- `server` is `https://`, or `http://` for a loopback host only. It is normalised without a trailing slash.
- `owner.id` and `owner.publicKey` are those of the signed-in user. They come from the app's own key bundle, never from an answer of the server, because the agent checks every key it receives against `owner.publicKey`.
- `name` is the name the user typed in the dialog, as sent to the server (trimmed). It is a label for people and the default `createdBy`/`updatedBy` of the CLI; `agentId` is the identity. See `AGENTS.md`, "The credentials file".
- `expiresAt` is written, `keyNumber` is not (it is left out while the key pair is the first one).
- The file holds no passphrase, no user key, no recovery code and no key of a project that was not granted.

The app does not keep a copy of the file or of the agent's secret key. After step 6 the secret key is dropped from memory.

## Code

| Part | Where | Notes |
| --- | --- | --- |
| Registration | `Core/Crypto/AgentCrypto.cs` | `CreateRegistration(agentPublicKey, agentId, userSecretKey, userPublicKey)` next to `OpenRegistration`. |
| Agent API | `Core/Server/AgentService.cs` | `ListAsync`, `CreateAsync`, `NewTokenAsync`, `RevokeAsync`, on top of `ServerApi`. `CreateAsync` also opens the project keys, hands them over (`PUT .../members/{agentId}`, every key version the user can open) and revokes the agent again when a step fails. |
| Credentials file | `Core/Io/AgentCredentialsFile.cs` | `Write`, `ReplaceToken`, `CheckBelongsTo`. Atomic, mode `0600` from the moment of creation, ACL for the current user on Windows. |
| Session | `ViewModels/ServerSession.cs` | `Agents`, set while the keys are open, `null` when locked or signed out. |
| View models | `ViewModels/AgentsViewModel.cs`, `ViewModels/NewAgentViewModel.cs` | List and commands; the form and the result. |
| Window services | `ViewModels/IAgentUi.cs` | The second dialog, the question and the two file choosers. The agents window implements it, so its dialogs open on top of it. |
| Views | `Views/AgentsDialog`, `Views/NewAgentDialog`, `Views/ConfirmDialog` | Opened from the button in the settings (`Views/SettingsDialog.axaml`). |

The view models reach the windows only through `IAgentUi`, so they stay testable.

## Tests

Unit tests in `OloopApp.Tests`, in the style of `ServerStoreTests` and `ServerCryptoVectorsTests`:

1. **Registration vectors.** `CreateRegistration`, given the nonce of the registration in `vectors.json`, produces exactly the bytes of that vector (88 bytes), and `OpenRegistration` opens what it made. No new vector was needed.
2. **Credentials file shape.** The written JSON has exactly the fields above (including `name`), `owner` as an object, base64url without padding, and mode `0600` (skipped on Windows, where the ACL is checked instead).
3. **Atomic write.** A failure during the write leaves an existing file unchanged and no temporary file behind.
4. **Create flow with a fake server.** Order of the calls, the body of `POST /v1/agents`, one `PUT` per project with a valid `wrapped` (opens with the agent's key and names the right project and key version).
5. **Rollback.** Failure of the second `PUT` leads to a revoke call and no file.
6. **Revoke and new token.** Right endpoints, `409` for a revoked agent, the file is only touched when `agentId` matches.
7. **Id clash.** `409` on the id leads to one retry with a new id and a new registration; `409` on the name does not retry and shows the duplicate-name error.
8. **Validation.** Empty name, 101 characters, duplicate name, no project, token longer than 10 years, `renewUntil` before `expiresAt`.
9. **Interop with the CLI.** A file written by the app loads with `AgentCredentials.from_json` in `oloop-cli`, and its public key matches the secret key. Manual check: `OLOOP_INTEROP_FILE=<path> dotnet test --filter Interop` writes the file, then load it in the CLI (`uv run python -c ...`). Without the variable the test does nothing.

## As built

Differences from the plan above:

- The labels are English, like the rest of the app.
- "Token valid for" and "Renewable for" offer fixed choices. A custom date is not offered.
- The list has no column for the projects of an agent. Whether the server shows an agent's projects to its owner in one call is still not clear from `AGENTS.md`.
- The project keys are handed over for every key version the user can open, not only the newest: the server requires the newest and the agent needs the older ones to read older documents.
- A name that is taken is found in the list of agents before anything is registered. The server answers `409` for a name and for an id alike, so after the check a `409` is taken for the id and retried up to three times.
- Windows: the ACL code is written but was not run on Windows.
- The dialogs were checked headless (they open, show their content, enable the buttons as expected) but not looked at by a person on screen.

## Security notes

- The agent's secret key and the token exist only in the new file. They are not logged, not shown in a dialog, and not copied to the clipboard.
- The file chooser warns when the target folder looks synced (for example a cloud drive), because the file is a secret.
- An agent file is as strong as its file mode. The result page says that an LLM that can read the file can act as the agent, and that real separation needs another operating-system user or a container (`AGENTS.md`, "What an agent cannot do").
- Grant the least: the dialog defaults to no projects selected, and `read` is one click.

## Open questions

Resolved: the agent id is chosen by the client and sent as `id` (`AGENTS.md`, "Creating an agent" and "API"). This needs the matching change in `oloop-server` (`CreateAgentIn.id`) before step 4 works against a real server.

1. **Projects of an existing agent.** Adding or removing a project later is possible with the same `PUT` and a `DELETE` on the member, but the spec has no UI for it. Proposal: out of scope for the first version; the user creates a new agent. Adding projects is now specified in `SPEC-agent-grant-projects.md`; taking one away is still open.
2. **Role per project.** The first version has one role for all projects. A role per project is a small extension of the dialog and needs no protocol change.
3. **Project list in the table.** Whether the server exposes the members of an agent to its owner in one call is not clear from `AGENTS.md`. If not, the column is left out.
4. **Where the CLI documentation lives.** `oloop-cli/docs/CLI_SERVER_BACKEND.md` points to this file for creating the credentials. Keep the two in step.
