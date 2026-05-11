using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NBA2k16_Trainer
{
    internal sealed class Settings
    {
        public int SchemaVersion { get; set; } = 3;

        // ── Existing global height clamp settings ──────────────────────────────
        public float MaxHeight { get; set; } = 300.0f;
        public float MinHeight { get; set; } = 100.0f;
        public bool DisablePositionClamp { get; set; } = false;
        public bool AutoApplyOnAttach { get; set; } = false;
        public bool AcceptedDisclaimer { get; set; } = false;

        // ── New per-player profile (null = "don't auto-apply, just observe") ──
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public int? PrimaryPosition { get; set; }
        public int? SecondaryPosition { get; set; }
        public float? Weight { get; set; }
        public byte? Jersey { get; set; }
        public float? PerPlayerHeight { get; set; }
        public float? Wingspan { get; set; }
        // Gameplay-side height/wingspan, written to the .rdata-pointed PHYS copy
        // that drives FUN_140c0a8e0's reach formula. Separate from the visual
        // pair so users can play tall without the dunk-overshoot side-effect.
        public float? PerPlayerGameplayHeight { get; set; }
        public float? GameplayWingspan { get; set; }
        public bool AutoApplyProfile { get; set; } = false;

        // Ratings: only entries the user has explicitly edited go in here.
        public Dictionary<string, byte>? RatingOverrides { get; set; }
        public bool AutoApplyRatings { get; set; } = false;

        // Badges: same shape as ratings — name → tier (0..3 for 2-bit, 0..1 for 1-bit).
        // Only entries the user has explicitly edited go in here.
        public Dictionary<string, byte>? BadgeOverrides { get; set; }
        public bool AutoApplyBadges { get; set; } = false;

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
