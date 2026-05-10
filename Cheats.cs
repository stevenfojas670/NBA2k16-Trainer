using System;
using System.Collections.Generic;
using System.Linq;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Base class for any patch the trainer applies. Cheats hold their own state
    /// (enabled flag, captured original bytes) so the form can revert in place.
    /// </summary>
    internal abstract class Cheat
    {
        public string Name { get; }
        public string Description { get; }
        public bool Enabled { get; protected set; }

        protected Cheat(string name, string description)
        {
            Name = name;
            Description = description;
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

        public FloatConstantCheat(string name, string description, long offset, float defaultValue, float desired)
            : base(name, description)
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
            : base(name, description)
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
    }
}
