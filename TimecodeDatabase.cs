using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using StudioLog.Models;

namespace StudioLog.Core
{
    public class TimecodeDatabase : IDisposable
    {
        private readonly string _connectionString;
        private SqliteConnection? _connection;
        private bool _disposed;

        public TimecodeDatabase(string dbPath)
        {
            string? directory = Path.GetDirectoryName(dbPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connectionString = $"Data Source={dbPath}";
            InitializeDatabase();
        }

        private void InitializeDatabase()
        {
            _connection = new SqliteConnection(_connectionString);
            _connection.Open();

            var createSessionsTable = @"
                CREATE TABLE IF NOT EXISTS Sessions (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionName TEXT NOT NULL,
                    Date TEXT NOT NULL,
                    Location TEXT NOT NULL,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    ClosedAt DATETIME,
                    IsActive INTEGER DEFAULT 1
                )";

            using (var command = new SqliteCommand(createSessionsTable, _connection))
            {
                command.ExecuteNonQuery();
            }

            var createLogEntriesTable = @"
                CREATE TABLE IF NOT EXISTS LogEntries (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL,
                    TimeCodeIn TEXT NOT NULL,
                    TimeCodeOut TEXT,
                    Duration TEXT,
                    ClipName TEXT,
                    Notes TEXT,
                    MarkTimecode TEXT,
                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                    FOREIGN KEY (SessionId) REFERENCES Sessions(Id)
                )";

            using (var command = new SqliteCommand(createLogEntriesTable, _connection))
            {
                command.ExecuteNonQuery();
            }

            var createIndexes = @"
                CREATE INDEX IF NOT EXISTS idx_session_id ON LogEntries(SessionId);
                CREATE INDEX IF NOT EXISTS idx_session_active ON Sessions(IsActive);
            ";

            using (var command = new SqliteCommand(createIndexes, _connection))
            {
                command.ExecuteNonQuery();
            }

            // Migration: Add MarkTimecode column if it doesn't exist
            try
            {
                var checkColumn = "SELECT COUNT(*) FROM pragma_table_info('LogEntries') WHERE name='MarkTimecode'";
                using (var command = new SqliteCommand(checkColumn, _connection))
                {
                    var columnExists = Convert.ToInt32(command.ExecuteScalar()) > 0;
                    
                    if (!columnExists)
                    {
                        var addColumn = "ALTER TABLE LogEntries ADD COLUMN MarkTimecode TEXT";
                        using (var alterCommand = new SqliteCommand(addColumn, _connection))
                        {
                            alterCommand.ExecuteNonQuery();
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Column might already exist or migration failed - continue anyway
            }

            // Migration: Rename ArtistName to SessionName in Sessions table
            try
            {
                var checkColumn = "SELECT COUNT(*) FROM pragma_table_info('Sessions') WHERE name='SessionName'";
                using (var command = new SqliteCommand(checkColumn, _connection))
                {
                    var columnExists = Convert.ToInt32(command.ExecuteScalar()) > 0;
                    
                    if (!columnExists)
                    {
                        using var transaction = _connection.BeginTransaction();
                        try
                        {
                            var migration = @"
                                ALTER TABLE Sessions RENAME TO Sessions_Old;
                                CREATE TABLE Sessions (
                                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                    SessionName TEXT NOT NULL,
                                    Date TEXT NOT NULL,
                                    Location TEXT NOT NULL,
                                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                                    ClosedAt DATETIME,
                                    IsActive INTEGER DEFAULT 1
                                );
                                INSERT INTO Sessions (Id, SessionName, Date, Location, CreatedAt, ClosedAt, IsActive)
                                SELECT Id, ArtistName, Date, Location, CreatedAt, ClosedAt, IsActive FROM Sessions_Old;
                                DROP TABLE Sessions_Old;
                            ";
                            using (var alterCommand = new SqliteCommand(migration, _connection, transaction))
                            {
                                alterCommand.ExecuteNonQuery();
                            }
                            transaction.Commit();
                            Console.WriteLine("[DB] Migration: ArtistName -> SessionName completed");
                        }
                        catch
                        {
                            transaction.Rollback();
                            Console.WriteLine("[DB] Migration: ArtistName -> SessionName rolled back");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] SessionName migration error: {ex.Message}");
            }

            // Migration: Rename SongTitle to ClipName in LogEntries table
            try
            {
                var checkColumn = "SELECT COUNT(*) FROM pragma_table_info('LogEntries') WHERE name='ClipName'";
                using (var command = new SqliteCommand(checkColumn, _connection))
                {
                    var columnExists = Convert.ToInt32(command.ExecuteScalar()) > 0;
                    
                    if (!columnExists)
                    {
                        using var transaction = _connection.BeginTransaction();
                        try
                        {
                            var migration = @"
                                ALTER TABLE LogEntries RENAME TO LogEntries_Old;
                                CREATE TABLE LogEntries (
                                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                    SessionId INTEGER NOT NULL,
                                    TimeCodeIn TEXT NOT NULL,
                                    TimeCodeOut TEXT,
                                    Duration TEXT,
                                    ClipName TEXT,
                                    Notes TEXT,
                                    MarkTimecode TEXT,
                                    CreatedAt DATETIME DEFAULT CURRENT_TIMESTAMP,
                                    FOREIGN KEY (SessionId) REFERENCES Sessions(Id)
                                );
                                INSERT INTO LogEntries (Id, SessionId, TimeCodeIn, TimeCodeOut, Duration, ClipName, Notes, MarkTimecode, CreatedAt)
                                SELECT Id, SessionId, TimeCodeIn, TimeCodeOut, Duration, SongTitle, Notes, MarkTimecode, CreatedAt FROM LogEntries_Old;
                                DROP TABLE LogEntries_Old;
                            ";
                            using (var alterCommand = new SqliteCommand(migration, _connection, transaction))
                            {
                                alterCommand.ExecuteNonQuery();
                            }
                            transaction.Commit();
                            Console.WriteLine("[DB] Migration: SongTitle -> ClipName completed");
                        }
                        catch
                        {
                            transaction.Rollback();
                            Console.WriteLine("[DB] Migration: SongTitle -> ClipName rolled back");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DB] ClipName migration error: {ex.Message}");
            }
        }

        public async Task<int> CreateSession(string sessionName, string date, string location)
        {
            if (_connection == null) throw new InvalidOperationException("Database not initialized");

            var sql = @"
                INSERT INTO Sessions (SessionName, Date, Location, IsActive)
                VALUES (@SessionName, @Date, @Location, 1);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@SessionName", sessionName);
            command.Parameters.AddWithValue("@Date", date);
            command.Parameters.AddWithValue("@Location", location);

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

        public async Task<Session?> GetActiveSession()
        {
            if (_connection == null) return null;

            var sql = "SELECT * FROM Sessions WHERE IsActive = 1 ORDER BY CreatedAt DESC LIMIT 1";

            using var command = new SqliteCommand(sql, _connection);
            using var reader = await command.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return new Session
                {
                    Id = reader.GetInt32(0),
                    SessionName = reader.GetString(1),
                    Date = reader.GetString(2),
                    Location = reader.GetString(3),
                    CreatedAt = reader.GetDateTime(4),
                    ClosedAt = reader.IsDBNull(5) ? null : reader.GetDateTime(5),
                    IsActive = reader.GetInt32(6) == 1
                };
            }

            return null;
        }

        public async Task CloseSession(int sessionId)
        {
            if (_connection == null) return;

            var sql = "UPDATE Sessions SET IsActive = 0, ClosedAt = @ClosedAt WHERE Id = @SessionId";

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@ClosedAt", DateTime.Now);
            command.Parameters.AddWithValue("@SessionId", sessionId);

            await command.ExecuteNonQueryAsync();
        }

        public async Task<int> AddEntry(TimecodeLogEntry entry, int sessionId)
        {
            if (_connection == null) throw new InvalidOperationException("Database not initialized");

            var sql = @"
                INSERT INTO LogEntries (SessionId, TimeCodeIn, TimeCodeOut, Duration, ClipName, Notes, MarkTimecode)
                VALUES (@SessionId, @TimeCodeIn, @TimeCodeOut, @Duration, @ClipName, @Notes, @MarkTimecode);
                SELECT last_insert_rowid();";

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@SessionId", sessionId);
            command.Parameters.AddWithValue("@TimeCodeIn", entry.TimeCodeIn);
            command.Parameters.AddWithValue("@TimeCodeOut", entry.TimeCodeOut ?? "");
            command.Parameters.AddWithValue("@Duration", entry.Duration ?? "");
            command.Parameters.AddWithValue("@ClipName", entry.ClipName ?? "");
            command.Parameters.AddWithValue("@Notes", entry.Notes ?? "");
            command.Parameters.AddWithValue("@MarkTimecode", entry.MarkTimecode ?? "");

            var result = await command.ExecuteScalarAsync();
            return Convert.ToInt32(result);
        }

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

        public async Task<List<TimecodeLogEntry>> GetSessionEntries(int sessionId)
        {
            if (_connection == null) return new List<TimecodeLogEntry>();

            var entries = new List<TimecodeLogEntry>();
            var sql = "SELECT * FROM LogEntries WHERE SessionId = @SessionId ORDER BY CreatedAt ASC";

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
                    CreatedAt = reader.GetDateTime(8)
                });
            }

            return entries;
        }

        public async Task UpdateSessionInfo(Session session)
        {
            if (_connection == null) return;

            var sql = @"
                UPDATE Sessions 
                SET SessionName = @SessionName, 
                    Date = @Date, 
                    Location = @Location
                WHERE Id = @Id";

            using var command = new SqliteCommand(sql, _connection);
            command.Parameters.AddWithValue("@SessionName", session.SessionName);
            command.Parameters.AddWithValue("@Date", session.Date);
            command.Parameters.AddWithValue("@Location", session.Location);
            command.Parameters.AddWithValue("@Id", session.Id);

            await command.ExecuteNonQueryAsync();
        }

        public void Dispose()
        {
            if (_disposed) return;

            _connection?.Close();
            _connection?.Dispose();
            _connection = null;

            _disposed = true;
        }
    }
}
