using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using StudioLog.Models;

namespace StudioLog.Core
{
    public class SessionFileData
    {
        public int SessionId { get; set; }
        public string SessionName { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<TimecodeLogEntry> Entries { get; set; } = new List<TimecodeLogEntry>();
    }

    public class SessionManager
    {
        private readonly string _sessionsFolderPath;
        private readonly TimecodeDatabase _database;

        public SessionManager(TimecodeDatabase database)
        {
            _database = database;
            
            // Get Documents folder path (cross-platform)
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _sessionsFolderPath = Path.Combine(documentsPath, "StudioLog", "Sessions");
            
            // Create sessions folder if it doesn't exist
            if (!Directory.Exists(_sessionsFolderPath))
            {
                Directory.CreateDirectory(_sessionsFolderPath);
                Console.WriteLine($"[SessionManager] Created sessions folder: {_sessionsFolderPath}");
            }
        }

        public string SessionsFolderPath => _sessionsFolderPath;

        public async Task<string> SaveSessionToFile(Session session, List<TimecodeLogEntry> entries)
        {
            try
            {
                // Create session data
                var sessionData = new SessionFileData
                {
                    SessionId = session.Id,
                    SessionName = session.SessionName,
                    Date = session.Date,
                    Location = session.Location,
                    CreatedAt = session.CreatedAt,
                    Entries = entries
                };

                // Create filename: SessionName_Date_Time.tcsession
                string safeSessionName = string.Join("_", session.SessionName.Split(Path.GetInvalidFileNameChars()));
                string safeDate = session.Date.Replace("/", "-").Replace("\\", "-");
                string timestamp = DateTime.Now.ToString("HHmmss");
                string filename = $"{safeSessionName}_{safeDate}_{timestamp}.tcsession";
                string filepath = Path.Combine(_sessionsFolderPath, filename);

                // Serialize to JSON
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                };
                string json = JsonSerializer.Serialize(sessionData, options);

                // Write to file
                await File.WriteAllTextAsync(filepath, json);

                Console.WriteLine($"[SessionManager] Session saved: {filepath}");
                return filepath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager] Error saving session: {ex.Message}");
                throw;
            }
        }

        public async Task<SessionFileData?> LoadSessionFromFile(string filepath)
        {
            try
            {
                if (!File.Exists(filepath))
                {
                    Console.WriteLine($"[SessionManager] File not found: {filepath}");
                    return null;
                }

                string json = await File.ReadAllTextAsync(filepath);
                var sessionData = JsonSerializer.Deserialize<SessionFileData>(json);

                Console.WriteLine($"[SessionManager] Session loaded: {filepath}");
                return sessionData;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager] Error loading session: {ex.Message}");
                throw;
            }
        }

        public List<string> GetSavedSessionFiles()
        {
            try
            {
                if (!Directory.Exists(_sessionsFolderPath))
                {
                    return new List<string>();
                }

                var files = Directory.GetFiles(_sessionsFolderPath, "*.tcsession")
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .ToList();

                Console.WriteLine($"[SessionManager] Found {files.Count} saved sessions");
                return files;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SessionManager] Error listing sessions: {ex.Message}");
                return new List<string>();
            }
        }

        public string GetSessionDisplayName(string filepath)
        {
            try
            {
                string filename = Path.GetFileNameWithoutExtension(filepath);
                return filename.Replace("_", " ");
            }
            catch
            {
                return filepath;
            }
        }
    }
}
