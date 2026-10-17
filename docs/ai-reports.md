# AI Notes, Recommendations and Bug Reports

An AI agent connected to the [AI endpoint](ai-endpoint.md) can write down what it finds while it plays
and mods the game: notes, recommendations and bug reports. The host saves each one as JSON and Markdown,
fills in the game's state for the agent, and keeps enough with a bug to reproduce it. People read them in
the in-game **AI Reports** window or straight from the folder.

## Where reports go

Every run of the host is a session with its own folder under the game's user data folder:

```
user://ai_reports/
  20261017-183845/          one folder per run, named after when it started (UTC)
    index.md                every report in this session, oldest first
    note-001.json           the report as data
    note-001.md             the same report for reading
    bug-001.json
    bug-001.md
    bug-001.replay.json     the calls that led up to the bug, as a recording
    bug-001.png             a screenshot, when the host has a screen
```

`user://` is `~/.local/share/godot/app_userdata/Mechanica` on Linux,
`%APPDATA%\Godot\app_userdata\Mechanica` on Windows and
`~/Library/Application Support/Godot/app_userdata/Mechanica` on macOS. A session keeps at most 5000
reports.

## Methods

All of them need the `observe` scope except `reports.delete`, which needs `admin`. Text fields may be
Markdown. Titles, steps and areas are one line. Tags are 1 to 32 lower case letters, digits, `-` or `_`.
`author` is optional: who is writing (a model or agent name); the connection's name otherwise.

| Method | Params | Result |
|--------|--------|--------|
| `reports.note` | `title`, optional `text`, `tags`, `screenshot` (default false), `author` | the saved report |
| `reports.recommend` | `title`, `text` (what to change and why), optional `area` (a feature, tool, part or mod id), `priority` (`low`, `medium`, `high`; default `medium`), `tags`, `screenshot`, `author` | the saved report |
| `reports.bug` | `title`, `steps` (1 to 50), `expected`, `actual`, optional `severity` (`low`, `medium`, `high`, `critical`; default `medium`), `text`, `area`, `tags`, `screenshot` (default true), `replay_actions` (most calls to keep; default every call since the world started, 0 for none), `messages` (recent log lines to keep, 0 to 200, default 50), `author` | the saved report |
| `reports.list` | optional `kind` (`note`, `recommendation`, `bug`), `session` (a folder name or `current`), `tag`, `limit` (1 to 500, default 50) | summaries, newest first: `id`, `kind`, `created`, `author`, `title`, `tags`, `severity`, `priority`, `has_screenshot` |
| `reports.get` | `id`, optional `replay` and `screenshot` (both default false) | the report; with `replay` its recording under `replay`, with `screenshot` the PNG as base64 under `screenshot_png` |
| `reports.delete` | `id` | `true` |
| `reports.screenshot` | none | `{png, width, height}`: what the host's screen shows now, PNG as base64 |

A report's `id` is `<session>/<file>`, for example `20261017-183845/bug-002`. Bad fields answer with
`-32602` (`bad_args`) and a message saying what to fix; `reports.screenshot` on a headless host answers
with `-32004` and says why. Each new report is also pushed as an event on the `reports` topic (its
summary) to connections subscribed to it.

```json
→ {"jsonrpc": "2.0", "id": 7, "method": "reports.bug", "params": {
    "title": "Wheel sinks into the ground after a reload",
    "steps": ["Place a wheel on a block", "Save and load the world", "Drive forward"],
    "expected": "The car drives off",
    "actual": "The front wheel sinks half into the ground and the car spins",
    "severity": "high", "area": "vanilla:wheel", "tags": ["physics", "saves"]}}
← {"jsonrpc": "2.0", "result": {"id": "20261017-183845/bug-001", "kind": "bug", ...}, "id": 7}
```

## What the host adds

Every report gets a `context`: game version, protocol version, content fingerprint and mods, world name,
the save it was loaded from, seed, mode, difficulty, physics tick, world time, whether the host is
headless, and every player with their position. A bug also gets:

- **A replay slice.** The host always keeps the state-changing Action API calls since the world started
  (up to 20 000, from every player, bot and agent; queries and part scripts are left out, as in a
  recording). The bug's `.replay.json` is in the [recording format](ai-endpoint.md#recording-and-replay),
  and its Markdown gives the command that starts a host like the reporter's and replays it:

  ```sh
  godot --path game -- --host --seed 1234 --survival --replay bug-001.replay.json
  ```

  `replay_truncated` is true when early calls were dropped (or `replay_actions` kept only the last few), so
  a replay into a fresh world may not reach the same state.
- **Recent messages.** Part script and bot prints, warnings and errors, Action API calls that failed on the
  host, and the host's own errors and warnings, with the tick each was logged on.
- **A screenshot** of the host's screen, unless `screenshot` is false. A headless host keeps none and says
  so in `screenshot_error`.

## The AI Reports window

Open it from the in-game menu (Esc, then **AI reports…**). It lists the reports in the reports folder,
newest first; bugs of high or critical severity are red. Filter by kind and by session, then pick a report
to read it in full with its screenshot.

- **Refresh** reads the folder again, for reports another game wrote (a headless host on the same machine
  writes to the same folder). Reports this game writes appear by themselves.
- **Open folder** shows the report's session folder in the file manager.
- **Copy** puts the report's Markdown on the clipboard, ready to paste into an issue.
- **Delete** asks first, then removes the report and its files.
