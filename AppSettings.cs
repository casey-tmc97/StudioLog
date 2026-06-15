using System;
using System.IO;
using System.Text.Json;

namespace StudioLog.Models
{
    public class AppSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StudioLog",
            "settings.json"
        );

        public string SelectedFrameRate { get; set; } = "30 fps";
        public string SelectedAudioOutput { get; set; } = "System Default";
        public string? SelectedAsioDriver { get; set; }
        public string SelectedAudioInput { get; set; } = "None";
        public string? SelectedAsioInputDriver { get; set; }
        public string? SelectedNDISource { get; set; }
        public string SelectedClockSource { get; set; } = "System Clock";
        public string SelectedTimezoneId { get; set; } = "UTC";
        public string LastLaunchedVersion { get; set; } = string.Empty;
        
        public bool IsInputActive => SelectedAudioInput != "None";
        
        public string DatabasePath { get; set; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "StudioLog",
            "timecode.db"
        );

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading settings: {ex.Message}");
            }

            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                string directory = Path.GetDirectoryName(SettingsPath) ?? string.Empty;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving settings: {ex.Message}");
            }
        }
    }
}
