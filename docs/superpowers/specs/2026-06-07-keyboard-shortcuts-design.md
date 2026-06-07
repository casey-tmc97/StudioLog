# Keyboard Shortcuts — Design Spec
Date: 2026-06-07

## Overview

Wire up five keyboard shortcuts in the StudioLog Avalonia UI app using Avalonia's `HotKey` attached property on MenuItems (for four direct-action shortcuts) plus a code-behind handler for the Export submenu shortcut.

## Shortcuts

| Shortcut | Action | Target command |
|---|---|---|
| Ctrl+O | Open Session | `OpenSessionCommand` |
| Ctrl+S | Save Session | `SaveSessionCommand` |
| Ctrl+Z | Undo Delete | `UndoDeleteCommand` |
| Ctrl+E | Open Export submenu | (code-behind) |
| Ctrl+Q | Quit / Exit | `ExitCommand` |

## Approach

**Avalonia `HotKey` on MenuItems** for Ctrl+O, Ctrl+S, Ctrl+Z, Ctrl+Q.

Adding `HotKey="Ctrl+X"` to a `<MenuItem>` does two things automatically:
1. Registers the gesture as a global window-level key binding.
2. Renders the shortcut hint in the menu item label (e.g. "Open Session    Ctrl+O").

No ViewModel changes required. All four target commands already exist.

**Code-behind for Ctrl+E** in the existing `OnGlobalKeyDown` tunnel handler. The FILE MenuItem and Export MenuItem will be given `Name` attributes in XAML so they can be located via `FindControl<MenuItem>`. Setting `IsSubMenuOpen = true` on each opens the menu chain.

## File Changes

### `MainWindow.axaml`

1. Add `Name="FileMenuItem"` to the top-level FILE `<MenuItem>`.
2. Add `HotKey="Ctrl+O"` to the "Open Session" `<MenuItem>`.
3. Add `HotKey="Ctrl+S"` to the "Save Session" `<MenuItem>`.
4. Add `HotKey="Ctrl+Z"` to the "Undo Delete" `<MenuItem>`.
5. Add `Name="ExportMenuItem"` to the "Export" nested `<MenuItem>`.
6. Add `HotKey="Ctrl+Q"` to the "Exit" `<MenuItem>`.

### `MainWindow.axaml.cs`

In `OnGlobalKeyDown`, add an `else if` branch:

```csharp
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
```

## Behavioral Notes

- **Ctrl+Z in a focused TextBox**: TextBox's built-in text-undo handler fires first (handled). The window-level Ctrl+Z / UndoDelete does not fire. This is correct — text editing undo and log-entry undo are separate concerns.
- **Ctrl+S in a focused TextBox**: TextBox has no built-in Ctrl+S handler, so Save fires as expected.
- Shortcuts are active whenever the main window has focus.
