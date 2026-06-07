# Mark Sub-Rows Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Marks pressed during an active TC In/TC Out session appear as indented sub-rows in the log grid (showing only a bullet + mark timecode + editable Notes), and the Mark column is removed from the main grid.

**Architecture:** Four sequential changes — (1) add `ParentEntryId` to the model, (2) migrate the DB and update query/insert methods, (3) change `TimeCodeMark()` to create child entries instead of appending to `MarkTimecode`, (4) update the AXAML DataTemplate to show two visual modes based on `IsMarkSubRow`. Standalone marks (no active TC In/TC Out entry) are unchanged.

**Tech Stack:** C#, Avalonia 11.2.2 (compiled bindings), SQLite via `Microsoft.Data.Sqlite`, MVVM / `INotifyPropertyChanged`

---

## Files

| File | Change |
|---|---|
| `TimecodeLogEntry.cs` | Add `ParentEntryId` (int?) + `IsMarkSubRow` computed property |
| `TimecodeDatabase.cs` | Migration for `ParentEntryId` column; update `AddEntry` and `GetSessionEntries` |
| `MainViewModel.cs` | `TimeCodeMark()` active-entry path: create child entry instead of appending to `MarkTimecode` |
| `MainWindow.axaml` | Remove Mark column header; update `ColumnDefinitions`; update DataTemplate for two visual modes |

---

### Task 1: Add `ParentEntryId` to `TimecodeLogEntry`

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\TimecodeLogEntry.cs`

- [ ] **Step 1: Read the current file**

  Read `TimecodeLogEntry.cs` in full to confirm exact content before editing.

- [ ] **Step 2: Add `ParentEntryId` and `IsMarkSubRow`**

  Add the following two members after the `MarkTimecode` property block (before `CreatedAt`).

  ```csharp
  private int? _parentEntryId;
  public int? ParentEntryId
  {
      get => _parentEntryId;
      set { _parentEntryId = value; OnPropertyChanged(); }
  }

  public bool IsMarkSubRow => ParentEntryId.HasValue;
  ```

  The full file should look like this after the edit:

  ```csharp
  using System;
  using System.ComponentModel;
  using System.Runtime.CompilerServices;

  namespace StudioLog.Models
  {
      public class TimecodeLogEntry : INotifyPropertyChanged
      {
          public event PropertyChangedEventHandler? PropertyChanged;

          public int Id { get; set; }
          public int SessionId { get; set; }

          private string _timeCodeIn = string.Empty;
          public string TimeCodeIn
          {
              get => _timeCodeIn;
              set { _timeCodeIn = value; OnPropertyChanged(); }
          }

          private string _timeCodeOut = string.Empty;
          public string TimeCodeOut
          {
              get => _timeCodeOut;
              set { _timeCodeOut = value; OnPropertyChanged(); }
          }

          private string _duration = string.Empty;
          public string Duration
          {
              get => _duration;
              set { _duration = value; OnPropertyChanged(); }
          }

          private string _clipName = string.Empty;
          public string ClipName
          {
              get => _clipName;
              set { _clipName = value; OnPropertyChanged(); }
          }

          private string _notes = string.Empty;
          public string Notes
          {
              get => _notes;
              set { _notes = value; OnPropertyChanged(); }
          }

          private string _markTimecode = string.Empty;
          public string MarkTimecode
          {
              get => _markTimecode;
              set { _markTimecode = value; OnPropertyChanged(); }
          }

          private int? _parentEntryId;
          public int? ParentEntryId
          {
              get => _parentEntryId;
              set { _parentEntryId = value; OnPropertyChanged(); }
          }

          public bool IsMarkSubRow => ParentEntryId.HasValue;

          public DateTime CreatedAt { get; set; } = DateTime.Now;

          protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
          {
              PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
          }
      }
  }
  ```

- [ ] **Step 3: Build to confirm no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```bash
  git add TimecodeLogEntry.cs
  git commit -m "feat: add ParentEntryId and IsMarkSubRow to TimecodeLogEntry"
  ```

---

### Task 2: DB migration + update `AddEntry` and `GetSessionEntries`

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\TimecodeDatabase.cs`

The `GetSessionEntries` method currently uses `SELECT *` and reads columns by position (0–8). After the migration adds `ParentEntryId`, it appears at position 9. The ORDER BY must place each child row immediately after its parent.

- [ ] **Step 1: Read `InitializeDatabase()` to understand migration pattern**

  Read `TimecodeDatabase.cs` lines 78–100 to see the existing `MarkTimecode` migration pattern. The new migration follows the same `pragma_table_info` check approach.

- [ ] **Step 2: Add `ParentEntryId` migration inside `InitializeDatabase()`**

  After the existing `MarkTimecode` migration block (around line 99), add:

  ```csharp
  // Migration: Add ParentEntryId column if it doesn't exist
  try
  {
      var checkParentCol = "SELECT COUNT(*) FROM pragma_table_info('LogEntries') WHERE name='ParentEntryId'";
      using (var command = new SqliteCommand(checkParentCol, _connection))
      {
          var columnExists = Convert.ToInt32(command.ExecuteScalar()) > 0;
          if (!columnExists)
          {
              var addColumn = "ALTER TABLE LogEntries ADD COLUMN ParentEntryId INTEGER NULL";
              using (var alterCommand = new SqliteCommand(addColumn, _connection))
              {
                  alterCommand.ExecuteNonQuery();
              }
          }
      }
  }
  catch (Exception)
  {
      // Column might already exist - continue
  }
  ```

- [ ] **Step 3: Update `AddEntry` to persist `ParentEntryId`**

  Find the current `AddEntry` method (around line 260). Replace the SQL and parameters with:

  ```csharp
  public async Task<int> AddEntry(TimecodeLogEntry entry, int sessionId)
  {
      if (_connection == null) throw new InvalidOperationException("Database not initialized");

      var sql = @"
          INSERT INTO LogEntries (SessionId, TimeCodeIn, TimeCodeOut, Duration, ClipName, Notes, MarkTimecode, ParentEntryId)
          VALUES (@SessionId, @TimeCodeIn, @TimeCodeOut, @Duration, @ClipName, @Notes, @MarkTimecode, @ParentEntryId);
          SELECT last_insert_rowid();";

      using var command = new SqliteCommand(sql, _connection);
      command.Parameters.AddWithValue("@SessionId", sessionId);
      command.Parameters.AddWithValue("@TimeCodeIn", entry.TimeCodeIn);
      command.Parameters.AddWithValue("@TimeCodeOut", entry.TimeCodeOut ?? "");
      command.Parameters.AddWithValue("@Duration", entry.Duration ?? "");
      command.Parameters.AddWithValue("@ClipName", entry.ClipName ?? "");
      command.Parameters.AddWithValue("@Notes", entry.Notes ?? "");
      command.Parameters.AddWithValue("@MarkTimecode", entry.MarkTimecode ?? "");
      command.Parameters.AddWithValue("@ParentEntryId", (object?)entry.ParentEntryId ?? DBNull.Value);

      var result = await command.ExecuteScalarAsync();
      return Convert.ToInt32(result);
  }
  ```

- [ ] **Step 4: Update `GetSessionEntries` to read `ParentEntryId` and order children after parents**

  Replace the `GetSessionEntries` method (around line 308) with:

  ```csharp
  public async Task<List<TimecodeLogEntry>> GetSessionEntries(int sessionId)
  {
      if (_connection == null) return new List<TimecodeLogEntry>();

      var entries = new List<TimecodeLogEntry>();
      var sql = @"
          SELECT * FROM LogEntries
          WHERE SessionId = @SessionId
          ORDER BY COALESCE(ParentEntryId, Id), (ParentEntryId IS NOT NULL), Id";

      using var command = new SqliteCommand(sql, _connection);
      command.Parameters.AddWithValue("@SessionId", sessionId);
      using var reader = await command.ExecuteReaderAsync();

      while (await reader.ReadAsync())
      {
          entries.Add(new TimecodeLogEntry
          {
              Id = reader.GetInt32(0),
              SessionId = reader.GetInt32(1),
              TimeCodeIn = reader.GetString(2),
              TimeCodeOut = reader.IsDBNull(3) ? "" : reader.GetString(3),
              Duration = reader.IsDBNull(4) ? "" : reader.GetString(4),
              ClipName = reader.IsDBNull(5) ? "" : reader.GetString(5),
              Notes = reader.IsDBNull(6) ? "" : reader.GetString(6),
              MarkTimecode = reader.IsDBNull(7) ? "" : reader.GetString(7),
              CreatedAt = reader.GetDateTime(8),
              ParentEntryId = reader.IsDBNull(9) ? null : reader.GetInt32(9)
          });
      }

      return entries;
  }
  ```

  The ORDER BY logic: `COALESCE(ParentEntryId, Id)` groups each child (ParentEntryId = parent's Id) alongside its parent (ParentEntryId = NULL, so COALESCE returns its own Id). `(ParentEntryId IS NOT NULL)` sorts 0 (false/parent) before 1 (true/child) within each group. `Id` breaks ties by insertion order.

- [ ] **Step 5: Build to confirm no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

  ```bash
  git add TimecodeDatabase.cs
  git commit -m "feat: add ParentEntryId DB migration, update AddEntry and GetSessionEntries"
  ```

---

### Task 3: Update `TimeCodeMark()` to create child entries

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainViewModel.cs`

When `_currentEntry != null`, instead of appending to `_currentEntry.MarkTimecode`, create a new child `TimecodeLogEntry` with `ParentEntryId = _currentEntry.Id`. The standalone-mark path (`_currentEntry == null`) is unchanged.

- [ ] **Step 1: Read the current `TimeCodeMark()` method**

  Read `MainViewModel.cs` from line 962 to ~1020 to confirm exact current text.

- [ ] **Step 2: Replace the `_currentEntry != null` branch**

  Find this block inside `TimeCodeMark()`:

  ```csharp
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
  ```

  Replace it with:

  ```csharp
  if (_currentEntry != null)
  {
      var markEntry = new TimecodeLogEntry
      {
          ParentEntryId = _currentEntry.Id,
          MarkTimecode = currentTimecode,
          Notes = "",
          SessionId = _currentSession.Id
      };

      int entryId = await _database.AddEntry(markEntry, _currentSession.Id);
      markEntry.Id = entryId;

      await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
      {
          LogEntries.Add(markEntry);
          SubscribeToEntry(markEntry);
          _hasUnsavedChanges = true;
          StatusMessage = $"MARK added: {currentTimecode}";
      });
  }
  ```

  Leave the `else` branch (standalone mark) exactly as-is.

- [ ] **Step 3: Build to confirm no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```bash
  git add MainViewModel.cs
  git commit -m "feat: TimeCodeMark creates child sub-row entry instead of appending to MarkTimecode"
  ```

---

### Task 4: Update `MainWindow.axaml` — remove Mark column, add sub-row visual

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainWindow.axaml`

The grid changes from 6 columns (`150,150,150,200,150,250`) to 5 columns (`150,150,150,200,300`). The old Mark column (col 4) is removed. The Notes column grows from 250 to 300. Both the header row grid and the DataTemplate grid need updating.

Sub-row cells use `IsVisible="{Binding !IsMarkSubRow}"` (Avalonia 11 compiled-binding negation) to hide normal cells, and `IsVisible="{Binding IsMarkSubRow}"` to show sub-row-only cells.

- [ ] **Step 1: Read the header row and DataTemplate in `MainWindow.axaml`**

  Read `MainWindow.axaml` from line 375 to ~450 to confirm current content.

- [ ] **Step 2: Update the header row `ColumnDefinitions` and remove the Mark column header**

  Find:
  ```xml
  <Grid ColumnDefinitions="150,150,150,200,150,250">
      <TextBlock Grid.Column="0" Text="TIMECODE IN" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="1" Text="TIMECODE OUT" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="2" Text="DURATION" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="3" Text="CLIP NAME" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="4" Text="MARK" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="5" Text="NOTES" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
  </Grid>
  ```

  Replace with:
  ```xml
  <Grid ColumnDefinitions="150,150,150,200,300">
      <TextBlock Grid.Column="0" Text="TIMECODE IN" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="1" Text="TIMECODE OUT" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="2" Text="DURATION" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="3" Text="CLIP NAME" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
      <TextBlock Grid.Column="4" Text="NOTES" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
  </Grid>
  ```

- [ ] **Step 3: Update the DataTemplate row**

  Find the entire `<DataTemplate x:DataType="models:TimecodeLogEntry">` block (lines ~391–446). Replace it with:

  ```xml
  <DataTemplate x:DataType="models:TimecodeLogEntry">
      <Border Background="#3a3a3a" BorderBrush="#555555" BorderThickness="1,0,1,1" Padding="5">
          <Grid ColumnDefinitions="150,150,150,200,300">
              <!-- Col 0: TC IN for normal rows; bullet + mark timecode for sub-rows -->
              <TextBox Grid.Column="0"
                       Text="{Binding TimeCodeIn}"
                       Background="Transparent"
                       Foreground="White"
                       FontSize="16"
                       Padding="10"
                       BorderThickness="0"
                       VerticalAlignment="Center"
                       IsVisible="{Binding !IsMarkSubRow}"/>
              <TextBlock Grid.Column="0"
                         Text="{Binding MarkTimecode, StringFormat='  •  {0}'}"
                         Foreground="#aaaaaa"
                         FontSize="16"
                         Padding="10"
                         VerticalAlignment="Center"
                         IsVisible="{Binding IsMarkSubRow}"/>
              <!-- Col 1: TC OUT (hidden for sub-rows) -->
              <TextBox Grid.Column="1"
                       Text="{Binding TimeCodeOut}"
                       Background="Transparent"
                       Foreground="White"
                       FontSize="16"
                       Padding="10"
                       BorderThickness="0"
                       VerticalAlignment="Center"
                       IsVisible="{Binding !IsMarkSubRow}"/>
              <!-- Col 2: Duration (hidden for sub-rows) -->
              <TextBlock Grid.Column="2"
                         Text="{Binding Duration}"
                         Foreground="White"
                         FontSize="16"
                         Padding="10"
                         TextWrapping="Wrap"
                         VerticalAlignment="Center"
                         IsVisible="{Binding !IsMarkSubRow}"/>
              <!-- Col 3: Clip Name (hidden for sub-rows) -->
              <TextBox Grid.Column="3"
                       Text="{Binding ClipName}"
                       Background="Transparent"
                       Foreground="White"
                       FontSize="16"
                       Padding="10"
                       BorderThickness="0"
                       TextWrapping="Wrap"
                       AcceptsReturn="False"
                       VerticalAlignment="Center"
                       IsVisible="{Binding !IsMarkSubRow}"/>
              <!-- Col 4: Notes — always visible and editable for all row types -->
              <TextBox Grid.Column="4"
                       Text="{Binding Notes}"
                       Background="Transparent"
                       Foreground="White"
                       FontSize="16"
                       Padding="10"
                       BorderThickness="0"
                       TextWrapping="Wrap"
                       AcceptsReturn="False"
                       VerticalAlignment="Center"/>
          </Grid>
      </Border>
  </DataTemplate>
  ```

  The `•` is the bullet character `•` encoded as a Unicode escape, which works in XAML string literals.

- [ ] **Step 4: Build to confirm no compile errors**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

  ```bash
  git add MainWindow.axaml
  git commit -m "feat: mark sub-rows visual — remove Mark column, add bullet sub-row style"
  ```

---

### Task 5: Manual verification

- [ ] **Step 1: Run the app**

  ```
  dotnet run
  ```

- [ ] **Step 2: Test mark sub-rows during active entry**

  Click GENERATE to start the timecode. Press TC IN (a row appears, button flashes). Press MARK. Confirm:
  - A second row appears immediately below the TC IN row
  - The sub-row shows `  •  HH:MM:SS:FF` in the first column (grey text)
  - TC OUT, Duration, and Clip Name columns are empty on the sub-row
  - A Notes cell is visible and editable on the sub-row

- [ ] **Step 3: Test multiple marks**

  Press MARK again without closing the entry. Confirm a second sub-row appears below the first sub-row.

- [ ] **Step 4: Test sub-row Notes auto-save**

  Click into the Notes cell of a sub-row, type something, press Tab. Confirm the status bar shows `Entry updated: ...`.

- [ ] **Step 5: Test TC OUT closes active entry without affecting sub-rows**

  Press TC OUT. Confirm the parent row gets TC Out and Duration filled in. Sub-rows remain in place below it.

- [ ] **Step 6: Test standalone mark (no active entry)**

  With no active entry, press MARK. Confirm: a standalone row appears with TC In = current timecode, Notes = "MARK", no bullet prefix. Behavior unchanged from before.

- [ ] **Step 7: Test session save + reload preserves sub-rows**

  Save the session (or let autosave run). Close the app and reopen. Load the session. Confirm sub-rows still appear correctly indented below their parent entries.

- [ ] **Step 8: Confirm Mark column is gone**

  Verify no "MARK" column header appears in the grid. Verify standalone mark rows still show their timecode in the TC IN column.
