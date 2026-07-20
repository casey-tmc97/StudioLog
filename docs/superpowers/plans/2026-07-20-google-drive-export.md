# Google Drive Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Google Drive" option under the existing Export menu so PDF/CSV/PNG session logs can be generated and uploaded straight into a Shared Drive folder (starting at Production/Artists), instead of only saving to a local file.

**Architecture:** A new `GoogleDriveManager.cs` (namespace `StudioLog.Core`, same convention as `ExportManager.cs`) owns OAuth auth (via `Google.Apis.Auth`'s installed-app flow, token cached on disk) and all Drive API calls (list Shared Drives, list child folders, upload). A new `DriveFolderPickerDialog` (plain code-behind `Window`, same style as `CompanionSettingsDialog`) lets casey browse Shared Drives and pick a destination folder. `MainViewModel` adds three new export commands that reuse the existing `ExportManager` methods to build a temp file, then show the picker and hand the file to `GoogleDriveManager` for upload — mirroring the existing `ExportPdf`/`ExportCsv`/`ExportPng` try/catch/`StatusMessage` pattern.

**Tech Stack:** .NET 9 / Avalonia (existing), new NuGet packages `Google.Apis.Drive.v3` and `Google.Apis.Auth`.

## Global Constraints

- StudioLog targets `net9.0` (existing `StudioLog.csproj`).
- OAuth 2.0 Client (Desktop app type, project `studiolog-503004`, Internal audience — restricted to `texasmusiccafe.org` accounts, no verification needed, no 7-day refresh-token expiry). This repo is **public**, so the Client ID/Secret are kept out of source and docs entirely — `GoogleDriveManager` loads them at runtime from `%AppData%\StudioLog\google-credentials.json` (gitignored, not committed), per the approved spec (`docs/superpowers/specs/2026-07-20-google-drive-export-design.md`).
- Drive scope is the full `https://www.googleapis.com/auth/drive` (not `drive.file`) — required to browse pre-existing Shared Drive folders the app didn't create.
- Token cache lives at `%AppData%\StudioLog\google-token\`, matching the existing `%AppData%\StudioLog\` convention used by `settings.json` and `timecode.db`.
- No automated tests are added — StudioLog has no existing test project (confirmed: no `*Test*` files anywhere in the repo), consistent with the precedent set in `docs/superpowers/plans/2026-07-06-companion-module.md`. Verification is build + manual end-to-end testing (Task 6).
- Folder picker starts at Shared Drive "Production" > folder "Artists"; if either can't be found, it falls back to listing all Shared Drives at the root. No persistence of the last-used folder between exports.
- All new/modified C# files follow existing repo conventions: files live at the repo root (flat structure, e.g. `ExportManager.cs`, `CompanionSettingsDialog.axaml.cs` are root-level despite differing namespaces), 4-space indentation, `StatusMessage` updates wrapped in `Avalonia.Threading.Dispatcher.UIThread.InvokeAsync`.

---

### Task 1: Add Google API NuGet packages

**Files:**
- Modify: `StudioLog.csproj`

**Interfaces:**
- Produces: `Google.Apis.Drive.v3` and `Google.Apis.Auth` packages available to all later tasks.

- [ ] **Step 1: Add the packages**

```powershell
dotnet add StudioLog.csproj package Google.Apis.Drive.v3
dotnet add StudioLog.csproj package Google.Apis.Auth
```

- [ ] **Step 2: Verify the packages were added**

Open `StudioLog.csproj` and confirm two new `<PackageReference>` lines were added under the existing `<ItemGroup>` containing `QuestPDF`/`SkiaSharp`, e.g.:

```xml
    <PackageReference Include="Google.Apis.Drive.v3" Version="1.68.0.3520" />
    <PackageReference Include="Google.Apis.Auth" Version="1.68.0" />
```

(Exact version numbers are whatever `dotnet add package` resolved — do not hand-edit them.)

- [ ] **Step 3: Verify it restores and builds**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add StudioLog.csproj
git commit -m "chore: add Google Drive API NuGet packages"
```

---

### Task 2: Create GoogleDriveManager.cs

**Files:**
- Create: `GoogleDriveManager.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (standalone Drive API wrapper), depends only on the packages from Task 1.
- Produces: `StudioLog.Core.GoogleDriveManager` with:
  - `class DriveFolder { public string Id; public string Name; public string? DriveId; }`
  - `Task<List<DriveFolder>> ListSharedDrivesAsync(CancellationToken ct = default)`
  - `Task<List<DriveFolder>> ListChildFoldersAsync(string folderId, string? driveId, CancellationToken ct = default)`
  - `Task<DriveFolder?> FindSharedDriveByNameAsync(string name, CancellationToken ct = default)`
  - `Task<DriveFolder?> FindChildFolderByNameAsync(string parentFolderId, string? driveId, string name, CancellationToken ct = default)`
  - `Task UploadFileAsync(string localPath, string mimeType, string parentFolderId, string? driveId, CancellationToken ct = default)`
  - `void Disconnect()`
  - Implements `IDisposable`
  - Consumed by Task 3 (picker calls the `List*`/`Find*` methods) and Task 4 (`MainViewModel` calls `UploadFileAsync`/`Disconnect`).

- [ ] **Step 1: Write GoogleDriveManager.cs**

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;

namespace StudioLog.Core
{
    public class GoogleDriveManager : IDisposable
    {
        // NOTE: superseded during implementation — Client ID/Secret are not embedded as
        // constants (this repo is public). See GoogleDriveManager.cs's actual LoadCredentials()
        // method, which reads them from %AppData%\StudioLog\google-credentials.json instead.
        private static readonly string[] Scopes = { DriveService.Scope.Drive };

        private static readonly string TokenStorePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StudioLog",
            "google-token"
        );

        private DriveService? _driveService;

        public class DriveFolder
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
            public string? DriveId { get; set; }
        }

        private async Task<DriveService> GetServiceAsync(CancellationToken ct)
        {
            if (_driveService != null) return _driveService;

            var creds = LoadCredentials(); // see NOTE above
            var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                new ClientSecrets { ClientId = creds.ClientId, ClientSecret = creds.ClientSecret },
                Scopes,
                "user",
                ct,
                new FileDataStore(TokenStorePath, true));

            _driveService = new DriveService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "StudioLog"
            });

            return _driveService;
        }

        public async Task<List<DriveFolder>> ListSharedDrivesAsync(CancellationToken ct = default)
        {
            var service = await GetServiceAsync(ct);
            var request = service.Drives.List();
            request.PageSize = 100;
            var result = await request.ExecuteAsync(ct);

            return (result.Drives ?? new List<Google.Apis.Drive.v3.Data.Drive>())
                .Select(d => new DriveFolder { Id = d.Id, Name = d.Name, DriveId = d.Id })
                .OrderBy(f => f.Name)
                .ToList();
        }

        public async Task<List<DriveFolder>> ListChildFoldersAsync(string folderId, string? driveId, CancellationToken ct = default)
        {
            var service = await GetServiceAsync(ct);
            var request = service.Files.List();
            request.Q = $"'{folderId}' in parents and mimeType='application/vnd.google-apps.folder' and trashed=false";
            request.Fields = "files(id, name)";
            request.SupportsAllDrives = true;
            request.IncludeItemsFromAllDrives = true;
            if (driveId != null)
            {
                request.DriveId = driveId;
                request.Corpora = "drive";
            }

            var result = await request.ExecuteAsync(ct);

            return (result.Files ?? new List<Google.Apis.Drive.v3.Data.File>())
                .Select(f => new DriveFolder { Id = f.Id, Name = f.Name, DriveId = driveId })
                .OrderBy(f => f.Name)
                .ToList();
        }

        public async Task<DriveFolder?> FindSharedDriveByNameAsync(string name, CancellationToken ct = default)
        {
            var drives = await ListSharedDrivesAsync(ct);
            return drives.FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<DriveFolder?> FindChildFolderByNameAsync(string parentFolderId, string? driveId, string name, CancellationToken ct = default)
        {
            var children = await ListChildFoldersAsync(parentFolderId, driveId, ct);
            return children.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public async Task UploadFileAsync(string localPath, string mimeType, string parentFolderId, string? driveId, CancellationToken ct = default)
        {
            var service = await GetServiceAsync(ct);

            var fileMetadata = new Google.Apis.Drive.v3.Data.File
            {
                Name = Path.GetFileName(localPath),
                Parents = new List<string> { parentFolderId }
            };

            await using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read);
            var request = service.Files.Create(fileMetadata, stream, mimeType);
            request.SupportsAllDrives = true;

            var progress = await request.UploadAsync(ct);
            if (progress.Status != Google.Apis.Upload.UploadStatus.Completed)
            {
                throw new Exception($"Upload did not complete: {progress.Exception?.Message}", progress.Exception);
            }
        }

        public void Disconnect()
        {
            _driveService?.Dispose();
            _driveService = null;

            if (Directory.Exists(TokenStorePath))
            {
                Directory.Delete(TokenStorePath, true);
            }
        }

        public void Dispose()
        {
            _driveService?.Dispose();
        }
    }
}
```

- [ ] **Step 2: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```powershell
git add GoogleDriveManager.cs
git commit -m "feat: add GoogleDriveManager for Drive OAuth, browsing, and upload"
```

---

### Task 3: Create DriveFolderPickerDialog

**Files:**
- Create: `DriveFolderPickerDialog.axaml`
- Create: `DriveFolderPickerDialog.axaml.cs`

**Interfaces:**
- Consumes: `StudioLog.Core.GoogleDriveManager` (Task 2) — specifically `ListSharedDrivesAsync`, `ListChildFoldersAsync`, `FindSharedDriveByNameAsync`, `FindChildFolderByNameAsync`, and the `GoogleDriveManager.DriveFolder` type.
- Produces: `StudioLog.DriveFolderPickerDialog : Window` with:
  - Constructor `DriveFolderPickerDialog(GoogleDriveManager driveManager)`
  - `string? SelectedFolderId { get; private set; }`
  - `string? SelectedDriveId { get; private set; }`
  - `ShowDialog<bool>(ownerWindow)` returns `true` on confirmed selection, `false` on cancel — consumed by Task 4.

- [ ] **Step 1: Write DriveFolderPickerDialog.axaml**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="StudioLog.DriveFolderPickerDialog"
        Title="Select Google Drive Folder"
        Width="480"
        Height="440"
        Background="#2d2d2d"
        WindowStartupLocation="CenterOwner">

    <Grid Margin="20" RowDefinitions="Auto,Auto,*,Auto,Auto">

        <TextBlock Grid.Row="0"
                    x:Name="BreadcrumbText"
                    Foreground="#aaaaaa"
                    FontSize="12"
                    TextWrapping="Wrap"
                    Margin="0,0,0,10"/>

        <Button Grid.Row="1"
                x:Name="UpButton"
                Content="Up"
                Width="60"
                HorizontalAlignment="Left"
                Background="#3a3a3a"
                Foreground="White"
                Click="UpButton_Click"
                Margin="0,0,0,10"/>

        <ListBox Grid.Row="2"
                 x:Name="FolderListBox"
                 Background="#1e1e1e"
                 Foreground="White"
                 DoubleTapped="FolderListBox_DoubleTapped"
                 SelectionChanged="FolderListBox_SelectionChanged"/>

        <TextBlock Grid.Row="3"
                   x:Name="StatusText"
                   Foreground="#f87171"
                   FontSize="12"
                   TextWrapping="Wrap"
                   Margin="0,10,0,0"/>

        <StackPanel Grid.Row="4"
                    Orientation="Horizontal"
                    HorizontalAlignment="Right"
                    Spacing="12"
                    Margin="0,15,0,0">
            <Button Content="Select This Folder"
                    x:Name="SelectButton"
                    Click="SelectButton_Click"
                    IsEnabled="False"
                    Background="#3b82f6"
                    Foreground="White"
                    FontWeight="Bold"
                    Width="150"
                    Height="35"/>
            <Button Content="Cancel"
                    Click="CancelButton_Click"
                    Background="#555555"
                    Foreground="White"
                    FontWeight="Bold"
                    Width="100"
                    Height="35"/>
        </StackPanel>

    </Grid>
</Window>
```

- [ ] **Step 2: Write DriveFolderPickerDialog.axaml.cs**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using StudioLog.Core;

namespace StudioLog
{
    public partial class DriveFolderPickerDialog : Window
    {
        private readonly GoogleDriveManager _driveManager;

        private class BreadcrumbEntry
        {
            public string Name { get; set; } = string.Empty;
            public string? FolderId { get; set; } // null = Shared Drives root
            public string? DriveId { get; set; }
        }

        private readonly List<BreadcrumbEntry> _breadcrumb = new();
        private List<GoogleDriveManager.DriveFolder> _currentItems = new();

        public string? SelectedFolderId { get; private set; }
        public string? SelectedDriveId { get; private set; }

        public DriveFolderPickerDialog(GoogleDriveManager driveManager)
        {
            InitializeComponent();
            _driveManager = driveManager;
            Opened += async (_, _) => await InitializeAsync();
        }

        private async System.Threading.Tasks.Task InitializeAsync()
        {
            _breadcrumb.Clear();
            _breadcrumb.Add(new BreadcrumbEntry { Name = "Shared Drives", FolderId = null, DriveId = null });

            StatusText.Text = "Loading...";

            try
            {
                var production = await _driveManager.FindSharedDriveByNameAsync("Production");
                if (production != null)
                {
                    var artists = await _driveManager.FindChildFolderByNameAsync(production.Id, production.DriveId, "Artists");
                    if (artists != null)
                    {
                        _breadcrumb.Add(new BreadcrumbEntry { Name = production.Name, FolderId = production.Id, DriveId = production.DriveId });
                        _breadcrumb.Add(new BreadcrumbEntry { Name = artists.Name, FolderId = artists.Id, DriveId = artists.DriveId });
                    }
                }

                await LoadCurrentLevelAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load Google Drive: {ex.Message}";
            }
        }

        private async System.Threading.Tasks.Task LoadCurrentLevelAsync()
        {
            var current = _breadcrumb[^1];
            BreadcrumbText.Text = string.Join(" / ", _breadcrumb.Select(b => b.Name));
            UpButton.IsEnabled = _breadcrumb.Count > 1;
            SelectButton.IsEnabled = false;
            StatusText.Text = "Loading...";

            try
            {
                _currentItems = current.FolderId == null
                    ? await _driveManager.ListSharedDrivesAsync()
                    : await _driveManager.ListChildFoldersAsync(current.FolderId, current.DriveId);

                FolderListBox.ItemsSource = _currentItems.Select(f => f.Name).ToList();
                StatusText.Text = _currentItems.Count == 0 ? "No folders here." : string.Empty;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to load folder: {ex.Message}";
                _currentItems = new List<GoogleDriveManager.DriveFolder>();
                FolderListBox.ItemsSource = null;
            }
        }

        private async void FolderListBox_DoubleTapped(object? sender, TappedEventArgs e)
        {
            if (FolderListBox.SelectedIndex < 0 || FolderListBox.SelectedIndex >= _currentItems.Count) return;

            var selected = _currentItems[FolderListBox.SelectedIndex];
            var atSharedDrivesRoot = _breadcrumb[^1].FolderId == null;

            _breadcrumb.Add(new BreadcrumbEntry
            {
                Name = selected.Name,
                FolderId = atSharedDrivesRoot ? selected.DriveId : selected.Id,
                DriveId = selected.DriveId
            });

            await LoadCurrentLevelAsync();
        }

        private async void UpButton_Click(object? sender, RoutedEventArgs e)
        {
            if (_breadcrumb.Count <= 1) return;
            _breadcrumb.RemoveAt(_breadcrumb.Count - 1);
            await LoadCurrentLevelAsync();
        }

        private void FolderListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var atSharedDrivesRoot = _breadcrumb[^1].FolderId == null;
            SelectButton.IsEnabled = !atSharedDrivesRoot && FolderListBox.SelectedIndex >= 0;
        }

        private void SelectButton_Click(object? sender, RoutedEventArgs e)
        {
            var current = _breadcrumb[^1];
            SelectedFolderId = current.FolderId;
            SelectedDriveId = current.DriveId;
            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
```

- [ ] **Step 3: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add DriveFolderPickerDialog.axaml DriveFolderPickerDialog.axaml.cs
git commit -m "feat: add DriveFolderPickerDialog for browsing Shared Drive folders"
```

---

### Task 4: Wire Google Drive export commands into MainViewModel

**Files:**
- Modify: `MainViewModel.cs`

**Interfaces:**
- Consumes: `StudioLog.Core.GoogleDriveManager` (Task 2), `StudioLog.DriveFolderPickerDialog` (Task 3), existing `ExportManager.ExportToPdf/ExportToCsv/ExportToPng`, existing `SessionName`/`Date`/`Location`/`LogEntries`/`StatusMessage` properties.
- Produces: `ICommand ExportPdfToDriveCommand`, `ICommand ExportCsvToDriveCommand`, `ICommand ExportPngToDriveCommand`, `ICommand DisconnectGoogleDriveCommand` — consumed by Task 5 (menu bindings).

- [ ] **Step 1: Add the `_driveManager` field**

In `MainViewModel.cs`, find:

```csharp
        private readonly ExportManager _exportManager;
```

Replace with:

```csharp
        private readonly ExportManager _exportManager;
        private readonly GoogleDriveManager _driveManager;
```

- [ ] **Step 2: Initialize it in the constructor**

Find:

```csharp
            _exportManager = new ExportManager();
```

Replace with:

```csharp
            _exportManager = new ExportManager();
            _driveManager = new GoogleDriveManager();
```

- [ ] **Step 3: Add the new ICommand properties**

Find:

```csharp
        public ICommand ExportPdfCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportPngCommand { get; }
```

Replace with:

```csharp
        public ICommand ExportPdfCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportPngCommand { get; }
        public ICommand ExportPdfToDriveCommand { get; }
        public ICommand ExportCsvToDriveCommand { get; }
        public ICommand ExportPngToDriveCommand { get; }
        public ICommand DisconnectGoogleDriveCommand { get; }
```

- [ ] **Step 4: Wire the commands in the constructor**

Find:

```csharp
            ExportPdfCommand = ReactiveCommand.Create(ExportPdf);
            ExportCsvCommand = ReactiveCommand.Create(ExportCsv);
            ExportPngCommand = ReactiveCommand.Create(ExportPng);
```

Replace with:

```csharp
            ExportPdfCommand = ReactiveCommand.Create(ExportPdf);
            ExportCsvCommand = ReactiveCommand.Create(ExportCsv);
            ExportPngCommand = ReactiveCommand.Create(ExportPng);
            ExportPdfToDriveCommand = ReactiveCommand.CreateFromTask(() => ExportToDriveAsync("pdf"));
            ExportCsvToDriveCommand = ReactiveCommand.CreateFromTask(() => ExportToDriveAsync("csv"));
            ExportPngToDriveCommand = ReactiveCommand.CreateFromTask(() => ExportToDriveAsync("png"));
            DisconnectGoogleDriveCommand = ReactiveCommand.CreateFromTask(DisconnectGoogleDriveAsync);
```

- [ ] **Step 5: Add the ExportToDriveAsync, ShowDriveFolderPickerAsync, and DisconnectGoogleDriveAsync methods**

Find the end of the existing `ExportPng()` method:

```csharp
        public async void ExportPng()
        {
            try
            {
                string defaultFilename = $"{SessionName}_{Date}_Log.png".Replace("/", "-").Replace("\\", "-");
                
                string? filepath = await ShowSaveFileDialog(defaultFilename, "PNG Images", "png");
                if (string.IsNullOrEmpty(filepath)) return;
                
                await _exportManager.ExportToPng(filepath, SessionName, Date, Location, LogEntries.ToList());
                
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"PNG exported: {Path.GetFileName(filepath)}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                });
            }
        }
```

Immediately after it (before `public async void OpenSession()`), add:

```csharp
        private async Task ExportToDriveAsync(string format)
        {
            string safeName = $"{SessionName}_{Date}_Log_{Guid.NewGuid():N}.{format}".Replace("/", "-").Replace("\\", "-");
            string tempPath = Path.Combine(Path.GetTempPath(), safeName);

            try
            {
                switch (format)
                {
                    case "pdf":
                        await _exportManager.ExportToPdf(tempPath, SessionName, Date, Location, LogEntries.ToList());
                        break;
                    case "csv":
                        await _exportManager.ExportToCsv(tempPath, SessionName, Date, Location, LogEntries.ToList());
                        break;
                    case "png":
                        await _exportManager.ExportToPng(tempPath, SessionName, Date, Location, LogEntries.ToList());
                        break;
                }

                var (folderId, driveId) = await ShowDriveFolderPickerAsync();
                if (folderId == null)
                {
                    return;
                }

                string mimeType = format switch
                {
                    "pdf" => "application/pdf",
                    "csv" => "text/csv",
                    "png" => "image/png",
                    _ => "application/octet-stream"
                };

                await _driveManager.UploadFileAsync(tempPath, mimeType, folderId, driveId);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"{format.ToUpperInvariant()} uploaded to Google Drive: {Path.GetFileName(tempPath)}";
                });
            }
            catch (Exception ex)
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = $"Drive upload failed: {ex.Message}";
                });
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                catch
                {
                    // Best-effort temp file cleanup
                }
            }
        }

        private async Task<(string? folderId, string? driveId)> ShowDriveFolderPickerAsync()
        {
            var tcs = new TaskCompletionSource<(string?, string?)>();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var mainWindow = Avalonia.Application.Current?.ApplicationLifetime is
                    Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow
                    : null;

                if (mainWindow == null)
                {
                    tcs.SetResult((null, null));
                    return;
                }

                var dialog = new DriveFolderPickerDialog(_driveManager);
                var confirmed = await dialog.ShowDialog<bool>(mainWindow);
                tcs.SetResult(confirmed ? (dialog.SelectedFolderId, dialog.SelectedDriveId) : (null, null));
            });

            return await tcs.Task;
        }

        private async Task DisconnectGoogleDriveAsync()
        {
            _driveManager.Disconnect();

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "Disconnected from Google Drive.";
            });
        }
```

- [ ] **Step 6: Dispose the drive manager**

Find:

```csharp
            _displayTimer?.Dispose();
            _database?.Dispose();
            _audioManager?.Dispose();
            _updateService?.Dispose();
```

Replace with:

```csharp
            _displayTimer?.Dispose();
            _database?.Dispose();
            _audioManager?.Dispose();
            _updateService?.Dispose();
            _driveManager?.Dispose();
```

- [ ] **Step 7: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

```powershell
git add MainViewModel.cs
git commit -m "feat: wire Google Drive export commands into MainViewModel"
```

---

### Task 5: Add Google Drive menu items

**Files:**
- Modify: `MainWindow.axaml`

**Interfaces:**
- Consumes: `ExportPdfToDriveCommand`, `ExportCsvToDriveCommand`, `ExportPngToDriveCommand`, `DisconnectGoogleDriveCommand` (Task 4).

- [ ] **Step 1: Add the Google Drive export submenu**

Find:

```xml
                <MenuItem Name="ExportMenuItem" Header="Export              Ctrl+E">
                    <MenuItem Header="Export to PDF" Command="{Binding ExportPdfCommand}"/>
                    <MenuItem Header="Export to CSV" Command="{Binding ExportCsvCommand}"/>
                    <MenuItem Header="Export to PNG" Command="{Binding ExportPngCommand}"/>
                </MenuItem>
```

Replace with:

```xml
                <MenuItem Name="ExportMenuItem" Header="Export              Ctrl+E">
                    <MenuItem Header="Export to PDF" Command="{Binding ExportPdfCommand}"/>
                    <MenuItem Header="Export to CSV" Command="{Binding ExportCsvCommand}"/>
                    <MenuItem Header="Export to PNG" Command="{Binding ExportPngCommand}"/>
                    <Separator/>
                    <MenuItem Header="Google Drive">
                        <MenuItem Header="Export to PDF" Command="{Binding ExportPdfToDriveCommand}"/>
                        <MenuItem Header="Export to CSV" Command="{Binding ExportCsvToDriveCommand}"/>
                        <MenuItem Header="Export to PNG" Command="{Binding ExportPngToDriveCommand}"/>
                    </MenuItem>
                </MenuItem>
```

- [ ] **Step 2: Add the Disconnect Google Drive settings entry**

Find:

```xml
                <Separator/>
                <MenuItem Header="COMPANION CONTROL..." Command="{Binding OpenCompanionSettingsCommand}"/>
            </MenuItem>
```

Replace with:

```xml
                <Separator/>
                <MenuItem Header="COMPANION CONTROL..." Command="{Binding OpenCompanionSettingsCommand}"/>
                <MenuItem Header="DISCONNECT GOOGLE DRIVE" Command="{Binding DisconnectGoogleDriveCommand}"/>
            </MenuItem>
```

- [ ] **Step 3: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add MainWindow.axaml
git commit -m "feat: add Google Drive export and disconnect menu items"
```

---

### Task 6: Manual end-to-end verification

**Files:** none (verification only)

**Interfaces:** none — this task exercises the full feature built in Tasks 1-5.

- [ ] **Step 1: Launch the app**

```powershell
dotnet run --project StudioLog.csproj -c Debug
```

- [ ] **Step 2: Start a session with at least one log entry**

In the running app: enter a Session Name/Date/Location, click Generate, log at least one Timecode In/Out entry so `LogEntries` is non-empty.

- [ ] **Step 3: Test PDF export to Drive (first-run OAuth)**

Menu: `File > Export > Google Drive > Export to PDF`.
Expected: system default browser opens to a Google sign-in / consent screen for the `texasmusiccafe.org` account. Approve access. The app's `DriveFolderPickerDialog` should then appear, either inside Production/Artists' contents, or (if Production/Artists don't exist yet in this Drive) showing the Shared Drives root — confirm which happened based on what's visible in the actual Google Drive for the account used.

- [ ] **Step 4: Navigate and select a folder**

Double-click into a folder (or a Shared Drive, if starting at root) to descend; click "Up" to go back. Confirm "Select This Folder" is disabled while viewing the Shared Drives root list, and enabled as soon as you're inside any real folder — regardless of whether that folder has subfolders to highlight. Click "Select This Folder".

Expected: status bar shows `PDF uploaded to Google Drive: <filename>` where `<filename>` is the clean `SessionName_Date_Log.pdf` form (no GUID or other internal suffix). Open Google Drive in a browser and confirm the PDF landed in the folder selected, with that clean filename.

- [ ] **Step 4b: Navigate into a leaf (empty) folder and select it**

Navigate into a folder that has no subfolders (e.g. an existing session folder with only files in it, no child folders). Confirm "Select This Folder" is still enabled here — even though the folder list is empty — and clicking it uploads into that folder. This is the primary real-world case (an existing session folder is usually a leaf) and previously failed silently by leaving the button permanently disabled.

- [ ] **Step 5: Test CSV and PNG export to Drive**

Repeat `File > Export > Google Drive > Export to CSV` and `Export to PNG`. Since the token is now cached, no browser consent screen should reappear — the folder picker should open directly. Confirm both files upload and appear in Drive.

- [ ] **Step 6: Test cancel**

Trigger any Google Drive export, and click "Cancel" in the folder picker. Expected: no file appears in Drive, `StatusMessage` is unchanged (no error shown).

- [ ] **Step 7: Test Disconnect**

Menu: `Settings > DISCONNECT GOOGLE DRIVE`. Expected: status bar shows `Disconnected from Google Drive.`. Trigger a Drive export again — expected: the browser consent screen reappears (token was cleared).

- [ ] **Step 8: Confirm local (non-Drive) exports are unaffected**

`File > Export > Export to PDF/CSV/PNG` should still show the local save-file dialog exactly as before, unaffected by this feature.
