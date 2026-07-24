# Snippets

Snippets is a standalone project that turns everyday short-form content into a local snippets library that is maintainable, quick to copy from, and driven by a Job Runner.

It focuses on three high-frequency objects:

| Area | Role | Description |
|---|---|---|
| Clip | Automatic capture | Persists text, HTML, images, and file lists as real files. |
| Note | Manual maintenance | Maintains continuous text in Markdown, while marking selected parts as quick-copy sources. |
| Jobs | Job Runner | Manages trigger + action jobs; built-in capabilities run as `tool`, and external processes run as `command`. |

## Background

Snippets provides a file-first Clipboard module:

- Clipboard payloads are written to disk instead of being stored only in an application database.
- Auto-saved items and favorite items are stored in separate directories.
- It supports copying an item back to the clipboard, revealing the file, deleting, and pinning/unpinning.
- Save paths and the auto-save limit are configurable.

Snippets focuses on Clip, Note, and Jobs rather than file backup or software scanning.

## Product goals

1. **File first**: Core data should exist as readable, backup-friendly, portable files.
2. **Local first**: The app does not depend on cloud services by default; core content lives on the local file system.
3. **Low-friction capture**: Clipboard content is saved automatically, and notes are quick to create.
4. **Separate maintenance from copying**: Notes let users maintain continuous, natural text while marking parts of that text as quick-copy snippets.
5. **Unified Job Runner**: Clip polling, startup cleanup, manual actions, and external commands all use the same trigger + action model.
6. **Tray-aware access**: The app can stay available in the system tray, reopen the main window, and expose a bounded Quick menu.

## Core concepts

### Clip

A Clip is one snapshot captured from the system clipboard. Each Clip should have its own file and metadata.

The Clip storage design uses the following layout:

```text
Clips/
  AutoSave/
    2026-07-24T06-44-00-063.txt
    2026-07-24T06-45-12-118.png
  Favorites/
    2026-07-24T06-43-21-004.html
```

Current capabilities:

- Watch clipboard changes automatically.
- Support text, HTML, images, and file lists.
- Support favorite, delete, copy back to clipboard, and reveal in file manager.
- Support a maximum auto-save count and cleanup behavior.
- Support deduplication by default: compare the current clipboard payload hash with the last saved payload hash to avoid consecutive duplicates.
- Optionally support cache-window deduplication: within a configured time window, content hashes that were already saved are not saved again.

### Note

A Note is user-maintained Markdown content. Unlike a general-purpose note-taking system, the current scope solves one problem: **mark quick-copy snippets inside a continuous, readable Markdown document**.

Notes are stored under a `Drafts/` subdirectory to keep the user-maintained Markdown files separate from future sibling directories:

```text
Notes/
  Drafts/
    profile.md
    replies.md
    templates.md
```

Current scope:

- Create, edit, and delete Markdown notes.
- Mark copyable snippets inside notes with `data-copy-*` attributes.
- Derive a Quick Copy list from notes.
- Provide Source editing, rendered Preview, and a Quick panel in the app.

Out of current scope:

- Tags.
- Full-text search.
- Archive flows.
- Inbox workflow.
- Converting Clip to Note.
- Daily or weekly generated notes.
- Bidirectional links between notes.
- Non-Markdown note formats.

#### Quick Copy markers

The UX problem is that users often want information to stay continuous when they maintain it, but want to copy only a small part of it when they use it.

For example, profile information is more naturally maintained as continuous text:

```markdown
Name: John Doe
Gender: Male
Phone: 13800000000
Address: Shanghai...
```

Splitting each field into a separate note would increase maintenance cost and lose context. Snippets should therefore support snippet markers inside a note, so one continuous document can derive multiple Quick Copy items.

The marker rule is intentionally simple: **Note files are always Markdown; inside Markdown content, Snippets looks for embedded marker tags with `data-copy-*` attributes**.

The note remains Markdown. Normal content is unaffected. Only content that needs quick-copy behavior is wrapped in marker tags. The copy value defaults to the tag's normalized `innerText`.

Example:

```markdown
<section data-copy-id="profile.full" data-copy-label="Full profile">
  Name: <span data-copy-id="profile.name" data-copy-label="Name">John Doe</span>
  Gender: <span data-copy-id="profile.gender" data-copy-label="Gender">Male</span>
  Phone: <span data-copy-id="profile.phone" data-copy-label="Phone">13800000000</span>
</section>
```

In a Markdown note:

```markdown
# Profile

<section data-copy-id="profile.full" data-copy-label="Full profile">
  Name: <span data-copy-id="profile.name" data-copy-label="Name">John Doe</span>
  Gender: <span data-copy-id="profile.gender" data-copy-label="Gender">Male</span>
  Phone: <span data-copy-id="profile.phone" data-copy-label="Phone">13800000000</span>
</section>

## Notes

This information is maintained as continuous text. The Quick Copy list is derived from `data-copy-*` tags.
```

Parsing rules:

1. Any allowed element with `data-copy-id` is a Quick Copy node.
2. `data-copy-label` is the display label; if omitted, it falls back to `data-copy-id`.
3. The copy value defaults to the element's normalized `innerText`.
4. Nested copy nodes are allowed; the parent copies the full content, while children copy local content.
5. Only `span`, `div`, and `section` may carry `data-copy-*` markers: `span` is for inline snippets, while `div` and `section` are for block snippets.
6. These tags are only structural markers. They do not mean that Note supports standalone HTML files or full HTML rendering. Other HTML tags are not treated as Quick Copy markers.

Derived Quick Copy items should include:

| Field | Description |
|---|---|
| `id` | Stable identifier, for example `profile.phone`. |
| `label` | Display label, for example `Phone`. |
| `value` | Actual copied content, defaulting to the element's normalized `innerText`. |
| `source` | Source note path and location. |
| `updated` | Source note update time. |

The Quick Copy list should not become another manually maintained data source. It is a derived view: users maintain notes, and the app generates copyable snippets.

#### App rendering

To make `data-copy-*` markers reliable to maintain, the app needs a copy-aware Markdown rendering experience:

```text
+---------------- Note Editor ----------------+------ Quick --------+
| Source                                      | Full profile [Copy] |
| Rendered preview                            | Name         [Copy] |
|                                             | Gender       [Copy] |
|                                             | Phone        [Copy] |
+---------------------------------------------+---------------------+
```

Minimum interaction:

- Edit the real `.md` content on the left.
- Render Markdown in the preview.
- Show all Quick Copy items derived from the current note in a right-side Quick panel.
- Clicking a copy button in the Quick panel copies the corresponding normalized `innerText`.
- If `data-copy-id` is duplicated, tags are not closed, or parsing fails, show the issue directly in the panel.

### Jobs

Jobs is the project Job Runner. It manages "when to trigger" and "what to execute". It comes from the Clip polling requirement: the original Clipboard feature checks for clipboard changes every second. In Snippets, that capability should not stay hidden inside Clip; it should be modeled as a unified trigger + action system.

#### Trigger

A trigger describes when a job runs:

| Type | Description |
|---|---|
| `startup` | Runs once after the Snippets process starts and finishes initialization. If the app starts with the system, this naturally fires after login. |
| `manual` | Does not run automatically. It can be triggered by the user, IPC, or a UI button. |
| `interval` | Runs repeatedly at a fixed interval, for example every `1s`. |
| `cron` | Runs from a 5-field or 6-field cron expression. Six-field expressions include seconds, for example `*/2 * * * * *` runs every two seconds. |

#### Action

An action describes what a job executes:

| Type | Description |
|---|---|
| `tool` | Calls an internal Snippets tool, such as the planned `clip.poll` and `clip.prune`. Tools run in-process in the app first. |
| `command` | Starts an external command, for example `node scripts/backup.js`. |

`tool` is the unified application capability model. It is not an internal message and it does not expose the invocation mechanism. Jobs only care about the tool name and arguments. Today, tools are executed directly by App/Core handlers. If external MCP clients need access to Core capabilities in the future, that should be provided by a separate `Snippets.Mcp` project, not by exposing an MCP server from the GUI app.

MVP supports both action types: `tool` for in-process capability calls, and `command` for external process execution.

#### Job configuration

```yaml
id: clip-poll
name: clipboard watcher
trigger:
  type: interval
  every: 1s
action:
  type: tool
  name: clip.poll
  args: {}
enabled: true
```

External commands use `command`:

`command` and every `args` entry support `${USERPROFILE}`, `${LOCALAPPDATA}`, and `${workspace.root}` expansion.

```yaml
id: backup-daily
name: daily backup
trigger:
  type: cron
  expression: "0 0 2 * * *"
action:
  type: command
  command: node
  args:
    - scripts/backup.js
  env:
    NODE_ENV: production
enabled: true
```

### App and tray

Snippets shows a tray icon while it is running, so users can bring the main window back and copy a small set of Quick items directly. If `app.closeToTray` is enabled, closing the main window hides it and keeps Snippets running in the tray; otherwise closing the main window quits the app.

Current tray menu scope:

- Open the main window.
- Read Quick items from `quick.md` in the Notes drafts directory and copy the clicked item.
- Show up to `app.trayQuickLimit` Quick items in the tray menu; use the Notes page for the full list.
- Rebuild the tray Quick menu after Notes are saved or refreshed.
- Quit the app.

The current settings UI intentionally exposes only two app-level switches:

- Start with system.
- Keep running in system tray.

Start with system is an app-level capability, not a job trigger. When enabled, Snippets starts after system login. Once the process finishes initialization, jobs with a `startup` trigger run according to the Jobs rules.

## Configuration draft

The default config file should live in the user's home directory:

- Windows: `%USERPROFILE%\snippets-config.yml`
- macOS/Linux: `~/snippets-config.yml`

```yaml
schema: snippets-v1

workspace:
  root: "${USERPROFILE}/Documents/Snippets"

app:
  closeToTray: false
  startWithSystem: false
  trayQuickLimit: 10
  logs: "${LOCALAPPDATA}/Snippets/logs"

clips:
  autoSave: "${workspace.root}/Clips/AutoSave"
  favorites: "${workspace.root}/Clips/Favorites"
  maxAutoSave: 100
  dedupeCacheWindow: 10m

notes:
  drafts: "${workspace.root}/Notes/Drafts"

jobs:
  enabled: true
  items:
    - id: clip-poll
      name: clipboard watcher
      trigger:
        type: interval
        every: 1s
      action:
        type: tool
        name: clip.poll
        args: {}
      enabled: true

    - id: clip-prune
      name: prune clips
      trigger:
        type: startup
      action:
        type: tool
        name: clip.prune
      enabled: true
```

## Initial scope

### MVP

- Provide the Clip data model, file naming, save directories, favorites directory, and auto-save limit behavior.
- Provide basic clipboard watching and clipboard history.
- Support Clip favorite, delete, copy back to clipboard, and reveal in file manager.
- Provide Note create, edit, and delete.
- Support `data-copy-*` markers in Notes for Quick Copy snippets.
- Derive a Quick Copy list from Notes.
- Support Note source editing, rendered preview, and the Quick panel.
- Support system tray residency.
- Support a bounded tray Quick menu sourced from `quick.md`.
- Support the minimal settings UI for start with system and close-to-tray behavior.
- Support start with system.
- Support the local config file.
- Support Jobs Runner by modeling Clip polling as a `trigger: interval` + `action: tool` job named `clip.poll`.
- Support Jobs Runner external `command` actions.
- Support at least one maintenance job: run `clip.prune` on startup to clean AutoSave Clips.

### Later enhancements

- `Snippets.Mcp`: if external MCP clients need access to Core capabilities, build a separate MCP interface layer.

## Suggested architecture

Technology choices:

- GUI uses **Avalonia + .NET 10**.
- Keep `Snippets.Core` as the reusable core for Clip, Note, Jobs, Config, and other cross-UI logic.
- `Snippets.Core` does not depend on Avalonia, so it can be reused by a future standalone `Snippets.Mcp` project.

```text
src/
  Snippets.Core/
    Clips/
    Notes/
    Jobs/
    Config/
  Snippets.App/
    Views/
    ViewModels/
    Services/
  # Future:
  # Snippets.Mcp/                  # optional MCP interface over Snippets.Core
```

Responsibilities:

| Module | Responsibility |
|---|---|
| Core | Data models, file storage, config loading, Jobs Runner, tool registration and execution, and other cross-UI logic. |
| App | Avalonia desktop app for Clip, Note, Jobs, tray menu, and settings UI. |
| Mcp | Future optional project that exposes an MCP interface over `Snippets.Core`; not part of the MVP. |

## Data storage conventions

The project should avoid locking core content into a proprietary database. Recommended conventions:

- Clip payloads continue to be stored as raw files.
- Notes are stored only as Markdown files, defaulting to `Notes/Drafts/`.
- The Quick Copy list is derived from Note markers and is not manually maintained primary data.
- Runtime logs are stored under the local app data directory. On Windows, the default path is `%LocalAppData%\Snippets\logs`.
- Job run records may be written to logs or state files, but they must not become the only source of core data.
