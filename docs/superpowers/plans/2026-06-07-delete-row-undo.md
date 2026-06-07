# Delete Row + Undo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a ✕ delete button to every log row that removes the row (plus any child sub-rows) from the grid and DB, with a FILE > Undo Delete menu item that restores the last deletion.

**Architecture:** Three sequential changes — (1) add `DeleteEntry`, `DeleteChildEntries`, and `RestoreEntry` to the DB layer, (2) add `DeleteEntryCommand`, `UndoDeleteCommand`, a `CanUndo` property, and an undo stack to the ViewModel, (3) add the ✕ button column and Undo menu item to the AXAML. Undo restores entries with their original IDs via `INSERT OR REPLACE` and reloads the grid from DB.

**Tech Stack:** C#, Avalonia 11.2.2 (compiled + reflection bindings), SQLite via `Microsoft.Data.Sqlite`, ReactiveUI commands, MVVM

---

## Files

| File | Change |
|---|---|
| `TimecodeDatabase.cs` | Add `DeleteEntry`, `DeleteChildEntries`, `RestoreEntry` |
| `MainViewModel.cs` | Add undo stack, `CanUndo`, `DeleteEntryCommand`, `UndoDeleteCommand`, wire in constructor |
| `MainWindow.axaml` | Add 7th delete-button column to header + DataTemplate; add Undo Delete menu item |

---

### Task 1: Add DB methods — `DeleteEntry`, `DeleteChildEntries`, `RestoreEntry`

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\TimecodeDatabase.cs`

- [ ] **Step 1: Read `TimecodeDatabase.cs`** to find the end of the existing public methods (just before `Dispose()`), around line 360.

- [ ] **Step 2: Add the three new methods** immediately before `Dispose()`:

```csharp
public async Task DeleteEntry(int id)
{
    if (_connection == null) return;
    var sql = "DELETE FROM LogEntries WHERE Id = @Id";
    using var command = new SqliteCommand(sql, _connection);
    command.Parameters.AddWithValue("@Id", id);
    await command.ExecuteNonQueryAsync();
}

public async Task DeleteChildEntries(int parentId)
{
    if (_connection == null) return;
    var sql = "DELETE FROM LogEntries WHERE ParentEntryId = @ParentEntryId";
    using var command = new SqliteCommand(sql, _connection);
    command.Parameters.AddWithValue("@ParentEntryId", parentId);
    await command.ExecuteNonQueryAsync();
}

public async Task RestoreEntry(TimecodeLogEntry entry)
{
    if (_connection == null) return;
    var sql = @"
        INSERT OR REPLACE INTO LogEntries
            (Id, SessionId, TimeCodeIn, TimeCodeOut, Duration, ClipName, Notes, MarkTimecode, CreatedAt, ParentEntryId)
        VALUES
            (@Id, @SessionId, @TimeCodeIn, @TimeCodeOut, @Duration, @ClipName, @Notes, @MarkTimecode, @CreatedAt, @ParentEntryId)";
    using var command = new SqliteCommand(sql, _connection);
    command.Parameters.AddWithValue("@Id", entry.Id);
    command.Parameters.AddWithValue("@SessionId", entry.SessionId);
    command.Parameters.AddWithValue("@TimeCodeIn", entry.TimeCodeIn);
    command.Parameters.AddWithValue("@TimeCodeOut", entry.TimeCodeOut ?? "");
    command.Parameters.AddWithValue("@Duration", entry.Duration ?? "");
    command.Parameters.AddWithValue("@ClipName", entry.ClipName ?? "");
    command.Parameters.AddWithValue("@Notes", entry.Notes ?? "");
    command.Parameters.AddWithValue("@MarkTimecode", entry.MarkTimecode ?? "");
    command.Parameters.AddWithValue("@CreatedAt", entry.CreatedAt);
    command.Parameters.AddWithValue("@ParentEntryId", (object?)entry.ParentEntryId ?? DBNull.Value);
    await command.ExecuteNonQueryAsync();
}
```

- [ ] **Step 3: Build**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```bash
  git add TimecodeDatabase.cs
  git commit -m "feat: add DeleteEntry, DeleteChildEntries, RestoreEntry to DB"
  ```

---

### Task 2: Add undo stack, `DeleteEntryCommand`, `UndoDeleteCommand` to ViewModel

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainViewModel.cs`

The undo stack stores each delete operation as a `List<TimecodeLogEntry>` — the deleted parent plus any children. Undo pops the list, restores every entry to DB via `RestoreEntry`, then reloads `LogEntries` from DB (which preserves correct child-after-parent ordering automatically).

- [ ] **Step 1: Read `MainViewModel.cs` lines 1–35** to confirm the field declarations area, and **lines 195–270** to confirm the command declarations and constructor.

- [ ] **Step 2: Add the undo stack field and `CanUndo` property**

  In the private fields section (after `_hasUnsavedChanges`, around line 30), add:

  ```csharp
  private readonly Stack<List<TimecodeLogEntry>> _undoStack = new();
  ```

  After the existing public properties (find `public ObservableCollection<TimecodeLogEntry> LogEntries` or the StatusMessage property), add:

  ```csharp
  public bool CanUndo => _undoStack.Count > 0;
  ```

- [ ] **Step 3: Declare the two new commands**

  In the `ICommand` declarations block (around line 215, after `ExitCommand`), add:

  ```csharp
  public ICommand DeleteEntryCommand { get; }
  public ICommand UndoDeleteCommand { get; }
  ```

- [ ] **Step 4: Wire the commands in the constructor**

  In the constructor (after `ExitCommand = ReactiveCommand.Create(Exit);`, around line 269), add:

  ```csharp
  DeleteEntryCommand = ReactiveCommand.Create<TimecodeLogEntry>(DeleteEntry);
  UndoDeleteCommand = ReactiveCommand.Create(UndoDelete);
  ```

- [ ] **Step 5: Add the `DeleteEntry` method**

  Add this method near the other timecode action methods (e.g. after `TimeCodeMark()`):

  ```csharp
  private async void DeleteEntry(TimecodeLogEntry entry)
  {
      try
      {
          if (_currentSession == null) return;

          // Collect everything being deleted: children first, then the entry itself
          var toDelete = new List<TimecodeLogEntry>();
          if (!entry.IsMarkSubRow)
          {
              var children = LogEntries.Where(e => e.ParentEntryId == entry.Id).ToList();
              toDelete.AddRange(children);
          }
          toDelete.Add(entry);

          // Delete from DB (children before parent to avoid FK concerns)
          if (!entry.IsMarkSubRow)
              await _database.DeleteChildEntries(entry.Id);
          await _database.DeleteEntry(entry.Id);

          // Remove from UI
          await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
          {
              foreach (var e in toDelete)
              {
                  e.PropertyChanged -= OnEntryPropertyChanged;
                  LogEntries.Remove(e);
              }

              // Clear _currentEntry if it was deleted
              if (_currentEntry != null && toDelete.Contains(_currentEntry))
              {
                  _currentEntry = null;
                  IsTimecodeInActive = false;
              }

              _undoStack.Push(toDelete);
              OnPropertyChanged(nameof(CanUndo));
              _hasUnsavedChanges = true;
              StatusMessage = $"Deleted {toDelete.Count} row(s). Use File > Undo Delete to restore.";
          });
      }
      catch (Exception ex)
      {
          await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
          {
              StatusMessage = $"Delete error: {ex.Message}";
          });
      }
  }
  ```

- [ ] **Step 6: Add the `UndoDelete` method**

  Add immediately after `DeleteEntry`:

  ```csharp
  private async void UndoDelete()
  {
      try
      {
          if (_undoStack.Count == 0) return;
          if (_currentSession == null) return;

          var toRestore = _undoStack.Pop();

          foreach (var entry in toRestore)
              await _database.RestoreEntry(entry);

          // Reload entries from DB so ordering is correct
          var entries = await _database.GetSessionEntries(_currentSession.Id);

          await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
          {
              UnsubscribeAllEntries();
              LogEntries.Clear();
              foreach (var e in entries)
              {
                  LogEntries.Add(e);
                  SubscribeToEntry(e);
              }
              OnPropertyChanged(nameof(CanUndo));
              _hasUnsavedChanges = true;
              StatusMessage = $"Undo: restored {toRestore.Count} row(s).";
          });
      }
      catch (Exception ex)
      {
          await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
          {
              StatusMessage = $"Undo error: {ex.Message}";
          });
      }
  }
  ```

- [ ] **Step 7: Build**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 8: Commit**

  ```bash
  git add MainViewModel.cs
  git commit -m "feat: add DeleteEntry/UndoDelete with stack-based undo to ViewModel"
  ```

---

### Task 3: Update `MainWindow.axaml` — delete button column + Undo menu item

**Files:**
- Modify: `C:\Users\Admin\Documents\GitHub\StudioLog\MainWindow.axaml`

The grid gains a 7th column (40px) for the delete button. The header row gets a blank spacer in col 6. The DataTemplate gets a `✕` Button in col 6 whose `Command` reaches up to the ViewModel via `{ReflectionBinding}` (required because the DataTemplate's `x:DataType` is `TimecodeLogEntry`, not the ViewModel).

- [ ] **Step 1: Read `MainWindow.axaml` lines 88–103** (menu) and **lines 375–465** (grid) to confirm current content.

- [ ] **Step 2: Add `Undo Delete` menu item** in the FILE menu

  Find the first `<Separator/>` after `Open Session` (line 95):
  ```xml
                  <MenuItem Header="Open Session" Command="{Binding OpenSessionCommand}"/>
                  <Separator/>
                  <MenuItem Header="Export">
  ```

  Replace with:
  ```xml
                  <MenuItem Header="Open Session" Command="{Binding OpenSessionCommand}"/>
                  <Separator/>
                  <MenuItem Header="Undo Delete" Command="{Binding UndoDeleteCommand}" IsEnabled="{Binding CanUndo}"/>
                  <Separator/>
                  <MenuItem Header="Export">
  ```

- [ ] **Step 3: Update the header row** to 7 columns

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
                            <Grid ColumnDefinitions="150,150,150,200,150,250,40">
                                <TextBlock Grid.Column="0" Text="TIMECODE IN" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
                                <TextBlock Grid.Column="1" Text="TIMECODE OUT" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
                                <TextBlock Grid.Column="2" Text="DURATION" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
                                <TextBlock Grid.Column="3" Text="CLIP NAME" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
                                <TextBlock Grid.Column="4" Text="MARK" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
                                <TextBlock Grid.Column="5" Text="NOTES" FontWeight="Bold" Foreground="White" FontSize="14" Padding="10"/>
                            </Grid>
  ```
  (Only `ColumnDefinitions` changes — adds `,40` at the end. No 7th header TextBlock needed.)

- [ ] **Step 4: Update the DataTemplate** to 7 columns with delete button

  Find `<Grid ColumnDefinitions="150,150,150,200,150,250">` inside the DataTemplate and replace with `<Grid ColumnDefinitions="150,150,150,200,150,250,40">`.

  Then, just before `</Grid>` (the closing tag of the DataTemplate's inner Grid), add:

  ```xml
                                            <!-- Col 6: Delete button -->
                                            <Button Grid.Column="6"
                                                    Command="{ReflectionBinding $parent[ItemsControl].DataContext.DeleteEntryCommand}"
                                                    CommandParameter="{Binding}"
                                                    Content="✕"
                                                    Background="Transparent"
                                                    Foreground="#cc4444"
                                                    BorderThickness="0"
                                                    FontSize="14"
                                                    Padding="4"
                                                    HorizontalAlignment="Center"
                                                    VerticalAlignment="Center"
                                                    Cursor="Hand"/>
  ```

- [ ] **Step 5: Build**

  ```
  dotnet build
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

  ```bash
  git add MainWindow.axaml
  git commit -m "feat: add delete button column and Undo Delete menu item"
  ```

---

### Task 4: Manual verification

- [ ] **Step 1: Run the app** — `dotnet run`

- [ ] **Step 2: Test delete a parent row with sub-rows** — Press TC IN, press MARK twice, press TC OUT. Three rows appear (one parent, two sub-rows). Click ✕ on the parent. Confirm: all three rows disappear. Status bar shows "Deleted 3 row(s)."

- [ ] **Step 3: Test undo** — Open FILE menu. Confirm "Undo Delete" is enabled. Click it. Confirm all three rows reappear in correct order. Status bar shows "Undo: restored 3 row(s)."

- [ ] **Step 4: Test delete a sub-row only** — Click ✕ on a sub-row. Confirm only that sub-row disappears; the parent and other sub-rows remain.

- [ ] **Step 5: Test undo sub-row delete** — FILE > Undo Delete. Confirm the sub-row is restored.

- [ ] **Step 6: Test undo is disabled when stack is empty** — With no pending undos, confirm "Undo Delete" in FILE menu is greyed out.

- [ ] **Step 7: Test multiple undo levels** — Delete two separate rows. Undo twice. Confirm each undo restores the correct row in order (LIFO).
