# Bitfocus Companion Module — Design Spec

**Date:** 2026-07-06
**Status:** Approved

---

## Overview

Add remote-control support for Bitfocus Companion, covering exactly four of StudioLog's existing functions: **Generate/Stop** (toggle), **Timecode In**, **Timecode Out**, and **Mark**. This is a two-repo change:

1. **StudioLog** (this repo) gains an opt-in TCP control server that exposes those four functions and pushes live state.
2. **`companion-module-studiolog`** (new, separate repo) is a standard Bitfocus Companion module that connects to that server, exposing 4 button actions, 3 variables (usable in Companion Triggers), and 2 feedbacks.

No authentication is used — the server binds to all interfaces so Companion can run on the same PC or elsewhere on the LAN, which means this is a trusted-local-network protocol by design (same trust model as OSC/NDI/similar AV control protocols already used by this app). This is a deliberate scope decision, not an oversight.

---

## Architecture

```
┌─────────────────────┐        TCP (line-based text protocol)        ┌──────────────────────────┐
│   StudioLog (.NET)   │ <───────────────────────────────────────────> │  Companion module (Node)  │
│  CompanionControl     │   persistent socket, port configurable        │  companion-module-studiolog│
│  Server (opt-in)      │   pushes STATE lines on change                │  (separate repo)           │
└─────────────────────┘                                                └──────────────────────────┘
```

---

## StudioLog-side changes

### New file: `CompanionControlServer.cs`

- Wraps a `TcpListener` bound to `IPAddress.Any` on a configurable port.
- Accepts multiple simultaneous client connections (so a reconnect, or Companion + a Satellite device, don't fight each other). State is broadcast to all connected clients.
- On each new client connection, immediately sends the full current state (the 3 `STATE` lines below) so variables initialize correctly without waiting for the next change.
- Parses incoming newline-terminated command lines and dispatches to the **existing** ViewModel commands — `ToggleGeneratorCommand`, `TimeCodeInCommand`, `TimeCodeOutCommand`, `TimeCodeMarkCommand` — marshaled onto the UI thread via `Dispatcher.UIThread.InvokeAsync`. This reuses all current validation/session logic (e.g. "no active session," "no open entry") untouched — the server is a thin transport layer, not a reimplementation.
- Subscribes to `PropertyChanged` on `IsGeneratorRunning`, `IsTimecodeInActive`, and `CurrentTimecode` on the `MainViewModel`, pushing a `STATE` line to all connected clients whenever one changes. `CurrentTimecode` already only updates at 10Hz (the existing 100ms display timer), so no additional throttling is needed.
- Malformed/unrecognized command lines are ignored (logged to console, not surfaced to the user).

### Wire protocol

Newline (`\n`) terminated ASCII lines.

**Client → server** (one command per line, no arguments):
```
GENERATE_TOGGLE
TC_IN
TC_OUT
MARK
```

**Server → client** (pushed on connect, and again whenever a value changes):
```
STATE GENERATOR RUNNING|STOPPED
STATE TC_IN_ACTIVE TRUE|FALSE
STATE TIMECODE HH:MM:SS:FF
```

### Settings

- `AppSettings.cs` gains:
  - `CompanionControlEnabled` (bool, default `false`)
  - `CompanionControlPort` (int, default `51234`)
- New dialog `CompanionSettingsDialog.axaml`/`.axaml.cs`, following the existing `UpdateConfirmDialog`/`WhatsNewDialog` pattern, reachable from the `SETTINGS` menu as a new `COMPANION CONTROL...` item. Contains an Enable checkbox and a numeric port field.
- Applying the dialog starts/stops/restarts `CompanionControlServer` live (matching how other settings, e.g. audio device selection, apply immediately without an app restart).
- Startup failures (e.g. port already in use) are caught and surfaced via the existing `StatusMessage` bar (e.g. "Companion Control: port 51234 already in use") rather than crashing the app.

---

## Companion module (`companion-module-studiolog`, new repo)

**Location:** `C:\Users\Admin\Documents\GitHub\companion-module-studiolog` (sibling to this repo).

Standard `@companion-module/base` structure:

```
companion-module-studiolog/
  companion/
    manifest.json          # id: studiolog, name: StudioLog, entrypoint main.js
  package.json
  src/
    main.js                 # InstanceBase subclass: init/destroy/configUpdated
    connection.js            # persistent TCP client: connect, line-buffer, auto-reconnect w/ backoff
    actions.js               # setActionDefinitions
    variables.js              # setVariableDefinitions / setVariableValues
    feedbacks.js               # setFeedbackDefinitions
  README.md
```

### Config fields

- **Host** (text, default `127.0.0.1`)
- **Port** (number, default `51234` — matches StudioLog's default)

### Actions (4 buttons)

| Action | Command sent |
|---|---|
| Generate/Stop | `GENERATE_TOGGLE` |
| Timecode In | `TC_IN` |
| Timecode Out | `TC_OUT` |
| Mark | `MARK` |

### Variables

Updated live as `STATE` lines arrive, usable in Companion's Trigger system (e.g. "run when `$(studiolog:generator_running)` changes to `true`"):

- `generator_running` → `true` / `false`
- `tc_in_active` → `true` / `false`
- `current_timecode` → `HH:MM:SS:FF`

### Feedbacks

Boolean-style feedbacks, matching the app's own button coloring (`GeneratorButtonColor`/`GeneratorButtonText` in `MainViewModel.cs`):

- `generator_running` — background `#dc2626` (red), text override `STOP`, when true. User configures the button's default (false) style as green/`GENERATE`.
- `tc_in_active` — amber background overlay when true, no style override when false.

### Connection behavior

- Persistent TCP socket with auto-reconnect (exponential backoff) if StudioLog closes or restarts.
- Companion's built-in connection status indicator (connecting/ok/error) reflects the TCP connection state.

---

## Testing / Verification Plan

No automated test suite (this app has no existing test project; verification is manual, matching prior features in this repo):

1. **StudioLog side:** throwaway TCP client script (PowerShell/netcat) connects to the port, sends each command line, confirms the corresponding app action fires and `STATE` lines are pushed correctly, including immediately on connect.
2. **Module side:** load the module into a local Companion instance via "Developer: local module," connect to a running StudioLog instance, press each of the 4 buttons on a Companion page, confirm the action fires in StudioLog, variables update in Companion's variable inspector, and feedback colors change correctly.
3. **Reconnect behavior:** stop StudioLog while the module is connected, confirm the module shows "connecting" and reconnects cleanly when StudioLog restarts, with no reconfiguration needed.
4. **Edge cases:** TC OUT with no open entry, MARK with no active session — confirm these no-op gracefully (already handled by existing ViewModel code) without crashing the TCP server or leaving the module in a bad state.

---

## Out of Scope

- Authentication/encryption on the control protocol (trusted-LAN assumption, explicit tradeoff — see Overview).
- Separate explicit Start/Stop actions (single toggle only, matching the app's existing button).
- Variables/feedbacks for last TC-in/TC-out/mark *values* (only live state + live timecode, per scope decision).
- Publishing the module to the official Companion module registry (local/custom module only, for now).
- Automated tests for `CompanionControlServer`.
