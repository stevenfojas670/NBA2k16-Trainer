using System;
using System.Collections.Generic;
using System.Linq;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Snapshot of all editable player profile fields at one point in time.
    /// Used both as the "captured original" for revert and as the "desired" payload for apply.
    ///
    /// Height/Wingspan are split into a visual pair (Height, Wingspan) and a
    /// gameplay pair (GameplayHeight, GameplayWingspan). The visual values feed
    /// the heap-resident player struct copies (drive the mesh re-instantiated at
    /// halftime / replay). The gameplay values feed the one copy whose +0x80
    /// PHYS sub-buffer pointer chains to nba2k16.exe's .rdata — that field is
    /// read every frame by FUN_140c0a8e0's reach formula (max_step ∝ height) and
    /// drives per-frame movement step distance during dunks. Editing them as a
    /// single value (the original behaviour) made visual edits leak into the
    /// reach calc, which is why tall edits produced "extremely fast" dunks.
    /// </summary>
    internal sealed record PlayerProfileSnapshot(
        string FirstName,
        string LastName,
        int PrimaryPosition,
        int SecondaryPosition,
        float Weight,
        byte Jersey,
        float Height,
        float Wingspan,
        float GameplayHeight,
        float GameplayWingspan);

    /// <summary>
    /// Reads/writes the player identity + body block (name, position, jersey,
    /// weight, height, wingspan). Lifecycle (probe/apply/revert/reset) lives in
    /// <see cref="PlayerCheatBase{TSnapshot}"/>.
    /// </summary>
    internal sealed class PlayerProfileCheat : PlayerCheatBase<PlayerProfileSnapshot>
    {
        public override PlayerProfileSnapshot Read(ProcessSession s, IntPtr p)
        {
            var (primary, secondary) = PlayerStructIO.ReadPositions(s, p);
            // On Read we can only observe whichever PHYS sub-buffer p chains to.
            // The resolver-found p is always heap-backed, so the two pairs are
            // initialised equal here; they only diverge once the user edits the
            // gameplay boxes and Apply fans out across the multi-copy set.
            float height   = PlayerStructIO.ReadIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_HEIGHT);
            float wingspan = PlayerStructIO.ReadIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_WINGSPAN);
            return new PlayerProfileSnapshot(
                FirstName: PlayerStructIO.ReadName(s, p, GameOffsets.PLAYER_FIRST_NAME, GameOffsets.PLAYER_FIRST_NAME_BYTES),
                LastName:  PlayerStructIO.ReadName(s, p, GameOffsets.PLAYER_LAST_NAME, GameOffsets.PLAYER_LAST_NAME_BYTES),
                PrimaryPosition: primary,
                SecondaryPosition: secondary,
                Weight: PlayerStructIO.ReadF32(s, p, GameOffsets.PLAYER_WEIGHT),
                Jersey: PlayerStructIO.ReadU8(s, p, GameOffsets.PLAYER_JERSEY),
                Height: height,
                Wingspan: wingspan,
                GameplayHeight: height,
                GameplayWingspan: wingspan);
        }

        protected override void Write(ProcessSession s, IntPtr p, PlayerProfileSnapshot v)
        {
            PlayerStructIO.WriteName(s, p, GameOffsets.PLAYER_FIRST_NAME, GameOffsets.PLAYER_FIRST_NAME_BYTES, v.FirstName);
            PlayerStructIO.WriteName(s, p, GameOffsets.PLAYER_LAST_NAME, GameOffsets.PLAYER_LAST_NAME_BYTES, v.LastName);
            PlayerStructIO.WritePositions(s, p, v.PrimaryPosition, v.SecondaryPosition);
            PlayerStructIO.WriteF32(s, p, GameOffsets.PLAYER_WEIGHT, v.Weight);
            PlayerStructIO.WriteU8(s, p, GameOffsets.PLAYER_JERSEY, v.Jersey);
            // Module-pointed copy → write the gameplay pair (drives reach formula);
            // heap copies → write the visual pair (drives mesh refresh at halftime).
            bool moduleSub = PlayerStructIO.IsIndirectInModule(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR);
            float heightOut   = moduleSub ? v.GameplayHeight   : v.Height;
            float wingspanOut = moduleSub ? v.GameplayWingspan : v.Wingspan;
            PlayerStructIO.WriteIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_HEIGHT, heightOut);
            PlayerStructIO.WriteIndirectF32(s, p, GameOffsets.PLAYER_PHYS_ATTRS_PTR, GameOffsets.PHYS_WINGSPAN, wingspanOut);
        }
    }

    /// <summary>One rating slot. Offset is relative to <c>player + 0x388</c>.</summary>
    internal sealed record RatingDef(string Name, int Offset, string Group);

    /// <summary>
    /// Reads/writes the 41 byte-sized ratings the CT table maps. Each value is
    /// a 25-99 (NBA scale) byte; writes outside that range are accepted but the
    /// game may clamp internally. Lifecycle lives in
    /// <see cref="PlayerCheatBase{TSnapshot}"/>.
    /// </summary>
    internal sealed class RatingsCheat : PlayerCheatBase<Dictionary<string, byte>>
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

        public override Dictionary<string, byte> Read(ProcessSession s, IntPtr p)
        {
            IntPtr attrBase = PlayerStructIO.AttributeBase(p);
            var dict = new Dictionary<string, byte>(Ratings.Length);
            foreach (var r in Ratings)
                dict[r.Name] = s.ReadByte(new IntPtr(attrBase.ToInt64() + r.Offset));
            return dict;
        }

        protected override void Write(ProcessSession s, IntPtr p, Dictionary<string, byte> values)
        {
            IntPtr attrBase = PlayerStructIO.AttributeBase(p);
            foreach (var r in Ratings)
            {
                if (!values.TryGetValue(r.Name, out byte v)) continue;
                s.WriteByte(new IntPtr(attrBase.ToInt64() + r.Offset), v);
            }
        }

        // Dict is mutable; hand back a defensive copy so callers can't poke our
        // captured Original/Live behind our back.
        protected override Dictionary<string, byte> Clone(Dictionary<string, byte> value) =>
            new Dictionary<string, byte>(value);
    }

    /// <summary>One tendency slot. Offset is relative to <c>player + 0x3C5</c>
    /// (the byte right after the 0xDE marker that starts the tendency block).</summary>
    internal sealed record TendencyDef(string Name, int Offset, string Group);

    /// <summary>
    /// Reads/writes the 84 tendency bytes that drive each NBA AI player's shot
    /// selection, drive choice, post moves, and defensive behaviour. Each value
    /// is a raw 0..100 byte; the in-game roster editor's "Tendencies" tabs read
    /// from and write to this same block. Verified 2026-05-23 via a bulk-edit
    /// position-encoding pass on Westbrook (see
    /// research/tendencies/westbrook-baseline.md and
    /// reference_nba2k16_tendency_offsets.md).
    ///
    /// Most names below are placeholders ("Jump Shooting #5") because only 11
    /// of the 84 sliders had their names captured in the discovery pass; the
    /// remaining 73 can be filled in incrementally by editing this array as
    /// each slider is identified in the editor. Memory offset and tab grouping
    /// are correct for all 84 — only the human-readable name is provisional.
    /// </summary>
    internal sealed class TendenciesCheat : PlayerCheatBase<Dictionary<string, byte>>
    {
        // Order is UI tab display order. Within each tab, items are in the
        // top-to-bottom order they appear in the in-game roster editor. The
        // Offset column reflects actual memory layout (which is NOT contiguous
        // by tab — Drive Setup is interleaved with Driving, Freelance items
        // are scattered, etc.).
        public static readonly TendencyDef[] Tendencies = new[]
        {
            // === Tab: Jump Shooting (23 items, mostly contiguous at 0x0D..0x23) ===
            new TendencyDef("Step Through Shot",       0x0D, "Jump Shooting"),
            new TendencyDef("Shot Under Basket",       0x0E, "Jump Shooting"),
            new TendencyDef("Shot Close",              0x0F, "Jump Shooting"),
            new TendencyDef("Shot Close Left",         0x10, "Jump Shooting"),
            new TendencyDef("Jump Shooting #5",        0x11, "Jump Shooting"),
            new TendencyDef("Jump Shooting #6",        0x12, "Jump Shooting"),
            new TendencyDef("Jump Shooting #7",        0x13, "Jump Shooting"),
            new TendencyDef("Jump Shooting #8",        0x14, "Jump Shooting"),
            new TendencyDef("Jump Shooting #9",        0x15, "Jump Shooting"),
            new TendencyDef("Jump Shooting #10",       0x16, "Jump Shooting"),
            new TendencyDef("Jump Shooting #11",       0x17, "Jump Shooting"),
            new TendencyDef("Jump Shooting #12",       0x18, "Jump Shooting"),
            new TendencyDef("Jump Shooting #13",       0x19, "Jump Shooting"),
            new TendencyDef("Jump Shooting #14",       0x1A, "Jump Shooting"),
            new TendencyDef("Jump Shooting #15",       0x1B, "Jump Shooting"),
            new TendencyDef("Jump Shooting #16",       0x1C, "Jump Shooting"),
            new TendencyDef("Jump Shooting #17",       0x1D, "Jump Shooting"),
            new TendencyDef("Jump Shooting #18",       0x1E, "Jump Shooting"),
            new TendencyDef("Jump Shooting #19",       0x1F, "Jump Shooting"),
            new TendencyDef("Jump Shooting #20",       0x20, "Jump Shooting"),
            new TendencyDef("Jump Shooting #21",       0x21, "Jump Shooting"),
            new TendencyDef("Jump Shooting #22",       0x22, "Jump Shooting"),
            new TendencyDef("Use Glass",               0x23, "Jump Shooting"),

            // === Tab: Layups and Dunks (12 items, contiguous at 0x01..0x0C) ===
            new TendencyDef("Standing Layup",          0x01, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #2",     0x02, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #3",     0x03, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #4",     0x04, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #5",     0x05, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #6",     0x06, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #7",     0x07, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #8",     0x08, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #9",     0x09, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #10",    0x0A, "Layups and Dunks"),
            new TendencyDef("Layups and Dunks #11",    0x0B, "Layups and Dunks"),
            new TendencyDef("Floater",                 0x0C, "Layups and Dunks"),

            // === Tab: Drive Setup (7 items, contiguous at 0x26..0x2C) ===
            new TendencyDef("Triple Threat Pump Fake", 0x26, "Drive Setup"),
            new TendencyDef("Drive Setup #2",          0x27, "Drive Setup"),
            new TendencyDef("Drive Setup #3",          0x28, "Drive Setup"),
            new TendencyDef("Drive Setup #4",          0x29, "Drive Setup"),
            new TendencyDef("Drive Setup #5",          0x2A, "Drive Setup"),
            new TendencyDef("Drive Setup #6",          0x2B, "Drive Setup"),
            new TendencyDef("No Setup Dribble",        0x2C, "Drive Setup"),

            // === Tab: Driving (12 items, split: 0x24, 0x25, then 0x2D..0x36) ===
            new TendencyDef("Driving #1",              0x24, "Driving"),
            new TendencyDef("Driving #2",              0x25, "Driving"),
            new TendencyDef("Driving #3",              0x2D, "Driving"),
            new TendencyDef("Driving #4",              0x2E, "Driving"),
            new TendencyDef("Driving #5",              0x2F, "Driving"),
            new TendencyDef("Driving #6",              0x30, "Driving"),
            new TendencyDef("Driving #7",              0x31, "Driving"),
            new TendencyDef("Driving #8",              0x32, "Driving"),
            new TendencyDef("Driving #9",              0x33, "Driving"),
            new TendencyDef("Driving #10",             0x34, "Driving"),
            new TendencyDef("Driving #11",             0x35, "Driving"),
            new TendencyDef("Driving #12",             0x36, "Driving"),

            // === Tab: Passing (3 items, scattered: 0x37, 0x4B, 0x4C) ===
            new TendencyDef("Passing #1",              0x37, "Passing"),
            new TendencyDef("Passing #2",              0x4B, "Passing"),
            new TendencyDef("Passing #3",              0x4C, "Passing"),

            // === Tab: Post Game (17 items, mostly contiguous, broken by Freelance #3 at 0x3A) ===
            new TendencyDef("Post Game #1",            0x39, "Post Game"),
            new TendencyDef("Post Game #2",            0x3B, "Post Game"),
            new TendencyDef("Post Game #3",            0x3C, "Post Game"),
            new TendencyDef("Post Game #4",            0x3D, "Post Game"),
            new TendencyDef("Post Game #5",            0x3E, "Post Game"),
            new TendencyDef("Post Game #6",            0x3F, "Post Game"),
            new TendencyDef("Post Game #7",            0x40, "Post Game"),
            new TendencyDef("Post Game #8",            0x41, "Post Game"),
            new TendencyDef("Post Game #9",            0x42, "Post Game"),
            new TendencyDef("Post Game #10",           0x43, "Post Game"),
            new TendencyDef("Post Game #11",           0x44, "Post Game"),
            new TendencyDef("Post Game #12",           0x45, "Post Game"),
            new TendencyDef("Post Game #13",           0x46, "Post Game"),
            new TendencyDef("Post Game #14",           0x47, "Post Game"),
            new TendencyDef("Post Game #15",           0x48, "Post Game"),
            new TendencyDef("Post Game #16",           0x49, "Post Game"),
            new TendencyDef("Post Game #17",           0x4A, "Post Game"),

            // === Tab: Freelance (3 items, scattered: 0x00, 0x38, 0x3A) ===
            new TendencyDef("Freelance #1",            0x00, "Freelance"),
            new TendencyDef("Freelance #2",            0x38, "Freelance"),
            new TendencyDef("Freelance #3",            0x3A, "Freelance"),

            // === Tab: Defense (7 items, contiguous at 0x4D..0x53) ===
            new TendencyDef("Pass Interception",       0x4D, "Defense"),
            new TendencyDef("Defense #2",              0x4E, "Defense"),
            new TendencyDef("Defense #3",              0x4F, "Defense"),
            new TendencyDef("Defense #4",              0x50, "Defense"),
            new TendencyDef("Defense #5",              0x51, "Defense"),
            new TendencyDef("Defense #6",              0x52, "Defense"),
            new TendencyDef("Hard Foul",               0x53, "Defense"),
        };

        public override Dictionary<string, byte> Read(ProcessSession s, IntPtr p)
        {
            IntPtr tendBase = PlayerStructIO.TendenciesBase(p);
            var dict = new Dictionary<string, byte>(Tendencies.Length);
            foreach (var t in Tendencies)
                dict[t.Name] = s.ReadByte(new IntPtr(tendBase.ToInt64() + t.Offset));
            return dict;
        }

        protected override void Write(ProcessSession s, IntPtr p, Dictionary<string, byte> values)
        {
            IntPtr tendBase = PlayerStructIO.TendenciesBase(p);
            foreach (var t in Tendencies)
            {
                if (!values.TryGetValue(t.Name, out byte v)) continue;
                s.WriteByte(new IntPtr(tendBase.ToInt64() + t.Offset), v);
            }
        }

        protected override Dictionary<string, byte> Clone(Dictionary<string, byte> value) =>
            new Dictionary<string, byte>(value);
    }

}
