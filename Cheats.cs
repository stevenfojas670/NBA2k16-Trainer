using System;
using System.Collections.Generic;
using System.Linq;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Buckets cheats by purpose so the UI can render one tab per category.
    /// </summary>
    internal enum CheatCategory
    {
        MyPlayer,
        Roster,
        Trades,
        MatchState,
        Sliders,
        MyCareer,
        GameplayPatch,
        Cosmetic,
        CapRemoval,
        QoL,
    }

    /// <summary>
    /// Shape of the value a cheat exposes to the UI. Drives editor choice
    /// (checkbox vs. spinbox vs. dropdown).
    /// </summary>
    internal enum CheatValueType
    {
        Toggle,
        Float,
        Int,
        Byte,
        String,
        Enum,
    }

    /// <summary>
    /// Whose state a cheat mutates. <see cref="Global"/> is a process-wide
    /// patch (code or shared data); the player-scoped values are reserved for
    /// the multi-resolver work in Phase 3.
    /// </summary>
    internal enum CheatScope
    {
        Global,
        MyPlayer,
        RosterPlayer,
    }

    /// <summary>
    /// Base class for any patch the trainer applies. Cheats hold their own state
    /// (enabled flag, captured original bytes) so the form can revert in place.
    /// </summary>
    internal abstract class Cheat
    {
        public string Name { get; }
        public string Description { get; }
        public CheatCategory Category { get; }
        public CheatValueType ValueType { get; }
        public CheatScope Scope { get; }
        public bool Enabled { get; protected set; }

        protected Cheat(
            string name,
            string description,
            CheatCategory category = CheatCategory.MyPlayer,
            CheatValueType valueType = CheatValueType.Toggle,
            CheatScope scope = CheatScope.Global)
        {
            Name = name;
            Description = description;
            Category = category;
            ValueType = valueType;
            Scope = scope;
        }

        /// <summary>Write the patch to the live process.</summary>
        public abstract void Apply(ProcessSession session);

        /// <summary>Restore whatever was there before Apply was called.</summary>
        public abstract void Revert(ProcessSession session);

        /// <summary>For float-constant cheats this captures the live game value at attach time.</summary>
        public virtual void Probe(ProcessSession session) { }
    }

    /// <summary>
    /// Overwrites a 4-byte float constant in .rdata (e.g. the hard min/max height clamps).
    /// "Default" is the original game value used when the cheat is reverted.
    /// </summary>
    internal sealed class FloatConstantCheat : Cheat
    {
        public long Offset { get; }
        public float DefaultValue { get; }
        public float CurrentValue { get; private set; }
        public float DesiredValue { get; set; }
        public float? LiveValue { get; private set; }

        public FloatConstantCheat(
            string name,
            string description,
            long offset,
            float defaultValue,
            float desired,
            CheatCategory category = CheatCategory.MyPlayer,
            CheatScope scope = CheatScope.Global)
            : base(name, description, category, CheatValueType.Float, scope)
        {
            Offset = offset;
            DefaultValue = defaultValue;
            DesiredValue = desired;
            CurrentValue = defaultValue;
        }

        public override void Probe(ProcessSession session)
        {
            LiveValue = session.ReadFloat(session.ResolveOffset(Offset));
        }

        public override void Apply(ProcessSession session)
        {
            session.WriteFloat(session.ResolveOffset(Offset), DesiredValue);
            CurrentValue = DesiredValue;
            LiveValue = DesiredValue;
            Enabled = !FloatsEqual(DesiredValue, DefaultValue);
        }

        public override void Revert(ProcessSession session)
        {
            session.WriteFloat(session.ResolveOffset(Offset), DefaultValue);
            CurrentValue = DefaultValue;
            LiveValue = DefaultValue;
            Enabled = false;
        }

        private static bool FloatsEqual(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }

    /// <summary>
    /// Replaces a run of bytes at a code offset with a patch. The original bytes are
    /// captured the first time Apply runs so Revert can put them back in place.
    /// </summary>
    internal sealed class BytePatchCheat : Cheat
    {
        public IReadOnlyList<PatchSite> Sites { get; }

        public BytePatchCheat(string name, string description, params PatchSite[] sites)
            : this(name, description, CheatCategory.MyPlayer, CheatScope.Global, sites)
        {
        }

        public BytePatchCheat(
            string name,
            string description,
            CheatCategory category,
            CheatScope scope,
            params PatchSite[] sites)
            : base(name, description, category, CheatValueType.Toggle, scope)
        {
            if (sites.Length == 0)
                throw new ArgumentException("BytePatchCheat needs at least one site.", nameof(sites));
            Sites = sites;
        }

        public override void Apply(ProcessSession session)
        {
            foreach (var site in Sites)
            {
                IntPtr addr = session.ResolveOffset(site.Offset);
                if (site.OriginalBytes is null)
                {
                    byte[] live = session.ReadBytes(addr, site.PatchBytes.Length);
                    if (site.ExpectedOriginal is { } expected)
                    {
                        if (expected.SequenceEqual(live))
                        {
                            site.OriginalBytes = live;
                        }
                        else if (site.PatchBytes.SequenceEqual(live))
                        {
                            // Already patched (e.g. from a previous trainer session).
                            // Use the documented expected bytes as the revert target — never
                            // store the patched bytes as "original" or revert becomes a no-op.
                            site.OriginalBytes = expected;
                        }
                        else
                        {
                            throw new InvalidOperationException(
                                $"Unexpected bytes at +0x{site.Offset:X} for '{Name}'. "
                                + $"Expected {Format(expected)} or {Format(site.PatchBytes)} (already-patched), got {Format(live)}.");
                        }
                    }
                    else
                    {
                        site.OriginalBytes = live;
                    }
                }
                session.WriteBytes(addr, site.PatchBytes);
            }
            Enabled = true;
        }

        public override void Revert(ProcessSession session)
        {
            foreach (var site in Sites)
            {
                if (site.OriginalBytes is null)
                {
                    // Never applied this session — fall back to the documented expected bytes.
                    if (site.ExpectedOriginal is null) continue;
                    site.OriginalBytes = site.ExpectedOriginal;
                }
                session.WriteBytes(session.ResolveOffset(site.Offset), site.OriginalBytes);
            }
            Enabled = false;
        }

        /// <summary>Forget previously captured original bytes (e.g. when the game process changes).</summary>
        public void ResetCapturedState()
        {
            foreach (var site in Sites) site.OriginalBytes = null;
            Enabled = false;
        }

        private static string Format(byte[] bytes) =>
            string.Join(' ', bytes.Select(b => b.ToString("X2")));
    }

    internal sealed class PatchSite
    {
        public long Offset { get; }
        public byte[] PatchBytes { get; }
        public byte[]? ExpectedOriginal { get; }
        public byte[]? OriginalBytes { get; set; }

        public PatchSite(long offset, byte[] patchBytes, byte[]? expectedOriginal = null)
        {
            Offset = offset;
            PatchBytes = patchBytes;
            ExpectedOriginal = expectedOriginal;
        }
    }

    /// <summary>
    /// Module-relative offsets and original bytes from the reverse-engineering brief.
    /// </summary>
    internal static class GameOffsets
    {
        // .rdata float constants
        public const long HARD_MAX_HEIGHT = 0x1FEA3F8;   // default 231.20f
        public const long HARD_MIN_HEIGHT = 0x1DC6A5C;   // default 137.00f
        public const float DEFAULT_MAX_HEIGHT = 231.20f;
        public const float DEFAULT_MIN_HEIGHT = 137.00f;

        // Per-position clamp gate: "cmp eax, 02" inside SetHeight / GetHeight.
        // Flipping the immediate to FF causes the GameMode==2 (Create Player) check to fail
        // for any plausible mode value, disabling the per-position clamp.
        public const long PATCH_SET_HEIGHT_MODE_CMP = 0xA3FB12;
        public const long PATCH_GET_HEIGHT_MODE_CMP = 0xA30F42;
        public static readonly byte[] CMP_ORIGINAL = { 0x83, 0xF8, 0x02 };
        public static readonly byte[] CMP_PATCHED  = { 0x83, 0xF8, 0xFF };

        // ─── Player resolver hook ────────────────────────────────────────────
        // The instruction `mov [rdx+0x84], ax` runs once per frame the game
        // updates the active MyPlayer. At that moment rdi == player struct ptr.
        // Pattern is the original 7 bytes plus the next instruction's 7 bytes
        // (mov r8d, [r8+0xC8]) to make the AOB unique.
        public static readonly byte[] HOOK_AOB_PATTERN = {
            0x66, 0x89, 0x82, 0x84, 0x00, 0x00, 0x00,
            0x41, 0x8B, 0x80, 0xC8, 0x00, 0x00, 0x00,
        };
        public static readonly byte[] HOOK_ORIGINAL_BYTES = {
            0x66, 0x89, 0x82, 0x84, 0x00, 0x00, 0x00,
        };
        // Expected location for the build at hand. Used as a hint only —
        // the real address comes from the AOB scan, so this can be stale
        // without breaking anything (it just costs a wider initial scan).
        public const long HOOK_SITE_HINT = 0x4F5A5F;

        // ─── Player struct field offsets ─────────────────────────────────────
        public const int PLAYER_LAST_NAME       = 0x00;   // UTF-16 fixed buffer
        public const int PLAYER_LAST_NAME_BYTES = 36;     // 18 wchars
        public const int PLAYER_FIRST_NAME      = 0x24;   // UTF-16 fixed buffer
        public const int PLAYER_FIRST_NAME_BYTES = 40;    // 20 wchars
        public const int PLAYER_WEIGHT          = 0x4C;   // f32, lbs
        public const int PLAYER_JERSEY          = 0x61;   // u8
        public const int PLAYER_PHYS_ATTRS_PTR  = 0x80;   // qword → sub-buffer
        public const int PLAYER_POSITION_DWORD  = 0xC8;   // u32; pos in bits 8..13
        public const int PLAYER_HANDEDNESS     = 0xCA;    // u8 bitfield
        // Live in-memory ratings table. Phase-1 documented this as 0x388 but
        // that block is a 41-byte 0xFF prefix (uninitialized template / save
        // buffer); the actual byte-per-attribute table the game continuously
        // rebuilds lives at +0x3C4. Verified 2026-05 via CE BP + memory scan
        // against Mike Jones's MyPlayer struct.
        public const int PLAYER_ATTRIBUTE_PTR_OFFSET = 0x3C4; // ratings table
        public const int PLAYER_BADGE_PTR_OFFSET     = 0x419; // badges bitfield

        // Sub-buffer (referenced by [player + 0x80])
        public const int PHYS_HEIGHT          = 0x00;     // f32, cm
        public const int PHYS_WINGSPAN        = 0x04;     // f32, cm
        public const int PHYS_BODY_LENGTH     = 0x08;     // f32
        public const int PHYS_SHOULDER_WIDTH  = 0x0C;     // f32

        // Position bit positions inside the dword at PLAYER_POSITION_DWORD.
        // `mov edi, [rbx+0xC8]; shr edi, 8; and edi, 7` extracts primary.
        public const int POS_PRIMARY_SHIFT   = 8;
        public const int POS_SECONDARY_SHIFT = 11;
        public const uint POS_MASK = 0x7;

        // ─── Static roster table (lives inside nba2k16.exe .data, PAGE_READWRITE) ─
        // Discovered 2026-05-22 via CE: ~450 NBA player records baked into the
        // module, 0x430 bytes per record, sharing the same field layout as the
        // heap-resident MyPlayer struct. Editing here changes the active in-memory
        // roster; the user persists with the game's own Options → Roster → Save.
        //
        // Anchor: Russell Westbrook's record. The actual array base + total count
        // are discovered dynamically by RosterResolver.Initialize() walking back
        // and forward from this anchor.
        public const long ROSTER_ANCHOR_OFFSET   = 0x70547C0;
        public const long ROSTER_RECORD_STRIDE   = 0x430;

        // Per-record team-metadata pointer. All players on the same team share
        // this qword value, which gives us team grouping without parsing names.
        public const int ROSTER_TEAM_PTR_OFFSET  = 0x50;
    }
}
