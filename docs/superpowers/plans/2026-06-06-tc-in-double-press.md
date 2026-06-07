# TC IN Double-Press Auto-Close Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When TC IN is pressed while an entry is already active, automatically close the active entry (TC Out + Duration) and open a new one — all at the same timecode snapshot.

**Architecture:** Single method change in `TimeCodeIn()`. Capture `CurrentTimecode` once at the top; if `_currentEntry != null`, close it with that timecode before creating the new entry. No other files change.

**Tech Stack:** C#, Avalonia UI, SQLite via `Microsoft.Data.Sqlite`

---

## Files

| File | Change |
|---|---|
| `MainViewModel.cs` | Modify `TimeCodeIn()` to detect and close an active entry before opening a new one |

---

### Task 1: Modify `TimeCodeIn()` to auto-close active entry on double-press

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainViewModel.cs` — `TimeCodeIn()` method (~line 867)

- [ ] **Step 1: Read the current `TimeCodeIn()` method to confirm its exact content**

  Read `MainViewModel.cs` from line 867 to ~905 to confirm the exact text before editing.

- [ ] **Step 2: Replace the entire `TimeCodeIn()` method**

  The current method starts with `public async void TimeCodeIn()`. Replace the full method body with the following. The only structural additions are: (a) capturing `timecode` at the top, and (b) the `if (_currentEntry != null)` block that closes the active entry.

  ```csharp
  public async void TimeCodeIn()
  {
      try
      {
          if (_currentSession == null)
          {
              await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
              {
                  StatusMessage = "No active session - create new session first";
              });
              return;
          }

          string timecode = CurrentTimecode;

          if (_currentEntry != null)
          {
              _currentEntry.TimeCodeOut = timecode;
              _currentEntry.Duration = CalculateDuration(_currentEntry.TimeCodeIn, timecode);
              await _database.UpdateEntry(_currentEntry);

              await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
              {
                  IsTimecodeInActive = false;
              });

              _currentEntry = null;
          }

          _currentEntry = new TimecodeLogEntry
          {
              TimeCodeIn = timecode,
              SessionId = _currentSession.Id
          };

          int entryId = await _database.AddEntry(_currentEntry, _currentSession.Id);
          _currentEntry.Id = entryId;

          await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
          {
              LogEntries.Add(_currentEntry);
              SubscribeToEntry(_currentEntry);
              IsTimecodeInActive = true;
              _hasUnsavedChanges = true;
              StatusMessage = $"TC IN: {timecode}";
          });
      }
      catch (Exception ex)
      {
          await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
          {
              StatusMessage = $"Error: {ex.Message}";
          });
      }
  }
  ```

- [ ] **Step 3: Build to verify no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```bash
  git add MainViewModel.cs
  git commit -m "feat: TC IN double-press auto-closes active entry and opens new one"
  ```

---

### Task 2: Manual verification

- [ ] **Step 1: Run the app**

  ```
  dotnet run
  ```

- [ ] **Step 2: Verify normal TC IN → TC OUT flow still works** — click GENERATE, press TC IN, wait a few seconds, press TC OUT. Confirm a complete row appears with TC In, TC Out, and Duration.

- [ ] **Step 3: Verify double-press TC IN flow** — press TC IN (row appears, button flashes). Without pressing TC OUT, press TC IN again. Confirm:
  - The first row now has a TC Out and Duration filled in (same timecode as the second TC In)
  - A second row appears immediately with a new TC In at the same timecode
  - The flashing indicator continues on the new active entry

- [ ] **Step 4: Verify triple-press** — press TC IN three times in a row without TC OUT. Confirm two complete rows and one open row, each sharing adjacent timecodes at the boundary.

- [ ] **Step 5: Verify TC OUT still closes the final open entry correctly** after a double-press sequence.
