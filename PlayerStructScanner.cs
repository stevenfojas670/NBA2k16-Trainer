using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Finds every copy of the active MyPlayer struct in the live process.
    ///
    /// The game maintains multiple parallel copies of MyPlayer — the live
    /// in-game struct (whose ratings get rebuilt every ~6 sec from a source),
    /// the per-team roster mirror, and one or more "save template" copies the
    /// save serializer reads from. Trainer writes that only target the active
    /// copy get overwritten by the rebuild within seconds (in-memory) and
    /// reverted entirely on reload (save reads from the source copy, not
    /// the live one).
    ///
    /// Strategy: take the active player's last+first name as a search key
    /// (unique enough — a name like "Mike Jones" only matches the same
    /// MyPlayer's copies, not other league players who happen to share
    /// either name), then scan committed private RW heap regions for that
    /// pattern. Every match is reported as a copy. Trainer writes are then
    /// fanned out to all copies so save-reads pick up the edit no matter
    /// which copy the save path chooses.
    /// </summary>
    internal static class PlayerStructScanner
    {
        // Upper bound on how many copies we report. The game maintains 2-3 in
        // practice; anything beyond that is almost certainly a duplicate
        // string in some unrelated buffer (UI text, log line, etc.).
        private const int MaxCopies = 16;

        // Upper bound on bytes scanned per call. Heap regions for NBA 2K16
        // can total several GB; 500 MB is plenty to cover all MyPlayer
        // copies and keeps the scan responsive (~3-5 sec on a warm cache).
        private const long MaxScanBytes = 500L * 1024 * 1024;

        // Chunk size for reading each region. Matches PlayerResolver's
        // ScanRange so we share the boundary-spanning approach.
        private const int ChunkSize = 0x100000; // 1 MB

        // MEM_PRIVATE flag in MEMORY_BASIC_INFORMATION.Type. Filters out
        // MEM_MAPPED (file-backed) and MEM_IMAGE (module-backed) regions —
        // MyPlayer copies are heap-allocated, so MEM_PRIVATE is the right
        // bucket.
        private const uint MEM_PRIVATE = 0x20000;

        /// <summary>
        /// Returns the list of player-struct copies in memory, including the
        /// reference. The reference is always element 0 so callers that
        /// only care about "the active one" can keep using <c>copies[0]</c>.
        ///
        /// If the player name is too short or scanning fails, returns just
        /// the reference.
        /// </summary>
        public static IReadOnlyList<IntPtr> FindCopies(ProcessSession session, IntPtr reference)
        {
            if (reference == IntPtr.Zero) return Array.Empty<IntPtr>();

            byte[] lastName, firstName;
            try
            {
                lastName = session.ReadBytes(
                    new IntPtr(reference.ToInt64() + GameOffsets.PLAYER_LAST_NAME),
                    GameOffsets.PLAYER_LAST_NAME_BYTES);
                firstName = session.ReadBytes(
                    new IntPtr(reference.ToInt64() + GameOffsets.PLAYER_FIRST_NAME),
                    GameOffsets.PLAYER_FIRST_NAME_BYTES);
            }
            catch
            {
                return new[] { reference };
            }

            byte[]? lastPattern = TrimToWchar(lastName);
            byte[]? firstPattern = TrimToWchar(firstName);
            // A two-character last name has a 4-byte UTF-16 prefix and will
            // false-positive too often (e.g. "Li", "Wu"). Require ≥3 wchars
            // on the last name (= 6 bytes) before scanning.
            if (lastPattern is null || lastPattern.Length < 6 ||
                firstPattern is null || firstPattern.Length < 4)
            {
                return new[] { reference };
            }

            var matches = new List<IntPtr> { reference };
            var seen = new HashSet<long> { reference.ToInt64() };

            long scannedBytes = 0;
            IntPtr cur = IntPtr.Zero;
            int infoSize = Marshal.SizeOf<MemoryIO.MEMORY_BASIC_INFORMATION>();

            while (scannedBytes < MaxScanBytes && matches.Count < MaxCopies)
            {
                int sz = MemoryIO.VirtualQueryEx(session.Handle, cur,
                    out MemoryIO.MEMORY_BASIC_INFORMATION info, (uint)infoSize);
                if (sz == 0) break;

                long regionStart = info.BaseAddress.ToInt64();
                long regionSize = (long)info.RegionSize.ToUInt64();
                long regionEnd = regionStart + regionSize;

                bool isCandidate =
                    info.State == MemoryIO.MEM_COMMIT_STATE &&
                    (info.Protect == MemoryIO.PAGE_READWRITE ||
                     info.Protect == MemoryIO.PAGE_EXECUTE_READWRITE) &&
                    (info.Type & MEM_PRIVATE) != 0;

                if (isCandidate && regionSize > 0)
                {
                    long toScan = Math.Min(regionSize, MaxScanBytes - scannedBytes);
                    ScanRegion(session, info.BaseAddress, toScan,
                        lastPattern, firstPattern, reference, matches, seen);
                    scannedBytes += toScan;
                }

                if (regionEnd <= cur.ToInt64()) break; // defensive: forward progress
                cur = new IntPtr(regionEnd);
            }

            return matches;
        }

        private static void ScanRegion(
            ProcessSession session,
            IntPtr regionStart,
            long regionSize,
            byte[] lastPattern,
            byte[] firstPattern,
            IntPtr reference,
            List<IntPtr> matches,
            HashSet<long> seen)
        {
            int patLen = lastPattern.Length;
            byte[] tail = Array.Empty<byte>();
            long tailAddr = 0;

            for (long pos = 0; pos < regionSize; pos += ChunkSize)
            {
                if (matches.Count >= MaxCopies) return;

                int len = (int)Math.Min(ChunkSize, regionSize - pos);
                IntPtr chunkAddr = new IntPtr(regionStart.ToInt64() + pos);

                byte[] buf;
                try
                {
                    buf = session.ReadBytes(chunkAddr, len);
                }
                catch
                {
                    // Region partially inaccessible — skip but keep going.
                    tail = Array.Empty<byte>();
                    continue;
                }

                // Splice the tail from the previous chunk so a pattern that
                // straddles the boundary still gets caught.
                byte[] search;
                long searchAddr;
                if (tail.Length > 0 && tailAddr + tail.Length == chunkAddr.ToInt64())
                {
                    search = new byte[tail.Length + buf.Length];
                    Buffer.BlockCopy(tail, 0, search, 0, tail.Length);
                    Buffer.BlockCopy(buf, 0, search, tail.Length, buf.Length);
                    searchAddr = tailAddr;
                }
                else
                {
                    search = buf;
                    searchAddr = chunkAddr.ToInt64();
                }

                int idx = 0;
                while (idx <= search.Length - patLen)
                {
                    int hit = IndexOf(search, idx, lastPattern);
                    if (hit < 0) break;

                    long candidate = searchAddr + hit;
                    if (!seen.Contains(candidate) &&
                        VerifyFirstName(session, new IntPtr(candidate), firstPattern))
                    {
                        matches.Add(new IntPtr(candidate));
                        seen.Add(candidate);
                        if (matches.Count >= MaxCopies) return;
                    }
                    idx = hit + 1;
                }

                int saveLen = Math.Min(buf.Length, patLen - 1);
                tail = new byte[saveLen];
                Buffer.BlockCopy(buf, buf.Length - saveLen, tail, 0, saveLen);
                tailAddr = chunkAddr.ToInt64() + buf.Length - saveLen;
            }
        }

        private static bool VerifyFirstName(ProcessSession session, IntPtr playerBase, byte[] firstPattern)
        {
            try
            {
                byte[] candidate = session.ReadBytes(
                    new IntPtr(playerBase.ToInt64() + GameOffsets.PLAYER_FIRST_NAME),
                    firstPattern.Length);
                for (int i = 0; i < firstPattern.Length; i++)
                    if (candidate[i] != firstPattern[i]) return false;
                return ValidatePlayerLikeStruct(session, playerBase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Cheap sanity check that the candidate at <paramref name="playerBase"/>
        /// looks like a player struct, not a stray copy of the name in some
        /// other buffer (UI log line, save scratch area, ad-hoc string). We
        /// don't validate the full struct — just confirm a few cheap
        /// invariants that a real MyPlayer struct always satisfies and a
        /// random "Mike Jones" string copy never will.
        /// </summary>
        private static bool ValidatePlayerLikeStruct(ProcessSession session, IntPtr playerBase)
        {
            try
            {
                // The ratings region at +0x3C4 (44 bytes covering all 41 rating
                // slots + 3 gap bytes) should not be entirely zero, entirely
                // 0xFF, or all the same byte. Real MyPlayer copies always have
                // a mix of values in 0-100ish range.
                byte[] ratings = session.ReadBytes(
                    new IntPtr(playerBase.ToInt64() + GameOffsets.PLAYER_ATTRIBUTE_PTR_OFFSET),
                    44);

                byte first = ratings[0];
                bool allSame = true;
                int nonZero = 0;
                int plausibleRange = 0; // bytes in [1..100] — typical rating range
                for (int i = 0; i < ratings.Length; i++)
                {
                    if (ratings[i] != first) allSame = false;
                    if (ratings[i] != 0) nonZero++;
                    if (ratings[i] >= 1 && ratings[i] <= 100) plausibleRange++;
                }

                if (allSame) return false;          // zero-filled / 0xFF / sentinel
                if (nonZero < 8) return false;      // too sparse to be ratings
                // At least half the bytes should look like ratings (1..100).
                return plausibleRange * 2 >= ratings.Length;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Returns the leading UTF-16 prefix of a fixed-size name buffer up
        /// to (but not including) the first null wchar, or <c>null</c> if
        /// the buffer is empty / all-null. Trailing padding is excluded so
        /// we don't match against zero-filled regions of memory.
        /// </summary>
        private static byte[]? TrimToWchar(byte[] buffer)
        {
            for (int i = 0; i + 1 < buffer.Length; i += 2)
            {
                if (buffer[i] == 0 && buffer[i + 1] == 0)
                {
                    if (i == 0) return null;
                    byte[] trimmed = new byte[i];
                    Buffer.BlockCopy(buffer, 0, trimmed, 0, i);
                    return trimmed;
                }
            }
            return buffer; // No null found — buffer is fully used
        }

        private static int IndexOf(byte[] hay, int start, byte[] needle)
        {
            int last = hay.Length - needle.Length;
            for (int i = start; i <= last; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }
    }
}
