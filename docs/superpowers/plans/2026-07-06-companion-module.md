# Bitfocus Companion Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an opt-in TCP control server to StudioLog exposing Generate/Stop, Timecode In, Timecode Out, and Mark, and build a companion `companion-module-studiolog` Bitfocus Companion module that connects to it, exposing 4 actions, 3 variables (usable in Companion Triggers), and 2 feedbacks.

**Architecture:** `CompanionControlServer.cs` is a thin TCP transport class inside StudioLog: it parses incoming command lines into events and exposes broadcast methods for pushing state. `MainViewModel` bridges it to the existing ViewModel commands and `PropertyChanged` events — no ViewModel business logic is duplicated. The separate `companion-module-studiolog` repo is a standard `@companion-module/base` module (JS) with a hand-rolled `net.Socket` client (`connection.js`) that reconnects automatically, maps incoming `STATE` lines to Companion variables/feedbacks, and maps the 4 button actions to outgoing command lines.

**Tech Stack:** .NET 9 / Avalonia (existing), `System.Net.Sockets.TcpListener` (built-in, no new NuGet package), Node.js + `@companion-module/base` ~2.1.1 + `@companion-module/tools` ^3.0.2 (new separate repo, npm-managed).

## Global Constraints

- StudioLog targets `net9.0` (existing `StudioLog.csproj`) — no new NuGet packages needed for the server.
- Default control port is `51234` on both sides (StudioLog `AppSettings.CompanionControlPort` and the Companion module's config field default) — must match so the module works out of the box against a default StudioLog install.
- No authentication/encryption on the wire protocol — trusted-LAN-only design, per the approved spec (`docs/superpowers/specs/2026-07-06-companion-module-design.md`).
- No automated tests are added on either side (StudioLog has no existing test project; the Companion module ecosystem doesn't require one here) — verification is manual/build-only, per the approved spec.
- Companion module repo: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog`, package name `companion-module-studiolog`, manifest id `studiolog`.

---

### Task 1: Add Companion Control settings to AppSettings

**Files:**
- Modify: `AppSettings.cs`

**Interfaces:**
- Produces: `AppSettings.CompanionControlEnabled` (bool, default `false`), `AppSettings.CompanionControlPort` (int, default `51234`) — consumed by Task 3 and Task 4.

- [ ] **Step 1: Add the two properties**

In `AppSettings.cs`, find:

```csharp
        public string LastLaunchedVersion { get; set; } = string.Empty;
        
        public bool IsInputActive => SelectedAudioInput != "None";
```

Replace with:

```csharp
        public string LastLaunchedVersion { get; set; } = string.Empty;

        public bool CompanionControlEnabled { get; set; } = false;
        public int CompanionControlPort { get; set; } = 51234;

        public bool IsInputActive => SelectedAudioInput != "None";
```

- [ ] **Step 2: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```powershell
git add AppSettings.cs
git commit -m "feat: add Companion control settings (enabled flag + port)"
```

---

### Task 2: Create CompanionControlServer.cs

**Files:**
- Create: `CompanionControlServer.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (standalone transport class).
- Produces: `StudioLog.Core.CompanionControlServer` with:
  - `void Start(int port)`, `void Stop()`, `bool IsRunning`
  - `Func<(bool generatorRunning, bool tcInActive, string timecode)>? GetCurrentState` (settable property)
  - Events: `event Action? GenerateToggleRequested`, `event Action? TimeCodeInRequested`, `event Action? TimeCodeOutRequested`, `event Action? MarkRequested`
  - `void BroadcastGeneratorState(bool running)`, `void BroadcastTimecodeInActive(bool active)`, `void BroadcastTimecode(string timecode)`
  - Implements `IDisposable`
  - Consumed by Task 3.

- [ ] **Step 1: Create the file**

Create `C:\Users\Admin\Documents\GitHub\StudioLog\CompanionControlServer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace StudioLog.Core
{
    /// <summary>
    /// TCP transport for Bitfocus Companion control. Parses incoming command
    /// lines into events and broadcasts state lines to all connected clients.
    /// Wire protocol: newline-terminated ASCII lines. See
    /// docs/superpowers/specs/2026-07-06-companion-module-design.md.
    /// </summary>
    public class CompanionControlServer : IDisposable
    {
        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly List<TcpClient> _clients = new();
        private readonly object _clientsLock = new();
        private bool _disposed;

        public event Action? GenerateToggleRequested;
        public event Action? TimeCodeInRequested;
        public event Action? TimeCodeOutRequested;
        public event Action? MarkRequested;

        public bool IsRunning { get; private set; }

        /// <summary>
        /// Called once per newly-accepted client to fetch current state so
        /// it can be sent immediately, before any state actually changes.
        /// </summary>
        public Func<(bool generatorRunning, bool tcInActive, string timecode)>? GetCurrentState { get; set; }

        public void Start(int port)
        {
            if (IsRunning) Stop();

            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            IsRunning = true;

            _ = AcceptLoopAsync(_cts.Token);
            Console.WriteLine($"[CompanionControlServer] Listening on port {port}");
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;

            lock (_clientsLock)
            {
                foreach (var client in _clients)
                {
                    try { client.Close(); } catch { }
                }
                _clients.Clear();
            }

            IsRunning = false;
            Console.WriteLine("[CompanionControlServer] Stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested && _listener != null)
                {
                    var client = await _listener.AcceptTcpClientAsync(token);

                    lock (_clientsLock)
                    {
                        _clients.Add(client);
                    }

                    var state = GetCurrentState?.Invoke();
                    if (state != null)
                    {
                        SendToClient(client, $"STATE GENERATOR {(state.Value.generatorRunning ? "RUNNING" : "STOPPED")}");
                        SendToClient(client, $"STATE TC_IN_ACTIVE {(state.Value.tcInActive ? "TRUE" : "FALSE")}");
                        SendToClient(client, $"STATE TIMECODE {state.Value.timecode}");
                    }

                    _ = HandleClientAsync(client, token);
                }
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                // Expected on Stop()
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CompanionControlServer] Accept loop error: {ex.Message}");
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new System.IO.StreamReader(stream, Encoding.ASCII);

                while (!token.IsCancellationRequested && client.Connected)
                {
                    var line = await reader.ReadLineAsync(token);
                    if (line == null) break;

                    DispatchCommand(line.Trim());
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CompanionControlServer] Client error: {ex.Message}");
            }
            finally
            {
                lock (_clientsLock)
                {
                    _clients.Remove(client);
                }
                try { client.Close(); } catch { }
            }
        }

        private void DispatchCommand(string command)
        {
            switch (command)
            {
                case "GENERATE_TOGGLE":
                    GenerateToggleRequested?.Invoke();
                    break;
                case "TC_IN":
                    TimeCodeInRequested?.Invoke();
                    break;
                case "TC_OUT":
                    TimeCodeOutRequested?.Invoke();
                    break;
                case "MARK":
                    MarkRequested?.Invoke();
                    break;
                default:
                    Console.WriteLine($"[CompanionControlServer] Unknown command: {command}");
                    break;
            }
        }

        public void BroadcastGeneratorState(bool running) =>
            Broadcast($"STATE GENERATOR {(running ? "RUNNING" : "STOPPED")}");

        public void BroadcastTimecodeInActive(bool active) =>
            Broadcast($"STATE TC_IN_ACTIVE {(active ? "TRUE" : "FALSE")}");

        public void BroadcastTimecode(string timecode) =>
            Broadcast($"STATE TIMECODE {timecode}");

        private void Broadcast(string message)
        {
            List<TcpClient> snapshot;
            lock (_clientsLock)
            {
                snapshot = new List<TcpClient>(_clients);
            }

            foreach (var client in snapshot)
            {
                SendToClient(client, message);
            }
        }

        private void SendToClient(TcpClient client, string message)
        {
            try
            {
                if (!client.Connected) return;
                var data = Encoding.ASCII.GetBytes(message + "\n");
                client.GetStream().Write(data, 0, data.Length);
            }
            catch
            {
                // Client will be cleaned up by its own read loop on next iteration.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            Stop();
            _disposed = true;
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
git add CompanionControlServer.cs
git commit -m "feat: add CompanionControlServer TCP transport for Bitfocus Companion"
```

---

### Task 3: Wire CompanionControlServer into MainViewModel

**Files:**
- Modify: `MainViewModel.cs`

**Interfaces:**
- Consumes: `CompanionControlServer` (Task 2), `AppSettings.CompanionControlEnabled`/`CompanionControlPort` (Task 1).
- Produces: `MainViewModel.ApplyCompanionSettings(bool enabled, int port)` — consumed by Task 4's dialog flow.

There are five edits to make in sequence.

- [ ] **Step 1: Add the field**

Find (near the top of the class, around line 30):

```csharp
        private readonly UpdateService _updateService;
        private readonly Stack<List<TimecodeLogEntry>> _undoStack = new();
```

Replace with:

```csharp
        private readonly UpdateService _updateService;
        private readonly CompanionControlServer _companionServer;
        private readonly Stack<List<TimecodeLogEntry>> _undoStack = new();
```

- [ ] **Step 2: Initialize and wire the server in the constructor**

Find (around line 247-250):

```csharp
            // Initialize LTC audio (but don't start yet - user must click Generate)
            _audioManager = new LTCAudioManager();
            var frameRate = GetFrameRateValue(_settings.SelectedFrameRate);
            _audioManager.Initialize(frameRate, _settings.SelectedAudioOutput, _settings.SelectedAudioInput, _settings.SelectedNDISource);
            // Don't start - wait for user to click Generate button
            
            LogEntries = new ObservableCollection<TimecodeLogEntry>();
```

Replace with:

```csharp
            // Initialize LTC audio (but don't start yet - user must click Generate)
            _audioManager = new LTCAudioManager();
            var frameRate = GetFrameRateValue(_settings.SelectedFrameRate);
            _audioManager.Initialize(frameRate, _settings.SelectedAudioOutput, _settings.SelectedAudioInput, _settings.SelectedNDISource);
            // Don't start - wait for user to click Generate button

            _companionServer = new CompanionControlServer
            {
                GetCurrentState = () => (IsGeneratorRunning, IsTimecodeInActive, CurrentTimecode)
            };
            _companionServer.GenerateToggleRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(ToggleGenerator);
            _companionServer.TimeCodeInRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(TimeCodeIn);
            _companionServer.TimeCodeOutRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(TimeCodeOut);
            _companionServer.MarkRequested += () => Avalonia.Threading.Dispatcher.UIThread.Post(TimeCodeMark);
            PropertyChanged += OnPropertyChangedForCompanion;

            if (_settings.CompanionControlEnabled)
            {
                StartCompanionServer();
            }

            LogEntries = new ObservableCollection<TimecodeLogEntry>();
```

- [ ] **Step 3: Add the PropertyChanged bridge and start/stop/apply methods**

Find (around line 1680, the end of `OpenContact()`):

```csharp
            catch (Exception ex)
            {
                StatusMessage = $"Could not open email: {ex.Message}";
            }
        }

        public async Task CheckForUpdatesAsync()
```

Replace with:

```csharp
            catch (Exception ex)
            {
                StatusMessage = $"Could not open email: {ex.Message}";
            }
        }

        private void OnPropertyChangedForCompanion(object? sender, PropertyChangedEventArgs e)
        {
            if (!_companionServer.IsRunning) return;

            switch (e.PropertyName)
            {
                case nameof(IsGeneratorRunning):
                    _companionServer.BroadcastGeneratorState(IsGeneratorRunning);
                    break;
                case nameof(IsTimecodeInActive):
                    _companionServer.BroadcastTimecodeInActive(IsTimecodeInActive);
                    break;
                case nameof(CurrentTimecode):
                    _companionServer.BroadcastTimecode(CurrentTimecode);
                    break;
            }
        }

        private void StartCompanionServer()
        {
            try
            {
                _companionServer.Start(_settings.CompanionControlPort);
                StatusMessage = $"Companion control listening on port {_settings.CompanionControlPort}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Companion Control: {ex.Message}";
            }
        }

        private void StopCompanionServer()
        {
            _companionServer.Stop();
        }

        public void ApplyCompanionSettings(bool enabled, int port)
        {
            _settings.CompanionControlEnabled = enabled;
            _settings.CompanionControlPort = port;
            _settings.Save();

            StopCompanionServer();
            if (enabled)
            {
                StartCompanionServer();
            }
            else
            {
                StatusMessage = "Companion control disabled";
            }
        }

        public async Task CheckForUpdatesAsync()
```

- [ ] **Step 4: Clean up in Dispose**

Find (in `Dispose()`, around line 1849-1850):

```csharp
            _audioManager?.Dispose();
            _updateService?.Dispose();
```

Replace with:

```csharp
            _audioManager?.Dispose();
            _updateService?.Dispose();
            PropertyChanged -= OnPropertyChangedForCompanion;
            _companionServer?.Dispose();
```

- [ ] **Step 5: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add MainViewModel.cs
git commit -m "feat: wire CompanionControlServer into MainViewModel"
```

---

### Task 4: Add CompanionSettingsDialog and SETTINGS menu item

**Files:**
- Create: `CompanionSettingsDialog.axaml`
- Create: `CompanionSettingsDialog.axaml.cs`
- Modify: `MainViewModel.cs`
- Modify: `MainWindow.axaml`

**Interfaces:**
- Consumes: `MainViewModel.ApplyCompanionSettings(bool, int)` (Task 3), `AppSettings.CompanionControlEnabled`/`CompanionControlPort` (Task 1).
- Produces: `MainViewModel.OpenCompanionSettingsCommand` (bound from XAML).

- [ ] **Step 1: Create the dialog view**

Create `C:\Users\Admin\Documents\GitHub\StudioLog\CompanionSettingsDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="StudioLog.CompanionSettingsDialog"
        Title="Companion Control"
        Width="380"
        Height="240"
        Background="#2d2d2d"
        WindowStartupLocation="CenterOwner"
        CanResize="False">

    <Grid Margin="30">
        <StackPanel Spacing="16" VerticalAlignment="Center">

            <CheckBox x:Name="EnableCheckBox"
                      Content="Enable Companion control"
                      Foreground="White"/>

            <StackPanel Orientation="Horizontal" Spacing="10">
                <TextBlock Text="Port:"
                           Foreground="White"
                           VerticalAlignment="Center"
                           Width="40"/>
                <TextBox x:Name="PortTextBox"
                         Width="100"
                         Background="#3a3a3a"
                         Foreground="White"
                         BorderBrush="#555555"/>
            </StackPanel>

            <TextBlock x:Name="ErrorText"
                       Foreground="#f87171"
                       FontSize="12"
                       TextWrapping="Wrap"
                       IsVisible="False"/>

            <StackPanel Orientation="Horizontal"
                        HorizontalAlignment="Center"
                        Spacing="12">
                <Button Content="Save"
                        Click="SaveButton_Click"
                        Width="100"
                        Height="35"
                        Background="#3b82f6"
                        Foreground="White"
                        FontWeight="Bold"/>
                <Button Content="Cancel"
                        Click="CancelButton_Click"
                        Width="100"
                        Height="35"
                        Background="#555555"
                        Foreground="White"
                        FontWeight="Bold"/>
            </StackPanel>

        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Create the code-behind**

Create `C:\Users\Admin\Documents\GitHub\StudioLog\CompanionSettingsDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StudioLog
{
    public partial class CompanionSettingsDialog : Window
    {
        public bool EnabledResult { get; private set; }
        public int PortResult { get; private set; }

        public CompanionSettingsDialog(bool enabled, int port)
        {
            InitializeComponent();
            EnableCheckBox.IsChecked = enabled;
            PortTextBox.Text = port.ToString();
        }

        private void SaveButton_Click(object? sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PortTextBox.Text, out var port) || port < 1024 || port > 65535)
            {
                ErrorText.Text = "Port must be a number between 1024 and 65535.";
                ErrorText.IsVisible = true;
                return;
            }

            EnabledResult = EnableCheckBox.IsChecked == true;
            PortResult = port;
            Close(true);
        }

        private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);
    }
}
```

- [ ] **Step 3: Add the command and open method to MainViewModel**

In `MainViewModel.cs`, find the command property block (around line 222):

```csharp
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand ExitCommand { get; }
```

Replace with:

```csharp
        public ICommand CheckForUpdatesCommand { get; }
        public ICommand OpenCompanionSettingsCommand { get; }
        public ICommand ExitCommand { get; }
```

Find the command wiring block (around line 277):

```csharp
            CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);
            ExitCommand = ReactiveCommand.Create(Exit);
```

Replace with:

```csharp
            CheckForUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);
            OpenCompanionSettingsCommand = ReactiveCommand.CreateFromTask(OpenCompanionSettingsAsync);
            ExitCommand = ReactiveCommand.Create(Exit);
```

Find the end of `ApplyCompanionSettings` (added in Task 3):

```csharp
            StopCompanionServer();
            if (enabled)
            {
                StartCompanionServer();
            }
            else
            {
                StatusMessage = "Companion control disabled";
            }
        }
```

Add a new method directly after that closing brace:

```csharp
        public async Task OpenCompanionSettingsAsync()
        {
            var dialog = new CompanionSettingsDialog(_settings.CompanionControlEnabled, _settings.CompanionControlPort);

            if (Avalonia.Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                && desktop.MainWindow != null)
            {
                var confirmed = await dialog.ShowDialog<bool>(desktop.MainWindow);
                if (confirmed)
                {
                    ApplyCompanionSettings(dialog.EnabledResult, dialog.PortResult);
                }
            }
        }
```

- [ ] **Step 4: Add the SETTINGS menu item**

In `MainWindow.axaml`, find:

```xml
                    <MenuItem Header="FREE RUN" 
                              Command="{Binding SetClockSourceCommand}" 
                              CommandParameter="Free Run"
                              Icon="{Binding IsClockFreeRun, Converter={x:Static converters:BoolToIconConverter.Instance}}"/>
                </MenuItem>
            </MenuItem>
            <MenuItem Header="HELP" Foreground="White">
```

Replace with:

```xml
                    <MenuItem Header="FREE RUN" 
                              Command="{Binding SetClockSourceCommand}" 
                              CommandParameter="Free Run"
                              Icon="{Binding IsClockFreeRun, Converter={x:Static converters:BoolToIconConverter.Instance}}"/>
                </MenuItem>
                <Separator/>
                <MenuItem Header="COMPANION CONTROL..." Command="{Binding OpenCompanionSettingsCommand}"/>
            </MenuItem>
            <MenuItem Header="HELP" Foreground="White">
```

- [ ] **Step 5: Verify it compiles**

```powershell
dotnet build StudioLog.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add CompanionSettingsDialog.axaml CompanionSettingsDialog.axaml.cs MainViewModel.cs MainWindow.axaml
git commit -m "feat: add Companion Control settings dialog and menu item"
```

---

### Task 5: Manual verification of the StudioLog control server

No code changes — this validates Tasks 1-4 end-to-end before moving to the Companion module repo.

- [ ] **Step 1: Run the app in debug**

```powershell
dotnet run --project StudioLog.csproj
```

- [ ] **Step 2: Enable Companion control**

In the running app: `SETTINGS > COMPANION CONTROL...` → check "Enable Companion control" → leave port at `51234` → `Save`. Status bar should show "Companion control listening on port 51234".

- [ ] **Step 3: Create a session**

`FILE > New Session` (needed so `TC IN` / `MARK` don't just show "No active session").

- [ ] **Step 4: Connect a throwaway TCP client and exercise every command**

In a second PowerShell window, run this one-off script (not committed to the repo):

```powershell
$client = New-Object System.Net.Sockets.TcpClient("127.0.0.1", 51234)
$stream = $client.GetStream()
$writer = New-Object System.IO.StreamWriter($stream)
$writer.AutoFlush = $true
$reader = New-Object System.IO.StreamReader($stream)

# Read the initial STATE sync (3 lines) sent immediately on connect
1..3 | ForEach-Object { Write-Output $reader.ReadLine() }

# Exercise each command, reading back any pushed STATE lines after each
foreach ($cmd in "GENERATE_TOGGLE", "TC_IN", "TC_OUT", "MARK", "GENERATE_TOGGLE") {
    $writer.WriteLine($cmd)
    Start-Sleep -Milliseconds 300
    while ($stream.DataAvailable) { Write-Output $reader.ReadLine() }
}

$client.Close()
```

Expected:
- The first 3 lines are `STATE GENERATOR STOPPED`, `STATE TC_IN_ACTIVE FALSE`, `STATE TIMECODE HH:MM:SS:FF` (in some order determined by the code, but all 3 present).
- After the first `GENERATE_TOGGLE`: the app's Generate button flips to "STOP" and a `STATE GENERATOR RUNNING` line appears, followed by periodic `STATE TIMECODE ...` lines.
- After `TC_IN`: a new row appears in the app's log table, and `STATE TC_IN_ACTIVE TRUE` is pushed.
- After `TC_OUT`: the row's Timecode Out fills in, and `STATE TC_IN_ACTIVE FALSE` is pushed.
- After `MARK`: a mark row appears in the app.
- After the second `GENERATE_TOGGLE`: the app's Generate button flips back to "GENERATE" and `STATE GENERATOR STOPPED` is pushed.

- [ ] **Step 5: Verify edge cases don't crash the server**

With no session active (`FILE > New Session` then immediately close the app's current entry by restarting, or simply test on first launch before creating a session), send `TC_OUT` and `MARK` with no open entry/session via the same script. Expected: app's status bar shows its existing "No active entry"/"No active session" messages; the TCP connection stays open and the app does not crash.

- [ ] **Step 6: No commit needed**

This task is verification-only; nothing to commit unless Step 4 or 5 surfaced a bug, in which case fix it in the relevant task's file and commit the fix with a `fix:` message before continuing.

---

### Task 6: Scaffold the companion-module-studiolog repo

**Files:**
- Create (new repo): `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\package.json`
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\companion\manifest.json`
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\.gitignore`
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\README.md`

**Interfaces:**
- Produces: the repo skeleton and installed `node_modules` (`@companion-module/base`, `@companion-module/tools`) that Tasks 7-9 depend on.

- [ ] **Step 1: Create the directory and git init**

```powershell
New-Item -ItemType Directory -Force "C:\Users\Admin\Documents\GitHub\companion-module-studiolog\companion" | Out-Null
New-Item -ItemType Directory -Force "C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src" | Out-Null
cd "C:\Users\Admin\Documents\GitHub\companion-module-studiolog"
git init
```

- [ ] **Step 2: Create package.json**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\package.json`:

```json
{
	"name": "companion-module-studiolog",
	"version": "0.1.0",
	"main": "src/main.js",
	"scripts": {
		"format": "prettier -w .",
		"package": "companion-module-build"
	},
	"license": "UNLICENSED",
	"repository": {
		"type": "git",
		"url": "git+https://github.com/casey-tmc97/companion-module-studiolog.git"
	},
	"engines": {
		"node": ">=18"
	},
	"dependencies": {
		"@companion-module/base": "~2.1.1"
	},
	"devDependencies": {
		"@companion-module/tools": "^3.0.2",
		"prettier": "^3.9.4"
	}
}
```

- [ ] **Step 3: Create the manifest**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\companion\manifest.json`:

```json
{
	"$schema": "../node_modules/@companion-module/base/assets/manifest.schema.json",
	"type": "connection",
	"id": "studiolog",
	"name": "studiolog",
	"shortname": "studiolog",
	"description": "Control StudioLog's Generate/Stop, Timecode In, Timecode Out, and Mark functions",
	"version": "0.1.0",
	"license": "UNLICENSED",
	"repository": "git+https://github.com/casey-tmc97/companion-module-studiolog.git",
	"bugs": "https://github.com/casey-tmc97/companion-module-studiolog/issues",
	"maintainers": [
		{
			"name": "casey-tmc97",
			"email": "casey@texasmusiccafe.org"
		}
	],
	"runtime": {
		"type": "node22",
		"api": "nodejs-ipc",
		"apiVersion": "0.0.0",
		"entrypoint": "../src/main.js"
	},
	"legacyIds": [],
	"manufacturer": "StudioLog",
	"products": ["StudioLog"],
	"keywords": ["timecode", "ltc"]
}
```

- [ ] **Step 4: Create .gitignore**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\.gitignore`:

```
node_modules/
pkg/
*.log
```

- [ ] **Step 5: Create README.md**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\README.md`:

```markdown
# companion-module-studiolog

Bitfocus Companion module for [StudioLog](https://github.com/casey-tmc97/StudioLog).

Requires StudioLog's Companion control server enabled via `SETTINGS > COMPANION CONTROL...`
(default port `51234`).

## Actions

- Generate/Stop
- Timecode In
- Timecode Out
- Mark

## Variables

- `generator_running` — `true`/`false`
- `tc_in_active` — `true`/`false`
- `current_timecode` — `HH:MM:SS:FF`

## Feedbacks

- Generator Running (red background, "STOP" text override, when running)
- TC IN Active (amber background overlay, when a TC IN entry is open)

## Development

```
npm install
```

Load this folder's parent directory as Companion's "Developer modules path"
(Companion Settings), then add a StudioLog connection pointing at the host/port
StudioLog is listening on.
```

- [ ] **Step 6: Install dependencies**

```powershell
cd "C:\Users\Admin\Documents\GitHub\companion-module-studiolog"
npm install
```

Expected: `node_modules/@companion-module/base` and `node_modules/@companion-module/tools` are installed, no errors.

- [ ] **Step 7: Commit**

```powershell
cd "C:\Users\Admin\Documents\GitHub\companion-module-studiolog"
git add package.json companion/manifest.json .gitignore README.md package-lock.json
git commit -m "chore: scaffold companion-module-studiolog"
```

---

### Task 7: Implement the TCP connection client

**Files:**
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\connection.js`

**Interfaces:**
- Consumes: an `instance` object with `updateStatus(status)`, `log(level, msg)`, and `handleLine(line)` methods (provided by Task 9's `main.js`).
- Produces: `StudioLogConnection` class with `connect(host, port)`, `sendCommand(command)`, `destroy()` — consumed by Task 8 (actions) and Task 9 (main.js).

- [ ] **Step 1: Create connection.js**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\connection.js`:

```javascript
const net = require('net')
const { InstanceStatus } = require('@companion-module/base')

class StudioLogConnection {
	constructor(instance) {
		this.instance = instance
		this.socket = null
		this.buffer = ''
		this.reconnectTimer = null
		this.destroyed = false
	}

	connect(host, port) {
		this.host = host
		this.port = typeof port === 'string' ? parseInt(port, 10) : port
		this.destroyed = false
		this.openSocket()
	}

	openSocket() {
		if (this.destroyed) return

		this.instance.updateStatus(InstanceStatus.Connecting)
		this.socket = new net.Socket()

		this.socket.on('connect', () => {
			this.instance.updateStatus(InstanceStatus.Ok)
		})

		this.socket.on('data', (data) => {
			this.buffer += data.toString('ascii')
			let index
			while ((index = this.buffer.indexOf('\n')) >= 0) {
				const line = this.buffer.slice(0, index).trim()
				this.buffer = this.buffer.slice(index + 1)
				if (line.length > 0) {
					this.instance.handleLine(line)
				}
			}
		})

		this.socket.on('error', (err) => {
			this.instance.log('debug', `Connection error: ${err.message}`)
		})

		this.socket.on('close', () => {
			this.instance.updateStatus(InstanceStatus.Disconnected)
			this.socket = null
			this.scheduleReconnect()
		})

		this.socket.connect(this.port, this.host)
	}

	scheduleReconnect() {
		if (this.destroyed || this.reconnectTimer) return
		this.reconnectTimer = setTimeout(() => {
			this.reconnectTimer = null
			this.openSocket()
		}, 2000)
	}

	sendCommand(command) {
		if (this.socket && !this.socket.destroyed) {
			this.socket.write(command + '\n')
		}
	}

	destroy() {
		this.destroyed = true
		if (this.reconnectTimer) {
			clearTimeout(this.reconnectTimer)
			this.reconnectTimer = null
		}
		if (this.socket) {
			this.socket.destroy()
			this.socket = null
		}
	}
}

module.exports = StudioLogConnection
```

- [ ] **Step 2: Syntax-check the file**

```powershell
cd "C:\Users\Admin\Documents\GitHub\companion-module-studiolog"
node --check src/connection.js
```

Expected: no output, exit code 0 (syntax OK). This does not execute the module — `runEntrypoint` requires Companion's own process environment, so full execution is verified in Task 10 inside a real Companion instance.

- [ ] **Step 3: Commit**

```powershell
git add src/connection.js
git commit -m "feat: add TCP connection client with auto-reconnect"
```

---

### Task 8: Implement actions.js and variables.js

**Files:**
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\actions.js`
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\variables.js`

**Interfaces:**
- Consumes: `self.connection.sendCommand(command)` (Task 7, accessed via `main.js`'s `this.connection` in Task 9).
- Produces: 4 actions (`generate_toggle`, `tc_in`, `tc_out`, `mark`) and 3 variable definitions (`generator_running`, `tc_in_active`, `current_timecode`) — consumed by Task 9's `main.js`.

- [ ] **Step 1: Create actions.js**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\actions.js`:

```javascript
module.exports = function (self) {
	self.setActionDefinitions({
		generate_toggle: {
			name: 'Generate/Stop',
			options: [],
			callback: async () => {
				self.connection.sendCommand('GENERATE_TOGGLE')
			},
		},
		tc_in: {
			name: 'Timecode In',
			options: [],
			callback: async () => {
				self.connection.sendCommand('TC_IN')
			},
		},
		tc_out: {
			name: 'Timecode Out',
			options: [],
			callback: async () => {
				self.connection.sendCommand('TC_OUT')
			},
		},
		mark: {
			name: 'Mark',
			options: [],
			callback: async () => {
				self.connection.sendCommand('MARK')
			},
		},
	})
}
```

- [ ] **Step 2: Create variables.js**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\variables.js`:

```javascript
module.exports = function (self) {
	self.setVariableDefinitions({
		generator_running: { name: 'Generator running (true/false)' },
		tc_in_active: { name: 'TC IN entry open (true/false)' },
		current_timecode: { name: 'Current timecode (HH:MM:SS:FF)' },
	})

	self.setVariableValues({
		generator_running: 'false',
		tc_in_active: 'false',
		current_timecode: '00:00:00:00',
	})
}
```

- [ ] **Step 3: Syntax-check both files**

```powershell
cd "C:\Users\Admin\Documents\GitHub\companion-module-studiolog"
node --check src/actions.js
node --check src/variables.js
```

Expected: no output, exit code 0 for both.

- [ ] **Step 4: Commit**

```powershell
git add src/actions.js src/variables.js
git commit -m "feat: add actions and variable definitions"
```

---

### Task 9: Implement feedbacks.js, upgrades.js, and main.js

**Files:**
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\feedbacks.js`
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\upgrades.js`
- Create: `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\main.js`

**Interfaces:**
- Consumes: `StudioLogConnection` (Task 7), `UpdateActions`/`UpdateVariableDefinitions` (Task 8).
- Produces: the `ModuleInstance` class (entrypoint), `self.generatorRunning`/`self.tcInActive`/`self.currentTimecode` state fields read by feedback callbacks, and `self.handleLine(line)` consumed by `connection.js`.

- [ ] **Step 1: Create feedbacks.js**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\feedbacks.js`:

```javascript
module.exports = function (self) {
	self.setFeedbackDefinitions({
		generator_running: {
			name: 'Generator Running',
			type: 'boolean',
			label: 'Generator Running',
			defaultStyle: {
				bgcolor: 0xdc2626,
				color: 0xffffff,
				text: 'STOP',
			},
			options: [],
			callback: () => {
				return self.generatorRunning === true
			},
		},
		tc_in_active: {
			name: 'TC IN Active',
			type: 'boolean',
			label: 'TC IN Active (entry open)',
			defaultStyle: {
				bgcolor: 0xf59e0b,
				color: 0x000000,
			},
			options: [],
			callback: () => {
				return self.tcInActive === true
			},
		},
	})
}
```

- [ ] **Step 2: Create upgrades.js**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\upgrades.js`:

```javascript
module.exports = [
	/*
	 * Place upgrade scripts here for future breaking changes.
	 * Remember that once one has been added it cannot be removed.
	 */
]
```

- [ ] **Step 3: Create main.js**

Create `C:\Users\Admin\Documents\GitHub\companion-module-studiolog\src\main.js`:

```javascript
const { InstanceBase, Regex, runEntrypoint } = require('@companion-module/base')
const UpgradeScripts = require('./upgrades')
const UpdateActions = require('./actions')
const UpdateFeedbacks = require('./feedbacks')
const UpdateVariableDefinitions = require('./variables')
const StudioLogConnection = require('./connection')

class ModuleInstance extends InstanceBase {
	constructor(internal) {
		super(internal)
	}

	async init(config) {
		this.config = config
		this.generatorRunning = false
		this.tcInActive = false
		this.currentTimecode = '00:00:00:00'

		this.updateActions()
		this.updateFeedbacks()
		this.updateVariableDefinitions()

		this.connection = new StudioLogConnection(this)
		this.connection.connect(this.config.host || '127.0.0.1', this.config.port || 51234)
	}

	async destroy() {
		this.connection?.destroy()
	}

	async configUpdated(config) {
		this.config = config
		this.connection?.destroy()
		this.connection = new StudioLogConnection(this)
		this.connection.connect(this.config.host || '127.0.0.1', this.config.port || 51234)
	}

	getConfigFields() {
		return [
			{
				type: 'textinput',
				id: 'host',
				label: 'StudioLog Host',
				width: 8,
				default: '127.0.0.1',
				regex: Regex.IP,
			},
			{
				type: 'textinput',
				id: 'port',
				label: 'StudioLog Port',
				width: 4,
				default: '51234',
				regex: Regex.PORT,
			},
		]
	}

	handleLine(line) {
		const parts = line.split(' ')
		if (parts[0] !== 'STATE') return

		if (parts[1] === 'GENERATOR') {
			this.generatorRunning = parts[2] === 'RUNNING'
			this.setVariableValues({ generator_running: this.generatorRunning ? 'true' : 'false' })
			this.checkFeedbacks('generator_running')
		} else if (parts[1] === 'TC_IN_ACTIVE') {
			this.tcInActive = parts[2] === 'TRUE'
			this.setVariableValues({ tc_in_active: this.tcInActive ? 'true' : 'false' })
			this.checkFeedbacks('tc_in_active')
		} else if (parts[1] === 'TIMECODE') {
			this.currentTimecode = parts[2]
			this.setVariableValues({ current_timecode: this.currentTimecode })
		}
	}

	updateActions() {
		UpdateActions(this)
	}

	updateFeedbacks() {
		UpdateFeedbacks(this)
	}

	updateVariableDefinitions() {
		UpdateVariableDefinitions(this)
	}
}

runEntrypoint(ModuleInstance, UpgradeScripts)
```

- [ ] **Step 4: Syntax-check all three files**

```powershell
cd "C:\Users\Admin\Documents\GitHub\companion-module-studiolog"
node --check src/feedbacks.js
node --check src/upgrades.js
node --check src/main.js
```

Expected: no output, exit code 0 for all three.

- [ ] **Step 5: Commit**

```powershell
git add src/feedbacks.js src/upgrades.js src/main.js
git commit -m "feat: add feedbacks and wire main.js entrypoint"
```

---

### Task 10: Manual end-to-end verification in a running Companion instance

This requires an actual Bitfocus Companion installation (not part of either repo) — perform these steps yourself; they can't be run from this environment.

- [ ] **Step 1: Point Companion at the module**

In Companion, go to Settings and set "Developer modules path" to the **parent** folder containing `companion-module-studiolog` (e.g. if the module is at `C:\Users\Admin\Documents\GitHub\companion-module-studiolog`, set the path to `C:\Users\Admin\Documents\GitHub`). Companion should detect and list the `studiolog` module.

- [ ] **Step 2: Add the connection**

In Companion's Connections tab, add a new "StudioLog" connection. Set Host to `127.0.0.1` and Port to `51234` (or whatever you configured in StudioLog's `SETTINGS > COMPANION CONTROL...`).

- [ ] **Step 3: Confirm connection status**

With StudioLog running and Companion control enabled (Task 5's setup), the connection should show status "OK" within a couple seconds. Stop StudioLog and confirm the connection shows "Connecting" and does not error out; restart StudioLog and confirm it returns to "OK" without touching the Companion connection config.

- [ ] **Step 4: Test all 4 actions**

Add all 4 actions (Generate/Stop, Timecode In, Timecode Out, Mark) to buttons on a Companion page and press each. Confirm each fires the corresponding behavior in StudioLog (matching Task 5's expectations).

- [ ] **Step 5: Test variables**

Open Companion's variables inspector for the `studiolog` connection. Confirm `generator_running`, `tc_in_active`, and `current_timecode` are present and update live as you press buttons in Companion or in the StudioLog app itself.

- [ ] **Step 6: Test feedbacks**

Add the "Generator Running" feedback to the Generate/Stop button (set its own default style to green background / "GENERATE" text) and the "TC IN Active" feedback to the Timecode In button. Confirm the Generate button turns red with "STOP" text while the generator runs, and the Timecode In button gets an amber background while an entry is open.

- [ ] **Step 7: Test a Trigger**

Create a simple Companion Trigger that fires when `$(studiolog:generator_running)` changes to `true` (e.g. it just logs or flashes another button). Toggle Generate/Stop in StudioLog and confirm the trigger fires.

- [ ] **Step 8: Fix any issues found**

If any step fails, fix the relevant file in `companion-module-studiolog` (or `CompanionControlServer.cs`/`MainViewModel.cs` in StudioLog if the wire protocol itself is at fault), re-run the syntax check for that file, and commit the fix with a `fix:` message in the appropriate repo before re-testing.
