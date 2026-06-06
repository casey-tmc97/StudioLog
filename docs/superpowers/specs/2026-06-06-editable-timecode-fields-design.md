# Editable Timecode In / Out Fields with Auto-Recalculated Duration

**Date:** 2026-06-06

## Summary

Allow users to manually edit TC In and TC Out values in the log grid. When either value changes the Duration column recalculates automatically and the entry is saved to the database.

## Scope

- `MainWindow.axaml` — TC In and TC Out columns: `TextBlock` → `TextBox`
- `TimecodeDatabase.cs` — `UpdateEntry` SQL must also update `TimeCodeIn`
- `MainViewModel.cs` — subscribe to each entry's `PropertyChanged`; on TC In or TC Out change, recalculate duration and call `_database.UpdateEntry(entry)`

## Out of Scope

- Notes column (already TextBlock, not requested)
- Validation UI (invalid timecodes silently keep existing duration)

## Architecture

**ViewModel subscribes to entry PropertyChanged.**

- `SubscribeToEntry(entry)` — called at every `LogEntries.Add(entry)` site
- `UnsubscribeAllEntries()` — called before every `LogEntries.Clear()`
- Handler: if `TimeCodeIn` or `TimeCodeOut` changed and entry has both values, recalculate and `_database.UpdateEntry(entry)` (fire-and-forget async)

**XAML styling** — TC In and TC Out `TextBox` cells use same style as the existing `ClipName` cell: `Background="Transparent"`, `Foreground="White"`, `BorderThickness="0"`, `FontSize="16"`, `Padding="10"`.

**Database** — `UpdateEntry` SQL adds `TimeCodeIn = @TimeCodeIn` to the SET clause and the corresponding parameter.

## Entry Points Where Subscription Must Be Added

| Location | Action |
|---|---|
| `TimeCodeIn()` | subscribe after Add |
| `TimeCodeMark()` | subscribe after Add |
| `OpenSession()` loop | subscribe after Add |
| `NewSession()` / `OpenSession()` / `InitializeAsync()` Clear | unsubscribe-all before Clear |
