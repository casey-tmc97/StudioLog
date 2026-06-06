# Remove Mark Dialog + Editable Notes Field Design

**Date:** 2026-06-06

## Summary

Two related changes: (1) pressing MARK captures the timecode instantly with no pop-out dialog, and (2) the Notes column in the log grid becomes an editable TextBox that auto-saves to the database.

## Scope

- `MainViewModel.cs` — simplify `TimeCodeMark()` to remove all dialog code; extend `OnEntryPropertyChanged` to handle `Notes` changes
- `MainWindow.axaml` — Notes column: `TextBlock` → `TextBox`
- `MarkNotesWindow.axaml` — delete
- `MarkNotesWindow.axaml.cs` — delete

## Behavior Changes

### Mark button (no dialog)

**Before:** Pressing MARK opens a dialog for notes input. OK confirms, Cancel aborts.

**After:** Pressing MARK captures `CurrentTimecode` immediately with no dialog.

- If an active entry exists (`_currentEntry != null`): append the mark timecode to `_currentEntry.MarkTimecode` (comma-separated if multiple marks already exist) and call `_database.UpdateEntry(_currentEntry)`. No notes change.
- If no active entry: create a standalone mark entry with `TimeCodeIn = currentTimecode`, `MarkTimecode = currentTimecode`, `Notes = "MARK"`, add to `LogEntries`, subscribe, save to DB.

The `okClicked` check and Cancel path are removed entirely.

### Notes field (editable grid cell)

- Notes column in the DataTemplate becomes a `TextBox` styled identically to the existing ClipName cell: `Background="Transparent"`, `Foreground="White"`, `FontSize="16"`, `Padding="10"`, `BorderThickness="0"`, `VerticalAlignment="Center"`.
- `OnEntryPropertyChanged` is extended to also handle `nameof(TimecodeLogEntry.Notes)` — on change, calls `_database.UpdateEntry(entry)` and updates `StatusMessage`. No duration recalculation for Notes changes.

## Files to Delete

- `MarkNotesWindow.axaml`
- `MarkNotesWindow.axaml.cs`

These files are only referenced from `TimeCodeMark()` in `MainViewModel.cs`. Once that reference is removed, they are dead code.
