# Remove Mark Dialog + Editable Notes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the MarkNotesWindow pop-out dialog so MARK captures timecode instantly, and make the Notes column in the log grid an editable, auto-saving TextBox.

**Architecture:** Two independent changes: (1) strip dialog code from `TimeCodeMark()` and delete the window files, (2) extend `OnEntryPropertyChanged` to handle `Notes` changes and swap the Notes TextBlock to TextBox. Both follow patterns already established in the codebase.

**Tech Stack:** C#, Avalonia UI, SQLite via `Microsoft.Data.Sqlite`, MVVM with `INotifyPropertyChanged`

---

## Files

| File | Change |
|---|---|
| `MainViewModel.cs` | Simplify `TimeCodeMark()`; extend `OnEntryPropertyChanged` for Notes |
| `MainWindow.axaml` | Notes column: `TextBlock` → `TextBox` |
| `MarkNotesWindow.axaml` | Delete |
| `MarkNotesWindow.axaml.cs` | Delete |

---

### Task 1: Simplify `TimeCodeMark()` and delete MarkNotesWindow files

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainViewModel.cs` — `TimeCodeMark()` method (~line 941)
- Delete: `C:\Users\Admin\Documents\GitHub\StudioLog\MarkNotesWindow.axaml`
- Delete: `C:\Users\Admin\Documents\GitHub\StudioLog\MarkNotesWindow.axaml.cs`

- [ ] **Step 1: Read the current `TimeCodeMark()` method**

  Read `MainViewModel.cs` from line 941 to ~1045 to confirm the exact current text.

- [ ] **Step 2: Replace the entire `TimeCodeMark()` method**

  Replace the full method (from `public async void TimeCodeMark()` through its closing brace) with:

  ```csharp
  public async void TimeCodeMark()
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

          string currentTimecode = CurrentTimecode;

          if (_currentEntry != null)
          {
              if (string.IsNullOrWhiteSpace(_currentEntry.MarkTimecode))
                  _currentEntry.MarkTimecode = currentTimecode;
              else
                  _currentEntry.MarkTimecode += ", " + currentTimecode;

              await _database.UpdateEntry(_currentEntry);

              await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
              {
                  StatusMessage = $"MARK added to current entry: {currentTimecode}";
              });
          }
          else
          {
              var markEntry = new TimecodeLogEntry
              {
                  TimeCodeIn = currentTimecode,
                  MarkTimecode = currentTimecode,
                  Notes = "MARK",
                  SessionId = _currentSession.Id
              };

              int entryId = await _database.AddEntry(markEntry, _currentSession.Id);
              markEntry.Id = entryId;

              await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
              {
                  LogEntries.Add(markEntry);
                  SubscribeToEntry(markEntry);
                  _hasUnsavedChanges = true;
                  StatusMessage = $"MARK created: {currentTimecode}";
              });
          }
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

- [ ] **Step 3: Delete `MarkNotesWindow.axaml` and `MarkNotesWindow.axaml.cs`**

  ```bash
  git rm "C:\Users\Admin\Documents\GitHub\StudioLog\MarkNotesWindow.axaml"
  git rm "C:\Users\Admin\Documents\GitHub\StudioLog\MarkNotesWindow.axaml.cs"
  ```

- [ ] **Step 4: Build to verify no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors. (Removing the dialog and its files should leave no dangling references since `TimeCodeMark()` was the only caller.)

- [ ] **Step 5: Commit**

  ```bash
  git add MainViewModel.cs
  git commit -m "feat: remove mark dialog — MARK now captures timecode instantly"
  ```

---

### Task 2: Extend `OnEntryPropertyChanged` to auto-save Notes edits

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainViewModel.cs` — `OnEntryPropertyChanged` method (~line 1599)

- [ ] **Step 1: Read the current `OnEntryPropertyChanged` method**

  Read `MainViewModel.cs` around line 1599 to confirm the exact current filter condition.

  The current filter looks like:
  ```csharp
  if (e.PropertyName != nameof(TimecodeLogEntry.TimeCodeIn) &&
      e.PropertyName != nameof(TimecodeLogEntry.TimeCodeOut)) return;

  if (!string.IsNullOrEmpty(entry.TimeCodeIn) && !string.IsNullOrEmpty(entry.TimeCodeOut))
  {
      entry.Duration = CalculateDuration(entry.TimeCodeIn, entry.TimeCodeOut);
  }
  ```

- [ ] **Step 2: Update the filter and duration recalculation guard**

  Replace those two blocks with:

  ```csharp
  if (e.PropertyName != nameof(TimecodeLogEntry.TimeCodeIn) &&
      e.PropertyName != nameof(TimecodeLogEntry.TimeCodeOut) &&
      e.PropertyName != nameof(TimecodeLogEntry.Notes)) return;

  if ((e.PropertyName == nameof(TimecodeLogEntry.TimeCodeIn) ||
       e.PropertyName == nameof(TimecodeLogEntry.TimeCodeOut)) &&
      !string.IsNullOrEmpty(entry.TimeCodeIn) && !string.IsNullOrEmpty(entry.TimeCodeOut))
  {
      entry.Duration = CalculateDuration(entry.TimeCodeIn, entry.TimeCodeOut);
  }
  ```

  This adds Notes to the trigger list while ensuring duration is only recalculated for TC changes, not Notes changes.

- [ ] **Step 3: Build to verify no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```bash
  git add MainViewModel.cs
  git commit -m "feat: auto-save Notes edits via PropertyChanged subscription"
  ```

---

### Task 3: Make Notes column editable in the XAML

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainWindow.axaml` — Notes column in DataTemplate (~line 427)

- [ ] **Step 1: Read the Notes column in the DataTemplate**

  Read `MainWindow.axaml` around line 427 to confirm current content:
  ```xml
  <TextBlock Grid.Column="5" 
             Text="{Binding Notes}" 
             Foreground="White" 
             FontSize="16" 
             Padding="10"
             TextWrapping="Wrap"
             VerticalAlignment="Center"/>
  ```

- [ ] **Step 2: Replace the Notes `TextBlock` with a `TextBox`**

  Replace with:
  ```xml
  <TextBox Grid.Column="5"
           Text="{Binding Notes}"
           Background="Transparent"
           Foreground="White"
           FontSize="16"
           Padding="10"
           BorderThickness="0"
           TextWrapping="Wrap"
           AcceptsReturn="False"
           VerticalAlignment="Center"/>
  ```

- [ ] **Step 3: Build to verify no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```bash
  git add MainWindow.axaml
  git commit -m "feat: make Notes column editable in log grid"
  ```

---

### Task 4: Manual verification

- [ ] **Step 1: Run the app**

  ```
  dotnet run
  ```

- [ ] **Step 2: Test instant MARK with active entry** — click GENERATE, press TC IN (row appears, flashing). Press MARK. Confirm: no dialog appears, mark timecode appended to the MARK column of the active row, status bar shows `MARK added to current entry: ...`.

- [ ] **Step 3: Test standalone MARK** — press TC OUT to close the entry, then press MARK without a TC IN. Confirm: new row created with TC In = current timecode, MARK column filled, Notes = "MARK", no dialog.

- [ ] **Step 4: Test editable Notes** — click into the Notes cell of any row, type something, press Tab. Confirm: text saves, status bar shows `Entry updated: ...`.

- [ ] **Step 5: Test Notes persist** — save session, close, reopen. Confirm edited Notes values are present.

- [ ] **Step 6: Confirm MarkNotesWindow is gone** — verify no `.axaml` or `.cs` file for it exists in the project directory.
