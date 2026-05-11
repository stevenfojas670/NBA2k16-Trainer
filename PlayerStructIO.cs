using System;
using System.Text;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Typed accessors for the live MyPlayer struct, given its base pointer.
    /// All addresses are computed inside this class so the form / cheats never
    /// have to touch raw byte offsets.
    /// </summary>
    internal static class PlayerStructIO
    {
        // ─── Names (UTF-16 LE, fixed-size buffer) ──────────────────────────────

        public static string ReadName(ProcessSession s, IntPtr playerBase, int offset, int maxBytes)
        {
            byte[] buf = s.ReadBytes(new IntPtr(playerBase.ToInt64() + offset), maxBytes);
            // Stop at the first U+0000.
            int charCount = maxBytes / 2;
            int end = charCount;
            for (int i = 0; i < charCount; i++)
            {
                if (buf[i * 2] == 0 && buf[i * 2 + 1] == 0) { end = i; break; }
            }
            return Encoding.Unicode.GetString(buf, 0, end * 2);
        }

        /// <summary>
        /// Writes <paramref name="value"/> as UTF-16 LE into a fixed-size buffer,
        /// truncating if necessary. Pads the rest of the buffer with zeros so no
        /// stale bytes from the previous occupant remain visible.
        /// </summary>
        public static void WriteName(ProcessSession s, IntPtr playerBase, int offset, int maxBytes, string value)
        {
            byte[] encoded = Encoding.Unicode.GetBytes(value ?? string.Empty);
            // Reserve one wchar (2 bytes) for the terminating null.
            int copyLen = Math.Min(encoded.Length, maxBytes - 2);
            byte[] buf = new byte[maxBytes];
            if (copyLen > 0) Buffer.BlockCopy(encoded, 0, buf, 0, copyLen);
            // remaining bytes already zero
            s.WriteBytes(new IntPtr(playerBase.ToInt64() + offset), buf);
        }

        // ─── Scalars ───────────────────────────────────────────────────────────

        public static float ReadF32(ProcessSession s, IntPtr playerBase, int offset)
            => s.ReadFloat(new IntPtr(playerBase.ToInt64() + offset));

        public static void WriteF32(ProcessSession s, IntPtr playerBase, int offset, float v)
            => s.WriteFloat(new IntPtr(playerBase.ToInt64() + offset), v);

        public static byte ReadU8(ProcessSession s, IntPtr playerBase, int offset)
            => s.ReadByte(new IntPtr(playerBase.ToInt64() + offset));

        public static void WriteU8(ProcessSession s, IntPtr playerBase, int offset, byte v)
            => s.WriteByte(new IntPtr(playerBase.ToInt64() + offset), v);

        // ─── Position bit field (dword at +0xC8) ───────────────────────────────

        public static (int primary, int secondary) ReadPositions(ProcessSession s, IntPtr playerBase)
        {
            uint dword = s.ReadUInt32(new IntPtr(playerBase.ToInt64() + GameOffsets.PLAYER_POSITION_DWORD));
            int primary = (int)((dword >> GameOffsets.POS_PRIMARY_SHIFT) & GameOffsets.POS_MASK);
            int secondary = (int)((dword >> GameOffsets.POS_SECONDARY_SHIFT) & GameOffsets.POS_MASK);
            return (primary, secondary);
        }

        public static void WritePositions(ProcessSession s, IntPtr playerBase, int primary, int secondary)
        {
            IntPtr addr = new IntPtr(playerBase.ToInt64() + GameOffsets.PLAYER_POSITION_DWORD);
            uint dword = s.ReadUInt32(addr);

            uint primaryMask = GameOffsets.POS_MASK << GameOffsets.POS_PRIMARY_SHIFT;
            uint secondaryMask = GameOffsets.POS_MASK << GameOffsets.POS_SECONDARY_SHIFT;

            dword &= ~(primaryMask | secondaryMask);
            dword |= ((uint)primary & GameOffsets.POS_MASK) << GameOffsets.POS_PRIMARY_SHIFT;
            dword |= ((uint)secondary & GameOffsets.POS_MASK) << GameOffsets.POS_SECONDARY_SHIFT;

            s.WriteUInt32(addr, dword);
        }

        // ─── Pointer-indirected attrs (Height/Wingspan via *(player+0x80)) ────

        public static IntPtr ReadPhysAttrsPtr(ProcessSession s, IntPtr playerBase)
            => s.ReadPointer(new IntPtr(playerBase.ToInt64() + GameOffsets.PLAYER_PHYS_ATTRS_PTR));

        // x64 user-mode modules sit above this boundary; private heap allocations
        // sit far below it. The classification is robust enough for the per-copy
        // heap-vs-rdata branch in <see cref="PlayerProfileCheat.Write"/>.
        private const long ModulePointerThreshold = 0x7FF000000000L;

        /// <summary>
        /// Returns true when the qword at <c>playerBase + outerOffset</c> is a
        /// pointer that lands inside a loaded module (nba2k16.exe's .rdata in
        /// practice). Used to detect the player struct copy whose PHYS sub-buffer
        /// is a binary template — writes there leak into the live reach formula
        /// instead of the halftime-refreshed mesh.
        /// </summary>
        public static bool IsIndirectInModule(ProcessSession s, IntPtr playerBase, int outerOffset)
        {
            IntPtr sub = s.ReadPointer(new IntPtr(playerBase.ToInt64() + outerOffset));
            return sub != IntPtr.Zero && sub.ToInt64() >= ModulePointerThreshold;
        }

        public static float ReadIndirectF32(ProcessSession s, IntPtr playerBase, int outerOffset, int innerOffset)
        {
            IntPtr sub = s.ReadPointer(new IntPtr(playerBase.ToInt64() + outerOffset));
            if (sub == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Indirect pointer at +0x{outerOffset:X} is null — game may not be in a live match.");
            return s.ReadFloat(new IntPtr(sub.ToInt64() + innerOffset));
        }

        public static void WriteIndirectF32(ProcessSession s, IntPtr playerBase, int outerOffset, int innerOffset, float v)
        {
            IntPtr sub = s.ReadPointer(new IntPtr(playerBase.ToInt64() + outerOffset));
            if (sub == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Indirect pointer at +0x{outerOffset:X} is null — game may not be in a live match.");
            s.WriteFloat(new IntPtr(sub.ToInt64() + innerOffset), v);
        }

        // ─── Sub-pointer addresses for AttributePtr / BadgePtr ────────────────

        public static IntPtr AttributeBase(IntPtr playerBase) =>
            new IntPtr(playerBase.ToInt64() + GameOffsets.PLAYER_ATTRIBUTE_PTR_OFFSET);

        public static IntPtr BadgeBase(IntPtr playerBase) =>
            new IntPtr(playerBase.ToInt64() + GameOffsets.PLAYER_BADGE_PTR_OFFSET);
    }

    /// <summary>
    /// Position labels for UI. Index 0..4 match the in-game enum (PG..C).
    /// Index 5 ("None") covers the game's "no secondary position" encoding,
    /// which we round-trip as raw value 7 (max of the 3-bit field).
    /// </summary>
    internal static class PositionNames
    {
        public const int NoneIndex = 5;
        public const int NoneRawValue = 7;
        public static readonly string[] Display = { "PG", "SG", "SF", "PF", "C", "None" };

        /// <summary>Convert raw 0..7 game value to a UI ComboBox index.</summary>
        public static int RawToIndex(int raw) => raw >= 0 && raw <= 4 ? raw : NoneIndex;

        /// <summary>Convert UI ComboBox index back to raw 0..7 game value.</summary>
        public static int IndexToRaw(int idx) => idx == NoneIndex ? NoneRawValue : idx;

        public static string Format(int raw) =>
            raw >= 0 && raw <= 4 ? Display[raw] : "None";
    }
}
