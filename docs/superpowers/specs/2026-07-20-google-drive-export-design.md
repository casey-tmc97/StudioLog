# Google Drive Export — Design

## Summary

Add a "Google Drive" option under the existing Export menu so PDF/CSV/PNG session logs can be uploaded straight to a Google Drive Shared Drive folder, instead of only saving locally.

## Background

StudioLog currently exports session logs to local files via `ExportManager` (PDF/CSV/PNG), triggered from `MainViewModel.ExportPdf/Csv/Png()` and a save-file dialog. The studio's Drive structure organizes session material under a Shared Drive named "Production" > "Artists" > (artist/session folders). This feature lets casey skip the local-file-then-manually-upload workflow.

## Google Cloud setup (completed)

- New Google Cloud project `studiolog-503004`, org `texasmusiccafe.org`.
- Drive API enabled.
- OAuth consent screen configured with **Internal** audience (restricted to texasmusiccafe.org accounts) — this avoids both the Google verification process and the 7-day refresh-token expiry that applies to unverified External apps.
- OAuth 2.0 Client ID created, type **Desktop app**, name "StudioLog Desktop". The Client ID/Secret are not recorded in this repo (public repo — see below); casey has them saved separately.

Although Google's own documentation treats "installed app" OAuth secrets as not fully confidential, this repo is **public**, so the values are kept out of source and out of documentation entirely rather than embedded as constants. Each machine running StudioLog's Drive export needs a local `%AppData%\StudioLog\google-credentials.json` file (never committed — see `.gitignore`) shaped like:

```json
{
  "ClientId": "...",
  "ClientSecret": "..."
}
```

## Architecture

### `GoogleDriveManager.cs` (new, `Core/`)

Responsibilities:
- **Auth**: `GoogleWebAuthorizationBroker.AuthorizeAsync` (from `Google.Apis.Auth`) with the embedded client ID/secret, scope `https://www.googleapis.com/auth/drive`. Opens the system browser for consent on first use; uses `FileDataStore` pointed at `%AppData%\StudioLog\google-token\` to persist and silently refresh tokens afterward — same directory convention as `settings.json` and `timecode.db`.
  - Full `drive` scope (not the narrower `drive.file`) is required because casey needs to browse and upload into *existing* Shared Drive folders the app didn't create, which `drive.file` cannot see.
- **Drive browsing**: methods to list Shared Drives, find a Shared Drive by name, and list child folders of a given folder (`Files.List` with `supportsAllDrives=true`, `includeItemsFromAllDrives=true`, filtered to `mimeType='application/vnd.google-apps.folder'`).
- **Upload**: `UploadFile(string localPath, string mimeType, string parentFolderId, string driveId)` using `Files.Create` with `supportsAllDrives=true`.
- **Disconnect**: deletes the `google-token` directory to force re-authentication.

### `DriveFolderPickerDialog.axaml` / `.axaml.cs` (new)

A modal dialog, structurally similar to the existing `CompanionSettingsDialog`:
- On open, resolves the Shared Drive named "Production", then its "Artists" subfolder, and lists Artists' contents as the starting view.
  - If "Production" or "Artists" can't be found (renamed, no access, first-run before drive is shared), falls back to listing all Shared Drives at the root rather than erroring out — casey can navigate manually from there.
- UI: breadcrumb trail (Shared Drives / Production / Artists / ...), a list of child folders (double-click to descend), an "Up" action, "Select This Folder" (confirm) and "Cancel" buttons.
- No persistence between exports — every invocation starts back at Production/Artists (or the fallback root), per explicit preference: casey wants to pick fresh each time rather than defaulting to a remembered folder.
- Returns the selected `(folderId, driveId)` pair, or `null` on cancel.

### Export flow (`MainViewModel.cs`)

New commands, parallel to the existing `ExportPdfCommand`/`ExportCsvCommand`/`ExportPngCommand`:
- `ExportPdfToDriveCommand`, `ExportCsvToDriveCommand`, `ExportPngToDriveCommand`

Each:
1. Generates the export to a temp file path (`Path.GetTempFileName()`-style, correct extension) via the existing `ExportManager.ExportToPdf/Csv/Png` methods — no save-file dialog shown.
2. Shows `DriveFolderPickerDialog`. If the user cancels, deletes the temp file and returns without changing `StatusMessage`.
3. On folder selection, calls `GoogleDriveManager.UploadFile(...)`.
4. Deletes the temp file.
5. Updates `StatusMessage` with success (`"PDF uploaded to Google Drive: <filename>"`) or failure (`"Drive upload failed: <message>"`), matching the try/catch/status pattern of the existing `ExportPdf()`/`ExportCsv()`/`ExportPng()` methods.

### Menu (`MainWindow.axaml`)

Under the existing `Export` `MenuItem` (`ExportMenuItem`), add a `Google Drive` submenu parallel to the current PDF/CSV/PNG items:

```
Export
├── Export to PDF
├── Export to CSV
├── Export to PNG
├── ─────────────
└── Google Drive
    ├── Export to PDF
    ├── Export to CSV
    └── Export to PNG
```

### Settings

Add a "Disconnect Google Drive" button to the settings UI (alongside the existing Companion Control settings) that calls `GoogleDriveManager`'s disconnect method, so casey can revoke/switch the linked account without manually deleting AppData files.

## Dependencies

Add to `StudioLog.csproj`:
- `Google.Apis.Drive.v3`
- `Google.Apis.Auth`

## Error handling

- No network / auth failure / revoked access → caught in the export command, surfaced via `StatusMessage`, consistent with existing export error handling. No new error-handling abstractions introduced.
- Folder picker's Production/Artists lookup failure is not a hard error — falls back to root Shared Drives listing.

## Out of scope

- No persistence of last-used Drive folder (explicit choice: ask every time).
- No support for uploading to "My Drive" specifically as a distinct entry point — Shared Drives (starting at Production/Artists) is the only path, matching the studio's actual usage.
- No re-authentication UI beyond "Disconnect" — reconnecting is just triggering another export, which re-opens the consent flow if no valid token exists.
