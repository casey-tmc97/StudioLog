# Check for Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Check for Updates" menu item under HELP that queries GitHub Releases, downloads the latest installer if newer, and silently installs it while closing the app. Also checks silently on startup.

**Architecture:** A new `UpdateService.cs` handles all GitHub API, download, and install logic. `MainViewModel.cs` gets a `CheckForUpdatesCommand` and a fire-and-forget startup check. A small `UpdateConfirmDialog` provides the yes/no install prompt.

**Tech Stack:** `System.Net.Http.HttpClient`, `System.Text.Json` (built into .NET 9), Avalonia dialogs, Inno Setup `/VERYSILENT` flag.

---

### Task 1: Create UpdateService.cs

**Files:**
- Create: `UpdateService.cs`

- [ ] **Step 1: Create the file**

Create `C:\Users\Admin\Documents\GitHub\StudioLog\UpdateService.cs` with this content:

```csharp
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StudioLog
{
    internal class UpdateService
    {
        private const string ApiUrl = "https://api.github.com/repos/casey-tmc97/StudioLog/releases/latest";
        private readonly HttpClient _http;

        public UpdateService()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var versionStr = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
            _http = new HttpClient();
            _http.DefaultRequestHeaders.Add("User-Agent", $"StudioLog/{versionStr}");
        }

        public record ReleaseInfo(string TagName, string InstallerUrl, Version LatestVersion);

        public async Task<ReleaseInfo?> GetLatestReleaseAsync()
        {
            var json = await _http.GetFromJsonAsync<GitHubRelease>(ApiUrl);
            if (json == null) return null;

            if (!Version.TryParse(json.TagName.TrimStart('v'), out var latestVersion))
                return null;

            string? installerUrl = null;
            foreach (var asset in json.Assets)
            {
                if (asset.Name.EndsWith("-Setup.exe", StringComparison.OrdinalIgnoreCase))
                {
                    installerUrl = asset.BrowserDownloadUrl;
                    break;
                }
            }

            if (installerUrl == null) return null;
            return new ReleaseInfo(json.TagName, installerUrl, latestVersion);
        }

        public bool IsUpdateAvailable(ReleaseInfo release)
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current == null) return false;
            var currentNormalized = new Version(current.Major, current.Minor, current.Build);
            return release.LatestVersion > currentNormalized;
        }

        public async Task<string> DownloadInstallerAsync(
            ReleaseInfo release,
            IProgress<(long downloaded, long total)>? progress = null)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "StudioLog-Setup.exe");
            using var response = await _http.GetAsync(
                release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var total = response.Content.Headers.ContentLength ?? -1L;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var file = File.Create(tempPath);

            var buffer = new byte[81920];
            long downloaded = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer)) > 0)
            {
                await file.WriteAsync(buffer.AsMemory(0, read));
                downloaded += read;
                progress?.Report((downloaded, total));
            }

            return tempPath;
        }

        public void LaunchInstallerAndExit(string installerPath)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /NORESTART",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);

            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        private class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("assets")]
            public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
        }

        private class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;
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
git add UpdateService.cs
git commit -m "feat: add UpdateService for GitHub release checking and install"
```

---

### Task 2: Create UpdateConfirmDialog

**Files:**
- Create: `UpdateConfirmDialog.axaml`
- Create: `UpdateConfirmDialog.axaml.cs`

- [ ] **Step 1: Create the AXAML view**

Create `C:\Users\Admin\Documents\GitHub\StudioLog\UpdateConfirmDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="StudioLog.UpdateConfirmDialog"
        Title="Update Available"
        Width="420"
        Height="200"
        Background="#2d2d2d"
        WindowStartupLocation="CenterOwner"
        CanResize="False">

    <Grid Margin="30">
        <StackPanel Spacing="20" VerticalAlignment="Center">

            <TextBlock x:Name="MessageText"
                       FontSize="14"
                       Foreground="White"
                       TextWrapping="Wrap"
                       HorizontalAlignment="Center"
                       TextAlignment="Center"/>

            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Center"
                        Spacing="12">
                <Button Content="Install Now"
                        Click="InstallButton_Click"
                        Width="120"
                        Height="35"
                        Background="#4a90d9"
                        Foreground="White"/>
                <Button Content="Cancel"
                        Click="CancelButton_Click"
                        Width="100"
                        Height="35"
                        Background="#555555"
                        Foreground="White"/>
            </StackPanel>

        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `C:\Users\Admin\Documents\GitHub\StudioLog\UpdateConfirmDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StudioLog
{
    public partial class UpdateConfirmDialog : Window
    {
        public UpdateConfirmDialog(string latestTag)
        {
            InitializeComponent();
            MessageText.Text =
                $"Version {latestTag} is available.\n\n" +
                "Download and install now?\nStudioLog will close automatically.";
        }

        private void InstallButton_Click(object? sender, RoutedEventArgs e) => Close(true);
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
git add UpdateConfirmDialog.axaml UpdateConfirmDialog.axaml.cs
git commit -m "feat: add UpdateConfirmDialog for install confirmation"
```

---

### Task 3: Wire UpdateService into MainViewModel

**Files:**
- Modify: `MainViewModel.cs`

There are four edits to make in sequence.

- [ ] **Step 1: Add the field and command property**

In `MainViewModel.cs`, find the existing field declarations near the top of the class (around line 25, after `private bool _disposed`). Add one new field:

```csharp
private readonly UpdateService _updateService;
```

Then find the block of `public ICommand` declarations (around line 218-222) and add after `OpenContactCommand`:

```csharp
public ICommand CheckForUpdatesCommand { get; }
```

- [ ] **Step 2: Initialize the service and wire the command**

In the constructor, find where other services are instantiated (around line 240, near `_sessionManager = new SessionManager(...)`). Add:

```csharp
_updateService = new UpdateService();
```

Find where `OpenContactCommand` is wired (around line 273):

```csharp
OpenContactCommand = ReactiveCommand.Create(OpenContact);
```

Directly after it, add:

```csharp
CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);
```

- [ ] **Step 3: Add the startup silent check**

In the constructor, find the `InitializeAsync().ContinueWith(...)` block (around line 292). After that entire block, add:

```csharp
// Check for updates silently 3 seconds after startup
Task.Delay(3000).ContinueWith(_ => CheckForUpdatesSilentAsync(),
    TaskContinuationOptions.None);
```

- [ ] **Step 4: Add the two update methods**

Find the `OpenContact()` method (around line 1625) and add these two methods directly after it:

```csharp
public async Task CheckForUpdatesAsync()
{
    StatusMessage = "Checking for updates...";
    try
    {
        var release = await _updateService.GetLatestReleaseAsync();
        if (release == null || !_updateService.IsUpdateAvailable(release))
        {
            var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var tag = current != null
                ? $"v{current.Major}.{current.Minor}.{current.Build}"
                : "current";
            StatusMessage = $"You're on the latest version ({tag}).";
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is
            Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            && desktop.MainWindow != null)
        {
            var dialog = new UpdateConfirmDialog(release.TagName);
            var confirmed = await dialog.ShowDialog<bool>(desktop.MainWindow);
            if (!confirmed)
            {
                StatusMessage = "Update cancelled.";
                return;
            }
        }

        var progress = new Progress<(long downloaded, long total)>(p =>
        {
            var mb = p.downloaded / 1_048_576.0;
            var totalMb = p.total > 0 ? $" / {p.total / 1_048_576.0:F1} MB" : string.Empty;
            StatusMessage = $"Downloading update... {mb:F1} MB{totalMb}";
        });

        var path = await _updateService.DownloadInstallerAsync(release, progress);
        _updateService.LaunchInstallerAndExit(path);
    }
    catch
    {
        StatusMessage = "Could not reach update server. Check your connection and try again.";
    }
}

private async void CheckForUpdatesSilentAsync()
{
    try
    {
        var release = await _updateService.GetLatestReleaseAsync();
        if (release != null && _updateService.IsUpdateAvailable(release))
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"Update available: {release.TagName} — Help > Check for Updates to install";
            });
        }
    }
    catch
    {
        // Silent — never surface errors from background check
    }
}
```

- [ ] **Step 5: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add MainViewModel.cs
git commit -m "feat: wire CheckForUpdatesCommand and silent startup update check"
```

---

### Task 4: Add menu item to MainWindow.axaml

**Files:**
- Modify: `MainWindow.axaml`

- [ ] **Step 1: Add the menu item**

In `MainWindow.axaml`, find the HELP menu block (around line 225):

```xml
<MenuItem Header="HELP" Foreground="White">
    <MenuItem Header="MANUAL" Command="{Binding OpenManualCommand}"/>
    <MenuItem Header="ABOUT" Command="{Binding OpenAboutCommand}"/>
    <MenuItem Header="CONTACT" Command="{Binding OpenContactCommand}"/>
</MenuItem>
```

Replace it with:

```xml
<MenuItem Header="HELP" Foreground="White">
    <MenuItem Header="MANUAL" Command="{Binding OpenManualCommand}"/>
    <MenuItem Header="ABOUT" Command="{Binding OpenAboutCommand}"/>
    <MenuItem Header="CONTACT" Command="{Binding OpenContactCommand}"/>
    <Separator/>
    <MenuItem Header="CHECK FOR UPDATES" Command="{Binding CheckForUpdatesCommand}"/>
</MenuItem>
```

- [ ] **Step 2: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```powershell
git add MainWindow.axaml
git commit -m "feat: add Check for Updates to HELP menu"
```

---

### Task 5: Build release and verify

- [ ] **Step 1: Build release binary**

```powershell
dotnet publish StudioLog.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

Expected: `publish\StudioLog.exe` produced.

- [ ] **Step 2: Manual smoke test**

Launch `publish\StudioLog.exe`. Verify:
- App opens normally (no crash on startup)
- After ~3 seconds, if running an older build, the status bar shows "Update available: vX.X.X — Help > Check for Updates to install"
- Open HELP menu — "CHECK FOR UPDATES" item appears with a separator above it
- Click "CHECK FOR UPDATES" — status bar briefly shows "Checking for updates..."
- If already on latest: status bar shows "You're on the latest version (vX.X.X)."

- [ ] **Step 3: Final commit (if any fixups needed)**

```powershell
git add -A
git commit -m "fix: update check smoke test fixups"
```

Only run this step if fixups were needed in step 2.
