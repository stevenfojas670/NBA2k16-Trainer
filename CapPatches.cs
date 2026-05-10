using System;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Phase-2 cap-removal suite. Each entry NOPs / flattens one of the four
    /// systems that keep edited ratings from sticking:
    ///
    ///   1. <see cref="HardRatingClamp"/> — the save-load function that snaps
    ///      every rating into [25, 99].
    ///   2. <see cref="PositionAttributeCaps"/> — the per-position max-attribute
    ///      lookup table in .data (Centers capped lower on Speed, etc.).
    ///   3. <see cref="HeightAttributeScaling"/> — the multiplier that scales
    ///      Speed / Quickness down as height increases.
    ///   4. <see cref="ArchetypeCaps"/> — MyCareer build-archetype per-attribute
    ///      ceilings (Slasher maxes 99 Driving Dunk but ~65 3PT, etc.). May
    ///      slip to Phase 7 if it lives in the MyCareer save struct rather
    ///      than the player struct.
    ///
    /// Offsets and patch bytes are filled in by the Ghidra static-analysis
    /// pass — see <see cref="GameOffsets"/> additions. Until populated, calling
    /// <c>Apply</c> on an uncalibrated cheat throws so we don't silently patch
    /// the wrong bytes.
    /// </summary>
    internal static class CapPatches
    {
        // ─── Hard 25-99 rating clamp ───────────────────────────────────────────
        // Module-relative offset of the instruction to NOP. Filled by Ghidra.
        public static long? HardClampOffset = null;
        public static byte[]? HardClampExpectedOriginal = null;
        public static byte[]? HardClampNopBytes = null;

        public static Cheat CreateHardRatingClamp()
        {
            if (HardClampOffset is null ||
                HardClampExpectedOriginal is null ||
                HardClampNopBytes is null)
            {
                return UncalibratedStub(
                    "Hard Rating Clamp (25-99)",
                    "NOP the save-load clamp so edited ratings survive a reload.");
            }
            return new BytePatchCheat(
                "Hard Rating Clamp (25-99)",
                "NOP the save-load clamp so edited ratings survive a reload.",
                CheatCategory.CapRemoval,
                CheatScope.Global,
                new PatchSite(
                    HardClampOffset.Value,
                    HardClampNopBytes,
                    HardClampExpectedOriginal));
        }

        // ─── Position-based attribute cap table ────────────────────────────────
        // Per-position max-attribute lookup in .data. Likely a 5 × 41 byte
        // table. Flatten = overwrite every entry with 99.
        public static long? PositionCapTableOffset = null;
        public static int PositionCapTableLength = 0;

        public static Cheat CreatePositionAttributeCaps()
        {
            if (PositionCapTableOffset is null || PositionCapTableLength == 0)
            {
                return UncalibratedStub(
                    "Position Attribute Caps",
                    "Flatten the per-position cap table so any position can hit 99 on any attribute.");
            }
            byte[] flattened = new byte[PositionCapTableLength];
            for (int i = 0; i < flattened.Length; i++) flattened[i] = 99;
            return new BytePatchCheat(
                "Position Attribute Caps",
                "Flatten the per-position cap table so any position can hit 99 on any attribute.",
                CheatCategory.CapRemoval,
                CheatScope.Global,
                new PatchSite(PositionCapTableOffset.Value, flattened));
        }

        // ─── Height-vs-attribute scaling ───────────────────────────────────────
        // The multiplier (likely fmul or imul) that scales Speed / Quickness
        // down for taller players. NOP target is the multiply instruction.
        public static long? HeightScalingOffset = null;
        public static byte[]? HeightScalingExpectedOriginal = null;
        public static byte[]? HeightScalingNopBytes = null;

        public static Cheat CreateHeightAttributeScaling()
        {
            if (HeightScalingOffset is null ||
                HeightScalingExpectedOriginal is null ||
                HeightScalingNopBytes is null)
            {
                return UncalibratedStub(
                    "Height Attribute Scaling",
                    "NOP the height-based speed / quickness multiplier so tall builds aren't slow.");
            }
            return new BytePatchCheat(
                "Height Attribute Scaling",
                "NOP the height-based speed / quickness multiplier so tall builds aren't slow.",
                CheatCategory.CapRemoval,
                CheatScope.Global,
                new PatchSite(
                    HeightScalingOffset.Value,
                    HeightScalingNopBytes,
                    HeightScalingExpectedOriginal));
        }

        // ─── MyCareer archetype caps ───────────────────────────────────────────
        // Per-archetype attribute ceiling lookup. May live in the MyCareer
        // save struct rather than the binary's .data — in that case this cheat
        // moves to Phase 7 (needs MyCareerResolver to land first).
        public static long? ArchetypeCapTableOffset = null;
        public static int ArchetypeCapTableLength = 0;

        public static Cheat CreateArchetypeCaps()
        {
            if (ArchetypeCapTableOffset is null || ArchetypeCapTableLength == 0)
            {
                return UncalibratedStub(
                    "Archetype Caps (MyCareer)",
                    "Flatten the per-archetype attribute ceilings so any build can hit 99 anywhere.");
            }
            byte[] flattened = new byte[ArchetypeCapTableLength];
            for (int i = 0; i < flattened.Length; i++) flattened[i] = 99;
            return new BytePatchCheat(
                "Archetype Caps (MyCareer)",
                "Flatten the per-archetype attribute ceilings so any build can hit 99 anywhere.",
                CheatCategory.CapRemoval,
                CheatScope.Global,
                new PatchSite(ArchetypeCapTableOffset.Value, flattened));
        }

        private static Cheat UncalibratedStub(string name, string description) =>
            new UncalibratedCapCheat(name, description);
    }

    /// <summary>
    /// Placeholder for a cap-removal cheat whose offset hasn't been resolved
    /// by Ghidra yet. Listed in the registry so the UI knows the feature is
    /// in-flight, but throws on Apply so we never write to the wrong bytes.
    /// </summary>
    internal sealed class UncalibratedCapCheat : Cheat
    {
        public UncalibratedCapCheat(string name, string description)
            : base(name, description, CheatCategory.CapRemoval, CheatValueType.Toggle, CheatScope.Global)
        {
        }

        public override void Apply(ProcessSession session) =>
            throw new InvalidOperationException(
                $"'{Name}' is not yet calibrated — Ghidra analysis still pending.");

        public override void Revert(ProcessSession session) { /* nothing to revert */ }
    }
}
