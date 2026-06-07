# Keyboard Shortcuts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire up Ctrl+O (Open), Ctrl+S (Save), Ctrl+Z (Undo Delete), Ctrl+E (Export submenu), and Ctrl+Q (Quit) in the StudioLog Avalonia UI app.

**Architecture:** Avalonia's `HotKey` attached property on the four direct-action `<MenuItem>` elements handles both shortcut registration and menu-label hint rendering. Ctrl+E is handled in the existing `OnGlobalKeyDown` tunnel handler in code-behind by setting `IsSubMenuOpen = true` on the FILE and Export MenuItems (located by Name).

**Tech Stack:** Avalonia 11.2.2, .NET 9, C#, XAML (.axaml)

---

### Task 1: Add HotKey attributes and Name attributes in XAML

**Files:**
- Modify: `MainWindow.axaml` (Menu section, lines 91–105)

- [ ] **Step 1: Add `Name="FileMenuItem"` to the top-level FILE `<MenuItem>`**

In `MainWindow.axaml`, change line 91 from:

```xml
<MenuItem Header="FILE" Foreground="White">
```

to:

```xml
<MenuItem Name="FileMenuItem" Header="FILE" Foreground="White">
```

- [ ] **Step 2: Add `HotKey="Ctrl+O"` to Open Session**

Change:

```xml
<MenuItem Header="Open Session" Command="{Binding OpenSessionCommand}"/>
```

to:

```xml
<MenuItem Header="Open Session" Command="{Binding OpenSessionCommand}" HotKey="Ctrl+O"/>
```

- [ ] **Step 3: Add `HotKey="Ctrl+S"` to Save Session**

Change:

```xml
<MenuItem Header="Save Session" Command="{Binding SaveSessionCommand}"/>
```

to:

```xml
<MenuItem Header="Save Session" Command="{Binding SaveSessionCommand}" HotKey="Ctrl+S"/>
```

- [ ] **Step 4: Add `HotKey="Ctrl+Z"` to Undo Delete**

Change:

```xml
<MenuItem Header="Undo Delete" Command="{Binding UndoDeleteCommand}" IsEnabled="{Binding CanUndo}"/>
```

to:

```xml
<MenuItem Header="Undo Delete" Command="{Binding UndoDeleteCommand}" IsEnabled="{Binding CanUndo}" HotKey="Ctrl+Z"/>
```

- [ ] **Step 5: Add `Name="ExportMenuItem"` to the Export submenu item**

Change:

```xml
<MenuItem Header="Export">
```

to:

```xml
<MenuItem Name="ExportMenuItem" Header="Export">
```

- [ ] **Step 6: Add `HotKey="Ctrl+Q"` to Exit**

Change:

```xml
<MenuItem Header="Exit" Command="{Binding ExitCommand}"/>
```

to:

```xml
<MenuItem Header="Exit" Command="{Binding ExitCommand}" HotKey="Ctrl+Q"/>
```

- [ ] **Step 7: Build to verify XAML is valid**

```powershell
dotnet build StudioLog.csproj
```

Expected: `Build succeeded` with 0 errors. Avalonia's compiled bindings will catch any typos in HotKey strings at compile time.

- [ ] **Step 8: Commit**

```bash
git add MainWindow.axaml
git commit -m "feat: add HotKey shortcuts to FILE menu items (Ctrl+O/S/Z/Q)"
```

---

### Task 2: Add Ctrl+E handler in code-behind

**Files:**
- Modify: `MainWindow.axaml.cs` (method `OnGlobalKeyDown`, lines 135–146)

- [ ] **Step 1: Add `Ctrl+E` branch to `OnGlobalKeyDown`**

The current method body is:

```csharp
private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
{
    if (e.Key == Key.Return || e.Key == Key.Enter)
    {
        if (e.Source is TextBox textBox)
        {
            this.FocusManager?.ClearFocus();
            e.Handled = true;
        }
    }
}
```

Add the Ctrl+E branch so it becomes:

```csharp
private void OnGlobalKeyDown(object? sender, KeyEventArgs e)
{
    if (e.Key == Key.Return || e.Key == Key.Enter)
    {
        if (e.Source is TextBox textBox)
        {
            this.FocusManager?.ClearFocus();
            e.Handled = true;
        }
    }
    else if (e.Key == Key.E && e.KeyModifiers == KeyModifiers.Control)
    {
        var fileMenuItem = this.FindControl<MenuItem>("FileMenuItem");
        var exportMenuItem = this.FindControl<MenuItem>("ExportMenuItem");
        if (fileMenuItem != null && exportMenuItem != null)
        {
            fileMenuItem.IsSubMenuOpen = true;
            exportMenuItem.IsSubMenuOpen = true;
        }
        e.Handled = true;
    }
}
```

`KeyModifiers` is already in `Avalonia.Input` which is imported at the top of the file.

- [ ] **Step 2: Build to verify no compile errors**

```powershell
dotnet build StudioLog.csproj
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add MainWindow.axaml.cs
git commit -m "feat: add Ctrl+E handler to open Export submenu"
```

---

### Task 3: Smoke test all five shortcuts

Run the app:

```powershell
dotnet run --project StudioLog.csproj
```

- [ ] **Ctrl+O** — File open dialog appears (or session picker, matching existing OpenSessionCommand behavior)
- [ ] **Ctrl+S** — Session saves; status bar shows save confirmation message
- [ ] **Ctrl+Z** — With a deleted entry in the undo stack: entry is restored. With empty undo stack: nothing happens (button is disabled, shortcut is a no-op)
- [ ] **Ctrl+E** — FILE menu opens with Export submenu expanded, showing PDF / CSV / PNG options
- [ ] **Ctrl+Q** — App exits (matching existing ExitCommand behavior)
- [ ] **Ctrl+Z in a TextBox** — Typing in Session Name, then Ctrl+Z undoes the text edit only; no log entries are affected
- [ ] **Shortcut hints visible in menu** — Open FILE menu and confirm each item shows its shortcut label (e.g. "Open Session  Ctrl+O")
