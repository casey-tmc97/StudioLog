# Mark Sub-Rows Design

**Date:** 2026-06-06

## Summary

Marks captured during an active TC In/TC Out entry appear as indented sub-rows directly below their parent entry in the log grid. Sub-rows show only a bullet + mark timecode (in the TC In column) and an editable Notes field. The Mark column is removed from the grid entirely. Standalone mark entries (pressed outside TC In/TC Out) are unchanged.

## Scope

- `TimecodeLogEntry.cs` — add `ParentEntryId` (nullable `int?`) property + `IsMarkSubRow` computed property
- `TimecodeDatabase.cs` — DB migration to add `ParentEntryId` column; update `AddEntry` and `GetSessionEntries`
- `MainViewModel.cs` — `TimeCodeMark()` active-entry path creates child entry instead of appending to `MarkTimecode`; `OpenSession()` / `InitializeAsync()` load entries in order (children after parents)
- `MainWindow.axaml` — remove Mark column header + DataTemplate cell; add sub-row visual (bullet + timecode in col 0, empty cols 1-3, editable Notes in col 4); update `ColumnDefinitions` from 6 to 5 columns

## Data Model

Add to `TimecodeLogEntry`:

```csharp
private int? _parentEntryId;
public int? ParentEntryId
{
    get => _parentEntryId;
    set { _parentEntryId = value; OnPropertyChanged(); }
}

public bool IsMarkSubRow => ParentEntryId.HasValue;
```

`IsMarkSubRow` is a pure computed property — no backing field, not persisted, not observed.

## Database Migration

In `TimecodeDatabase.cs`, `InitializeDatabase()` already runs on startup. Add a migration step after table creation:

```sql
ALTER TABLE LogEntries ADD COLUMN ParentEntryId INTEGER NULL;
```

Wrapped in a `try/catch` so it's a no-op if the column already exists (SQLite `ALTER TABLE ADD COLUMN` is idempotent via this pattern).

`GetSessionEntries` ORDER BY clause: `ORDER BY COALESCE(ParentEntryId, Id), ParentEntryId IS NOT NULL, Id` — this groups each child immediately after its parent while preserving insertion order within each group.

`AddEntry` passes `ParentEntryId` as a parameter (nullable). For existing callers that don't set it, the default is NULL.

## Behavior Changes

### MARK during active TC In/TC Out (`_currentEntry != null`)

**Before:** Appends timecode to `_currentEntry.MarkTimecode` (comma-separated) and calls `UpdateEntry`.

**After:** Creates a new child `TimecodeLogEntry`:
- `ParentEntryId = _currentEntry.Id`
- `MarkTimecode = currentTimecode`
- `Notes = ""`
- `SessionId = _currentSession.Id`
- `TimeCodeIn`, `TimeCodeOut`, `Duration`, `ClipName` all null/empty

Inserts the child entry into the DB, adds it to `LogEntries` immediately after the parent entry (by insertion order, child naturally follows parent since parent was just added), and subscribes via `SubscribeToEntry`.

### MARK with no active entry (standalone, `_currentEntry == null`)

**No change.** Creates a standalone `TimecodeLogEntry` with `TimeCodeIn = currentTimecode`, `MarkTimecode = currentTimecode`, `Notes = "MARK"`, no `ParentEntryId`.

### Old comma-separated MarkTimecode data

Left as-is. Existing sessions with MarkTimecode values on parent rows continue to display that field in the TC In column (since standalone marks already used `TimeCodeIn`). No migration of old mark data.

## Grid Layout

New `ColumnDefinitions`: `"150,150,150,200,250"` (5 columns, Mark column removed)

| # | Header | Normal row | Sub-row |
|---|--------|-----------|---------|
| 0 | TC IN | TextBox bound to `TimeCodeIn` | TextBlock: `"  •  " + MarkTimecode` |
| 1 | TC OUT | TextBox bound to `TimeCodeOut` | empty TextBlock |
| 2 | DURATION | TextBlock bound to `Duration` | empty TextBlock |
| 3 | CLIP NAME | TextBox bound to `ClipName` | empty TextBlock |
| 4 | NOTES | TextBox bound to `Notes` | TextBox bound to `Notes` |

Sub-row visual differentiation uses `IsConverter`-style approach: `IsVisible` on each cell controlled by `IsMarkSubRow`. In Avalonia, use a `DataTrigger` or converter:
- Normal cells: `IsVisible="{Binding !IsMarkSubRow}"` (shown when NOT a sub-row)
- Sub-row cells: `IsVisible="{Binding IsMarkSubRow}"` (shown only for sub-rows)

The sub-row col 0 TextBlock displays `"  •  "` as a prefix for visual indentation.

## Auto-Save for Sub-Row Notes

`OnEntryPropertyChanged` already handles `Notes` changes and calls `_database.UpdateEntry(entry)`. Sub-row entries are subscribed via `SubscribeToEntry` at creation — no additional changes needed.

## Files Changed

| File | Change |
|---|---|
| `TimecodeLogEntry.cs` | Add `ParentEntryId` (int?), `IsMarkSubRow` (computed) |
| `TimecodeDatabase.cs` | Migration for `ParentEntryId` column; update `AddEntry`, `GetSessionEntries` |
| `MainViewModel.cs` | `TimeCodeMark()` active path: create child entry; load order preserved naturally |
| `MainWindow.axaml` | Remove Mark column; add sub-row DataTemplate cells; update ColumnDefinitions |

## Out of Scope

- Migrating existing comma-separated `MarkTimecode` values to child rows
- Deleting or re-parenting child entries
- Collapsing/expanding sub-row groups
