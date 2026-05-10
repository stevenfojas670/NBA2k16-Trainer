using System;
using System.Collections.Generic;
using System.Linq;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Snapshot of all editable player profile fields at one point in time.
    /// Used both as the "captured original" for revert and as the "desired" payload for apply.
    /// </summary>
    internal sealed record PlayerProfileSnapshot(
        string FirstName,
        string LastName,
        int PrimaryPosition,
        int SecondaryPosition,
        float Weight,
        byte Jersey,
        float Height,
        float Wingspan);

    /// <summary>
    /// Reads/writes the player identity + body block (name, position, jersey,
    /// weight, height, wingspan). Captures the live values as "Original" the
    /// first time it's probed so Revert can put them back.
    /// </summary>
    internal sealed class PlayerProfileCheat
    {
        public PlayerProfileSnapshot? Original { get; private set; }
        public PlayerProfileSnapshot? Live { get; private set; }
        public bool Applied { get; private set; }

        public PlayerProfileSnapshot Read(ProcessSession s, IntPtr p)
        {
            var (primary, secondary) = PlayerStructIO.ReadPositions(s, p);
            return new PlayerProfileSnapshot(
                FirstName: PlayerStructIO.ReadName(s, p, GameOffsets.PLAYER_FIRST_NAME, GameOffsets.PLAYER_FIRST_NAME_BYTES),
                LastName:  PlayerStructIO.ReadName(s, p, GameOffsets.PLAYER_LAST_NAME, GameOffsets.PLAYER_LAST_NAME_BYTES),
                PrimaryPosition: primary,
                SecondaryPosition: secondary,
                Weight: PlayerStructIO.ReadF32(s, p, GameOffsets.PLAYER_WEIGHT),
                Jersey: PlayerStructIO.ReadU8(s, p, GameOffsets.PLAYER_JERSEY),
                Height: PlayerStructIO.ReadIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_HEIGHT),
                Wingspan: PlayerStructIO.ReadIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_WINGSPAN));
        }

        /// <summary>Reads current values into <see cref="Live"/>. Captures <see cref="Original"/> once.</summary>
        public PlayerProfileSnapshot Probe(ProcessSession s, IntPtr p)
        {
            Live = Read(s, p);
            Original ??= Live;
            return Live;
        }

        public void Apply(ProcessSession s, IntPtr p, PlayerProfileSnapshot desired)
        {
            // Make sure Original is captured before any write.
            if (Original is null) Probe(s, p);
            WriteAll(s, p, desired);
            Live = desired;
            Applied = true;
        }

        public void Revert(ProcessSession s, IntPtr p)
        {
            if (Original is null) return;
            WriteAll(s, p, Original);
            Live = Original;
            Applied = false;
        }

        public void ResetCapturedState()
        {
            Original = null;
            Live = null;
            Applied = false;
        }

        private static void WriteAll(ProcessSession s, IntPtr p, PlayerProfileSnapshot v)
        {
            PlayerStructIO.WriteName(s, p, GameOffsets.PLAYER_FIRST_NAME, GameOffsets.PLAYER_FIRST_NAME_BYTES, v.FirstName);
            PlayerStructIO.WriteName(s, p, GameOffsets.PLAYER_LAST_NAME, GameOffsets.PLAYER_LAST_NAME_BYTES, v.LastName);
            PlayerStructIO.WritePositions(s, p, v.PrimaryPosition, v.SecondaryPosition);
            PlayerStructIO.WriteF32(s, p, GameOffsets.PLAYER_WEIGHT, v.Weight);
            PlayerStructIO.WriteU8(s, p, GameOffsets.PLAYER_JERSEY, v.Jersey);
            PlayerStructIO.WriteIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_HEIGHT, v.Height);
            PlayerStructIO.WriteIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_WINGSPAN, v.Wingspan);
        }
    }

    /// <summary>One rating slot. Offset is relative to <c>player + 0x388</c>.</summary>
    internal sealed record RatingDef(string Name, int Offset, string Group);

    /// <summary>
    /// Reads/writes the 41 byte-sized ratings the CT table maps. Each value is
    /// a 25-99 (NBA scale) byte; writes outside that range are accepted but the
    /// game may clamp internally.
    /// </summary>
    internal sealed class RatingsCheat
    {
        // Offsets verified from NBA2k16.ct records 5192..5241 (AttributePtr-relative).
        public static readonly RatingDef[] Ratings = new[]
        {
            // ── Inside Scoring ────────────────────────────────────────────────
            new RatingDef("Standing Layup",         0x00, "Inside"),
            new RatingDef("Driving Layup",          0x01, "Inside"),
            new RatingDef("Post Fadeaway",          0x02, "Inside"),
            new RatingDef("Post Hook",              0x03, "Inside"),
            new RatingDef("Post Control",           0x04, "Inside"),
            new RatingDef("Draw Foul",              0x05, "Inside"),

            // ── Jump Shot ─────────────────────────────────────────────────────
            new RatingDef("Moving Shot Close",      0x06, "Shot"),
            new RatingDef("Standing Shot Close",    0x07, "Shot"),
            new RatingDef("Moving Shot Mid-Range",  0x08, "Shot"),
            new RatingDef("Standing Shot Mid-Range",0x09, "Shot"),
            new RatingDef("Moving Shot 3PT",        0x0A, "Shot"),
            new RatingDef("Standing Shot 3PT",      0x0B, "Shot"),
            new RatingDef("Free Throw",             0x0C, "Shot"),

            // ── Playmaker ─────────────────────────────────────────────────────
            new RatingDef("Ball Control",           0x0D, "Playmaker"),
            new RatingDef("Passing Vision",         0x0E, "Playmaker"),
            new RatingDef("Passing IQ",             0x0F, "Playmaker"),
            new RatingDef("Passing Accuracy",       0x10, "Playmaker"),

            // ── Rebounding ────────────────────────────────────────────────────
            new RatingDef("Boxout",                 0x11, "Rebound"),
            new RatingDef("Offensive Rebound",      0x12, "Rebound"),
            new RatingDef("Defensive Rebound",      0x13, "Rebound"),

            // ── Defense ───────────────────────────────────────────────────────
            new RatingDef("Lateral Quickness",      0x14, "Defense"),
            new RatingDef("Pass Perception",        0x15, "Defense"),
            new RatingDef("Block",                  0x16, "Defense"),
            new RatingDef("Shot Contest",           0x17, "Defense"),
            new RatingDef("Steal",                  0x18, "Defense"),
            new RatingDef("Defensive Consistency",  0x19, "Defense"),
            new RatingDef("On-Ball Defense IQ",     0x1A, "Defense"),
            new RatingDef("Pick & Roll Defense IQ", 0x1B, "Defense"),
            new RatingDef("Help Defense IQ",        0x1C, "Defense"),
            new RatingDef("Low Post Defense IQ",    0x1D, "Defense"),

            // ── Athletics ─────────────────────────────────────────────────────
            new RatingDef("Standing Dunk",          0x1E, "Athletics"),
            new RatingDef("Driving Dunk",           0x1F, "Athletics"),
            new RatingDef("Contact Dunk",           0x20, "Athletics"),
            new RatingDef("Speed",                  0x21, "Athletics"),
            new RatingDef("Acceleration",           0x22, "Athletics"),
            new RatingDef("Vertical",               0x23, "Athletics"),
            new RatingDef("Strength",               0x24, "Athletics"),
            new RatingDef("Stamina",                0x25, "Athletics"),
            new RatingDef("Hustle",                 0x26, "Athletics"),

            // ── Other ─────────────────────────────────────────────────────────
            new RatingDef("Shot IQ",                0x27, "Other"),
            new RatingDef("Hands",                  0x28, "Other"),
            new RatingDef("Offensive Consistency",  0x2A, "Other"),
            new RatingDef("Potential",              0x2B, "Other"),
        };

        public Dictionary<string, byte>? Original { get; private set; }
        public Dictionary<string, byte>? Live { get; private set; }
        public bool Applied { get; private set; }

        public Dictionary<string, byte> Read(ProcessSession s, IntPtr p)
        {
            IntPtr attrBase = PlayerStructIO.AttributeBase(p);
            var dict = new Dictionary<string, byte>(Ratings.Length);
            foreach (var r in Ratings)
                dict[r.Name] = s.ReadByte(new IntPtr(attrBase.ToInt64() + r.Offset));
            return dict;
        }

        public Dictionary<string, byte> Probe(ProcessSession s, IntPtr p)
        {
            Live = Read(s, p);
            Original ??= new Dictionary<string, byte>(Live);
            return Live;
        }

        public void Apply(ProcessSession s, IntPtr p, Dictionary<string, byte> desired)
        {
            if (Original is null) Probe(s, p);
            IntPtr attrBase = PlayerStructIO.AttributeBase(p);
            foreach (var r in Ratings)
            {
                if (!desired.TryGetValue(r.Name, out byte v)) continue;
                s.WriteByte(new IntPtr(attrBase.ToInt64() + r.Offset), v);
            }
            Live = new Dictionary<string, byte>(desired);
            Applied = true;
        }

        public void Revert(ProcessSession s, IntPtr p)
        {
            if (Original is null) return;
            IntPtr attrBase = PlayerStructIO.AttributeBase(p);
            foreach (var r in Ratings)
            {
                if (!Original.TryGetValue(r.Name, out byte v)) continue;
                s.WriteByte(new IntPtr(attrBase.ToInt64() + r.Offset), v);
            }
            Live = new Dictionary<string, byte>(Original);
            Applied = false;
        }

        public void ResetCapturedState()
        {
            Original = null;
            Live = null;
            Applied = false;
        }
    }

    /// <summary>
    /// Phase-2 scaffolding for badges. The CT records 5032..5120 model each
    /// badge as a single bit at <c>BadgePtr + (byteOffset, bitIndex)</c>. The
    /// table below is intentionally empty for now — the form has a placeholder
    /// tab; populate this list and wire UI when we tackle badges properly.
    /// </summary>
    internal sealed record BadgeDef(string Name, int ByteOffset, int BitIndex, string Group);

    internal static class BadgesScopeOut
    {
        public static readonly BadgeDef[] AllBadges = Array.Empty<BadgeDef>();

        public static bool ReadBadge(ProcessSession s, IntPtr playerBase, BadgeDef b)
        {
            byte by = PlayerStructIO.ReadU8(s,
                PlayerStructIO.BadgeBase(playerBase),
                b.ByteOffset);
            return ((by >> b.BitIndex) & 1) != 0;
        }

        public static void WriteBadge(ProcessSession s, IntPtr playerBase, BadgeDef b, bool on)
        {
            IntPtr addr = new IntPtr(PlayerStructIO.BadgeBase(playerBase).ToInt64() + b.ByteOffset);
            byte by = s.ReadByte(addr);
            byte mask = (byte)(1 << b.BitIndex);
            by = (byte)(on ? (by | mask) : (by & ~mask));
            s.WriteByte(addr, by);
        }

    }
}
