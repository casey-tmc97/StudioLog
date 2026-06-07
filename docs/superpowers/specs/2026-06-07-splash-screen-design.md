# Splash Screen Design

**Date:** 2026-06-07  
**Status:** Approved

## Overview

Add a loading splash screen that displays the custom icon artwork (`SplashScreen.png`) for 2 seconds while `MainWindow` initializes in the background. No text overlay. Image stands alone.

## Artwork

- File: `Splash Screen.png` → renamed to `SplashScreen.png`
- Source dimensions: 1586×992 px
- Logical display size: 480×300 px at 96 DPI (same aspect ratio — no distortion)
- Added to `.csproj` as `AvaloniaResource` so it is embedded in the binary

## SplashWindow

- New files: `SplashWindow.axaml` + `SplashWindow.axaml.cs`
- `SystemDecorations="None"` — no title bar, no border chrome
- `CanResize="False"`
- `WindowStartupLocation="CenterScreen"`
- `Width="480"`, `Height="300"`
- `Topmost="True"` — stays above taskbar/other windows during splash
- `Background="Black"` — fills any sub-pixel gap if image doesn't cover edge exactly
- Single `Image` control with `Stretch="Fill"` fills the window edge-to-edge

## Startup Sequence (Approach B)

`App.axaml.cs` `OnFrameworkInitializationCompleted` is changed to `async void`:

1. Instantiate and show `SplashWindow` — set as `desktop.MainWindow` immediately so the app lifecycle is happy
2. Instantiate `MainWindow` on the UI thread — any ViewModel initialization runs during the splash delay
3. `await Task.Delay(2000)` — minimum 2-second display
4. Set `desktop.MainWindow = mainWindow`, call `mainWindow.Show()`, then `splash.Close()`

## Files Changed

| File | Change |
|------|--------|
| `Splash Screen.png` | Renamed to `SplashScreen.png` |
| `StudioLog.csproj` | Add `SplashScreen.png` as `AvaloniaResource` |
| `SplashWindow.axaml` | New — borderless image window |
| `SplashWindow.axaml.cs` | New — empty code-behind |
| `App.axaml.cs` | Updated startup sequence |

## Out of Scope

- Fade in/out animation
- Progress indicator
- Version number or text overlay
