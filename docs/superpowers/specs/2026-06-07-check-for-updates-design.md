# Check for Updates — Design Spec

**Date:** 2026-06-07
**Status:** Approved

---

## Overview

Add a "Check for Updates" feature to the HELP menu that queries the GitHub Releases API, downloads the latest installer if a newer version is available, and silently installs it while closing the app.

---

## Architecture

### New file: `UpdateService.cs`

A self-contained service class in the root `StudioLog` namespace. Responsible for:

- Querying `https://api.github.com/repos/casey-tmc97/StudioLog/releases/latest`
- Parsing the `tag_name` field (e.g. `v2.1.2`) into a `System.Version`
- Comparing against the running assembly version (`Assembly.GetExecutingAssembly().GetName().Version`)
- Finding the `StudioLog-vX.X.X-Setup.exe` asset in the release's `assets` array
- Downloading the asset to `%TEMP%\StudioLog-Setup.exe` with progress reporting
- Launching the installer with `/VERYSILENT /NORESTART` then shutting down the app

### `MainViewModel.cs` changes

- New `ICommand CheckForUpdatesCommand` wired to manual menu click
- Startup call: `_ = _updateService.CheckSilentlyAsync()` inside the constructor, delayed 3 seconds via `Task.Delay`
- Silent check sets `StatusMessage` if update found; swallows all exceptions
- Manual check drives full UI flow (status messages, dialogs)

---

## UI / UX

### Menu item

Added to the HELP menu after CONTACT, with a separator:

```
HELP
  MANUAL
  ABOUT
  CONTACT
  ──────────────────
  CHECK FOR UPDATES
```

### Manual flow

1. Status bar → "Checking for updates..."
2. **Up to date** → dialog: "You're on the latest version (vX.X.X)." — OK button
3. **Update found** → dialog: "Version vX.X.X is available. Download and install now? StudioLog will close automatically." — Install / Cancel buttons
4. **Install clicked** → status bar shows "Downloading update... X MB / Y MB", then app closes and installer runs silently

### Startup silent check

- Runs 3 seconds after launch (fire-and-forget, no UI block)
- If update found → status bar: "Update available: vX.X.X — Help > Check for Updates to install"
- No dialog shown; all exceptions swallowed silently

---

## Error Handling

| Scenario | Silent check | Manual check |
|---|---|---|
| No internet / GitHub unreachable | Silently ignored | "Could not reach update server. Check your connection and try again." |
| GitHub API rate limit | Silently ignored | Treated same as unreachable |
| Download interrupted | N/A | Temp file deleted. "Update download failed. Try again later." |
| Installer launch fails | N/A | "Could not launch installer. Please download manually from github.com/casey-tmc97/StudioLog/releases" |

All errors on the silent path are caught and discarded. Errors on the manual path set `StatusMessage`.

---

## Implementation Notes

- No new NuGet packages required — uses `System.Net.Http.HttpClient`, `System.Diagnostics.Process`, and `System.Reflection`
- GitHub API requires a `User-Agent` header — use `StudioLog/<version>`
- Download progress reported via `IProgress<(long downloaded, long total)>`
- Temp installer path: `Path.Combine(Path.GetTempPath(), "StudioLog-Setup.exe")`
- Installer flags: `/VERYSILENT /NORESTART` (Inno Setup)
- App shutdown: `(Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown()`
