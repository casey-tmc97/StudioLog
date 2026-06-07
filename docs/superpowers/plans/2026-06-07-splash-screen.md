# Splash Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a borderless 480×300 splash window with custom artwork for 2 seconds while MainWindow initializes, then swap seamlessly to the main UI.

**Architecture:** A new `SplashWindow` (no chrome, image fills it edge-to-edge) is shown first as `desktop.MainWindow`. While it displays, `MainWindow` is constructed on the UI thread. After `Task.Delay(2000)`, `desktop.MainWindow` is swapped to `MainWindow` before `SplashWindow.Close()` is called, keeping the app lifetime intact.

**Tech Stack:** Avalonia 11.2, .NET 9, C#, AvaloniaResource for embedded PNG

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `Splash Screen.png` | Rename → `SplashScreen.png` | Artwork asset (spaces in resource paths are unreliable) |
| `StudioLog.csproj` | Modify | Embed `SplashScreen.png` as `AvaloniaResource` |
| `SplashWindow.axaml` | Create | Borderless 480×300 window with image filling it |
| `SplashWindow.axaml.cs` | Create | Minimal code-behind (partial class + InitializeComponent) |
| `App.axaml.cs` | Modify | Async startup: show splash → build MainWindow → delay → swap |

---

### Task 1: Rename artwork and embed as resource

**Files:**
- Rename: `Splash Screen.png` → `SplashScreen.png`
- Modify: `StudioLog.csproj`

- [ ] **Step 1: Rename the file using git mv to preserve history**

```bash
git mv "Splash Screen.png" SplashScreen.png
```

- [ ] **Step 2: Add SplashScreen.png as an AvaloniaResource in StudioLog.csproj**

Open `StudioLog.csproj`. After the existing `<ItemGroup>` that contains `icon.ico` and `colorbars.png`, add a new item group:

```xml
  <ItemGroup>
    <AvaloniaResource Include="SplashScreen.png"/>
  </ItemGroup>
```

The full updated csproj `<ItemGroup>` blocks should look like:

```xml
  <ItemGroup>
    <None Include="icon.ico">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Include="colorbars.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
    <None Include="libltc.dll">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>

  <ItemGroup>
    <AvaloniaResource Include="SplashScreen.png"/>
  </ItemGroup>
```

- [ ] **Step 3: Verify the build still compiles**

```bash
dotnet build StudioLog.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 4: Commit**

```bash
git add StudioLog.csproj SplashScreen.png
git commit -m "feat: add SplashScreen.png as embedded AvaloniaResource"
```

---

### Task 2: Create SplashWindow

**Files:**
- Create: `SplashWindow.axaml`
- Create: `SplashWindow.axaml.cs`

- [ ] **Step 1: Create SplashWindow.axaml**

Create `SplashWindow.axaml` in the project root (same level as `MainWindow.axaml`):

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="StudioLog.SplashWindow"
        Width="480"
        Height="300"
        CanResize="False"
        SystemDecorations="None"
        WindowStartupLocation="CenterScreen"
        Topmost="True"
        Background="Black">
    <Image Source="avares://StudioLog/SplashScreen.png"
           Stretch="Fill"/>
</Window>
```

- [ ] **Step 2: Create SplashWindow.axaml.cs**

Create `SplashWindow.axaml.cs` in the project root:

```csharp
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace StudioLog
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }
    }
}
```

- [ ] **Step 3: Verify the build compiles**

```bash
dotnet build StudioLog.csproj
```

Expected: `Build succeeded.` with 0 errors. If you see "Could not find resource avares://StudioLog/SplashScreen.png", confirm Task 1 Step 2 was completed and the file is named exactly `SplashScreen.png` (no space).

- [ ] **Step 4: Commit**

```bash
git add SplashWindow.axaml SplashWindow.axaml.cs
git commit -m "feat: add SplashWindow — borderless 480x300 splash with artwork"
```

---

### Task 3: Update App.axaml.cs startup sequence

**Files:**
- Modify: `App.axaml.cs`

- [ ] **Step 1: Replace OnFrameworkInitializationCompleted in App.axaml.cs**

The current `App.axaml.cs` is:

```csharp
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace StudioLog
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            // CRITICAL FIX: Configure ReactiveUI to use Avalonia's dispatcher
            RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
            
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

Replace it entirely with:

```csharp
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;

namespace StudioLog
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            // CRITICAL FIX: Configure ReactiveUI to use Avalonia's dispatcher
            RxApp.MainThreadScheduler = AvaloniaScheduler.Instance;
            
            AvaloniaXamlLoader.Load(this);
        }

        public override async void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var splash = new SplashWindow();
                desktop.MainWindow = splash;
                splash.Show();

                var mainWindow = new MainWindow();
                await Task.Delay(2000);

                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                splash.Close();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
```

- [ ] **Step 2: Verify the build compiles**

```bash
dotnet build StudioLog.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Run the app and verify splash behavior**

```bash
dotnet run --project StudioLog.csproj
```

Verify:
- Splash window appears centered on screen immediately on launch
- No title bar or window chrome visible
- Artwork fills the window edge-to-edge (no letterboxing or black bars)
- After exactly 2 seconds, splash closes and MainWindow opens
- MainWindow behaves normally (timecode display, buttons, menus all functional)
- Closing MainWindow exits the app cleanly

- [ ] **Step 4: Commit**

```bash
git add App.axaml.cs
git commit -m "feat: show SplashWindow for 2s on startup before opening MainWindow"
```
