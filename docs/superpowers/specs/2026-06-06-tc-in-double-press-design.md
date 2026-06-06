# TC IN Double-Press Auto-Close Design

**Date:** 2026-06-06

## Summary

When TC IN is pressed while an entry is already active (TC IN was previously pressed with no TC OUT yet), automatically close the active entry and open a new one at the same timecode — instead of creating a second open entry.

## Behavior

**Before:** Pressing TC IN with an active entry creates a duplicate open row (no TC Out, no Duration).

**After:** The second TC IN press acts as TC OUT for the current entry and TC IN for the new entry simultaneously, using the same captured timecode for both.

## Scope

Single method change: `TimeCodeIn()` in `MainViewModel.cs`.

No changes to `TimeCodeOut()`, the database schema, or the UI.

Existing sessions with duplicate open entries (from the old behavior) are left as-is.

## Logic

```
capture timecode = CurrentTimecode

if _currentEntry != null:
    _currentEntry.TimeCodeOut = timecode
    _currentEntry.Duration = CalculateDuration(_currentEntry.TimeCodeIn, timecode)
    await _database.UpdateEntry(_currentEntry)
    IsTimecodeInActive = false
    _currentEntry = null

// normal TC IN path continues:
create new entry with TimeCodeIn = timecode
add to DB → get Id
add to LogEntries + SubscribeToEntry
IsTimecodeInActive = true
StatusMessage = "TC IN: {timecode}"
```

## Key Detail

`CurrentTimecode` is read once and reused for both the TC OUT of the closing entry and the TC IN of the new entry, ensuring they share the exact same frame.
