using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBA2k16_Trainer
{
    internal sealed class Settings
    {
        public float MaxHeight { get; set; } = 300.0f;
        public float MinHeight { get; set; } = 100.0f;
        public bool DisablePositionClamp { get; set; } = false;
        public bool AutoApplyOnAttach { get; set; } = false;
        public bool AcceptedDisclaimer { get; set; } = false;

        [JsonIgnore]
        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NBA2K16Trainer",
            "settings.json");

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public static Settings Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new Settings();
                string json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<Settings>(json, Options) ?? new Settings();
            }
            catch
            {
                // Corrupt or unreadable — fall back to defaults silently.
                return new Settings();
            }
        }

        public void Save()
        {
            try
            {
                string? dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
            }
            catch
            {
                // Best-effort; never let a settings write block the user.
            }
        }
    }
}
