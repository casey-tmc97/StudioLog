# Editable Timecode In/Out Fields Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make TC In and TC Out cells in the log grid editable; when either changes, duration is recalculated and the entry is auto-saved to the database.

**Architecture:** The ViewModel subscribes to each `TimecodeLogEntry`'s `PropertyChanged` event when it's added to `LogEntries`. On `TimeCodeIn` or `TimeCodeOut` change, the handler recalculates duration and calls `_database.UpdateEntry()`. The DB's `UpdateEntry` is extended to also persist `TimeCodeIn`. The XAML swaps those two `TextBlock` cells to `TextBox`.

**Tech Stack:** C#, Avalonia UI, SQLite via `Microsoft.Data.Sqlite`, MVVM with `INotifyPropertyChanged`

---

## Files

| File | Change |
|---|---|
| `TimecodeDatabase.cs` | Extend `UpdateEntry` SQL and parameter to include `TimeCodeIn` |
| `MainViewModel.cs` | Add `SubscribeToEntry`, `UnsubscribeAllEntries`, `OnEntryPropertyChanged`; wire up at every Add/Clear site |
| `MainWindow.axaml` | TC In and TC Out columns: `TextBlock` → `TextBox` |

---

### Task 1: Extend `UpdateEntry` to persist `TimeCodeIn`

**Files:**
- Modify: `TimecodeDatabase.cs` — `UpdateEntry` method (~line 282)

- [ ] **Step 1: Update the SQL and add the parameter**

  In `TimecodeDatabase.cs`, replace the `UpdateEntry` method body with:

  ```csharp
  public async Task UpdateEntry(TimecodeLogEntry entry)
  {
      if (_connection == null) return;

      var sql = @"
          UPDATE LogEntries 
          SET TimeCodeIn = @TimeCodeIn,
              TimeCodeOut = @TimeCodeOut, 
              Duration = @Duration, 
              ClipName = @ClipName, 
              Notes = @Notes,
              MarkTimecode = @MarkTimecode
          WHERE Id = @Id";

      using var command = new SqliteCommand(sql, _connection);
      command.Parameters.AddWithValue("@TimeCodeIn", entry.TimeCodeIn ?? "");
      command.Parameters.AddWithValue("@TimeCodeOut", entry.TimeCodeOut ?? "");
      command.Parameters.AddWithValue("@Duration", entry.Duration ?? "");
      command.Parameters.AddWithValue("@ClipName", entry.ClipName ?? "");
      command.Parameters.AddWithValue("@Notes", entry.Notes ?? "");
      command.Parameters.AddWithValue("@MarkTimecode", entry.MarkTimecode ?? "");
      command.Parameters.AddWithValue("@Id", entry.Id);

      await command.ExecuteNonQueryAsync();
  }
  ```

- [ ] **Step 2: Build to verify no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

  ```bash
  git add TimecodeDatabase.cs
  git commit -m "feat: persist TimeCodeIn in UpdateEntry"
  ```

---

### Task 2: Add entry PropertyChanged subscription infrastructure to ViewModel

**Files:**
- Modify: `MainViewModel.cs`

- [ ] **Step 1: Add the three subscription methods**

  In `MainViewModel.cs`, add these three methods before the `Dispose()` method (around line 1582):

  ```csharp
  private void SubscribeToEntry(TimecodeLogEntry entry)
  {
      entry.PropertyChanged += OnEntryPropertyChanged;
  }

  private void UnsubscribeAllEntries()
  {
      foreach (var entry in LogEntries)
          entry.PropertyChanged -= OnEntryPropertyChanged;
  }

  private async void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
  {
      if (sender is not TimecodeLogEntry entry) return;
      if (e.PropertyName != nameof(TimecodeLogEntry.TimeCodeIn) &&
          e.PropertyName != nameof(TimecodeLogEntry.TimeCodeOut)) return;

      if (!string.IsNullOrEmpty(entry.TimeCodeIn) && !string.IsNullOrEmpty(entry.TimeCodeOut))
      {
          entry.Duration = CalculateDuration(entry.TimeCodeIn, entry.TimeCodeOut);
      }

      try
      {
          await _database.UpdateEntry(entry);
          await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
          {
              StatusMessage = $"Entry updated: TC In {entry.TimeCodeIn}  TC Out {entry.TimeCodeOut}";
          });
      }
      catch (Exception ex)
      {
          Console.WriteLine($"[Entry] Auto-save error: {ex.Message}");
      }
  }
  ```

- [ ] **Step 2: Build to verify no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

  ```bash
  git add MainViewModel.cs
  git commit -m "feat: add entry PropertyChanged subscription infrastructure"
  ```

---

### Task 3: Wire subscriptions at every LogEntries Add and Clear site

**Files:**
- Modify: `MainViewModel.cs`

There are three `LogEntries.Add()` call sites and three `LogEntries.Clear()` call sites to update.

- [ ] **Step 1: Wire subscription in `TimeCodeIn()` (~line 889)**

  After `LogEntries.Add(_currentEntry);`, add:
  ```csharp
  SubscribeToEntry(_currentEntry);
  ```

  The block should look like:
  ```csharp
  await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
  {
      LogEntries.Add(_currentEntry);
      SubscribeToEntry(_currentEntry);
      IsTimecodeInActive = true;
      _hasUnsavedChanges = true;
      StatusMessage = $"TC IN: {CurrentTimecode}";
  });
  ```

- [ ] **Step 2: Wire subscription in `TimeCodeMark()` (~line 1026)**

  After `LogEntries.Add(markEntry);`, add:
  ```csharp
  SubscribeToEntry(markEntry);
  ```

  The block should look like:
  ```csharp
  await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
  {
      LogEntries.Add(markEntry);
      SubscribeToEntry(markEntry);
      _hasUnsavedChanges = true;
      StatusMessage = $"MARK created: {currentTimecode}";
  });
  ```

- [ ] **Step 3: Wire subscription in `OpenSession()` entry loop (~line 600)**

  Replace:
  ```csharp
  foreach (var entry in sessionData.Entries)
  {
      LogEntries.Add(entry);
  }
  ```
  With:
  ```csharp
  foreach (var entry in sessionData.Entries)
  {
      LogEntries.Add(entry);
      SubscribeToEntry(entry);
  }
  ```

- [ ] **Step 4: Wire unsubscribe before `LogEntries.Clear()` in `NewSession()` (~line 407)**

  Replace:
  ```csharp
  LogEntries.Clear();
  ```
  With:
  ```csharp
  UnsubscribeAllEntries();
  LogEntries.Clear();
  ```

- [ ] **Step 5: Wire unsubscribe before `LogEntries.Clear()` in `OpenSession()` (~line 601)**

  In the `await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync` block, replace:
  ```csharp
  LogEntries.Clear();
  ```
  With:
  ```csharp
  UnsubscribeAllEntries();
  LogEntries.Clear();
  ```

- [ ] **Step 6: Wire unsubscribe before `LogEntries.Clear()` in `InitializeAsync()` (~line 313)**

  Replace:
  ```csharp
  LogEntries.Clear();
  ```
  With:
  ```csharp
  UnsubscribeAllEntries();
  LogEntries.Clear();
  ```

- [ ] **Step 7: Build to verify no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

  ```bash
  git add MainViewModel.cs
  git commit -m "feat: wire entry PropertyChanged subscriptions at all Add/Clear sites"
  ```

---

### Task 4: Make TC In and TC Out cells editable in the XAML

**Files:**
- Modify: `MainWindow.axaml` — DataTemplate grid (~lines 394–413)

- [ ] **Step 1: Replace the TC In `TextBlock` with a `TextBox`**

  Replace:
  ```xml
  <TextBlock Grid.Column="0" 
             Text="{Binding TimeCodeIn}" 
             Foreground="White" 
             FontSize="16" 
             Padding="10"
             TextWrapping="Wrap"
             VerticalAlignment="Center"/>
  ```
  With:
  ```xml
  <TextBox Grid.Column="0"
           Text="{Binding TimeCodeIn}"
           Background="Transparent"
           Foreground="White"
           FontSize="16"
           Padding="10"
           BorderThickness="0"
           VerticalAlignment="Center"/>
  ```

- [ ] **Step 2: Replace the TC Out `TextBlock` with a `TextBox`**

  Replace:
  ```xml
  <TextBlock Grid.Column="1" 
             Text="{Binding TimeCodeOut}" 
             Foreground="White" 
             FontSize="16" 
             Padding="10"
             TextWrapping="Wrap"
             VerticalAlignment="Center"/>
  ```
  With:
  ```xml
  <TextBox Grid.Column="1"
           Text="{Binding TimeCodeOut}"
           Background="Transparent"
           Foreground="White"
           FontSize="16"
           Padding="10"
           BorderThickness="0"
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
  git commit -m "feat: make TC In and TC Out cells editable in log grid"
  ```

---

### Task 5: Manual verification

- [ ] **Step 1: Run the app**

  ```
  dotnet run
  ```

- [ ] **Step 2: Log a few entries** — click GENERATE, TC IN, TC OUT a couple of times.

- [ ] **Step 3: Edit a TC In value** — click into a TC In cell, change the timecode (e.g. `00:01:00:00` → `00:01:10:00`), press Tab or click away. Verify:
  - Duration column updates to the new calculated value
  - Status bar shows "Entry updated: TC In ... TC Out ..."

- [ ] **Step 4: Edit a TC Out value** — same test, verify duration recalculates.

- [ ] **Step 5: Edit TC In on a row with no TC Out** — verify the cell saves without error (no duration change, status updates).

- [ ] **Step 6: Save the session, close, re-open** — verify the edited TC In/Out values persisted correctly.

- [ ] **Step 7: Commit (if any fixups were made)**

  ```bash
  git add -p
  git commit -m "fix: address manual verification findings"
  ```
