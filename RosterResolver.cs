using System;
using System.Collections.Generic;
using System.Text;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// One contiguous run of roster records that share the same team-metadata
    /// pointer (the qword at <c>record + 0x50</c>). The roster is laid out so
    /// teams group together, so a run = a team.
    /// </summary>
    internal sealed record TeamGroup(
        IntPtr MetadataPtr,
        int FirstRosterIndex,
        int PlayerCount,
        string DisplayName);

    /// <summary>
    /// Locates the static roster table inside nba2k16.exe.
    ///
    /// Unlike <see cref="PlayerResolver"/> this does NOT install a hook,
    /// trampoline, or code cave — the roster lives at a module-relative offset
    /// with a fixed 0x430-byte stride, so resolution is pure computation.
    ///
    /// Lifecycle:
    ///   <see cref="Initialize"/> ⇒ sanity-checks the Westbrook anchor, walks
    ///   back and forward to find the array bounds, groups records by their
    ///   team-metadata pointer at +0x50.
    ///
    ///   <see cref="GetPlayer"/> ⇒ computes the IntPtr of any record by index.
    ///
    ///   <see cref="Reset"/> ⇒ clears state (call on detach).
    /// </summary>
    internal sealed class RosterResolver
    {
        // Consecutive invalid records before we declare "off the end of array".
        // Some real records can be empty (e.g. unfilled bench slots), so a small
        // tolerance avoids cutting the walk short.
        private const int InvalidRecordTolerance = 16;

        // Hard cap on walk distance — guards against a misaligned anchor turning
        // this into a multi-MB scan. ~2000 × 0x430 ≈ 2 MB each direction.
        private const int MaxWalkRecords = 2000;

        public bool Initialized { get; private set; }
        public IntPtr ArrayBase { get; private set; }
        public int PlayerCount { get; private set; }
        public IReadOnlyList<TeamGroup> Teams => _teams;

        private readonly List<TeamGroup> _teams = new();

        public void Initialize(ProcessSession session)
        {
            Reset();

            IntPtr anchor = session.ResolveOffset(GameOffsets.ROSTER_ANCHOR_OFFSET);

            // Sanity-check: the anchor's last_name buffer must read "Westbrook".
            // If the user is running a different game build the offset will land
            // somewhere else and we want to fail loudly rather than corrupt data.
            string anchorName = TryReadName(session, anchor, GameOffsets.PLAYER_LAST_NAME, GameOffsets.PLAYER_LAST_NAME_BYTES);
            if (anchorName != "Westbrook")
                throw new InvalidOperationException(
                    $"Roster anchor sanity check failed — expected \"Westbrook\" at module + 0x{GameOffsets.ROSTER_ANCHOR_OFFSET:X}, "
                    + $"got \"{anchorName}\". The game build may differ from the one this trainer was tuned for. "
                    + "Re-locate Westbrook in Cheat Engine and update GameOffsets.ROSTER_ANCHOR_OFFSET.");

            long stride = GameOffsets.ROSTER_RECORD_STRIDE;
            long anchorAddr = anchor.ToInt64();

            // Walk back to find the array's first record.
            long startAddr = anchorAddr;
            int consecutiveInvalid = 0;
            for (int i = 1; i <= MaxWalkRecords; i++)
            {
                long candidate = anchorAddr - i * stride;
                if (IsValidRecord(session, new IntPtr(candidate)))
                {
                    startAddr = candidate;
                    consecutiveInvalid = 0;
                }
                else
                {
                    consecutiveInvalid++;
                    if (consecutiveInvalid >= InvalidRecordTolerance) break;
                }
            }

            // Walk forward to find the array's last record.
            long endAddr = anchorAddr;
            consecutiveInvalid = 0;
            for (int i = 1; i <= MaxWalkRecords; i++)
            {
                long candidate = anchorAddr + i * stride;
                if (IsValidRecord(session, new IntPtr(candidate)))
                {
                    endAddr = candidate;
                    consecutiveInvalid = 0;
                }
                else
                {
                    consecutiveInvalid++;
                    if (consecutiveInvalid >= InvalidRecordTolerance) break;
                }
            }

            ArrayBase = new IntPtr(startAddr);
            PlayerCount = (int)((endAddr - startAddr) / stride) + 1;

            BuildTeamGroups(session);

            Initialized = true;
        }

        /// <summary>Returns the IntPtr of the player record at <paramref name="rosterIndex"/>.</summary>
        public IntPtr GetPlayer(int rosterIndex)
        {
            if (!Initialized)
                throw new InvalidOperationException("RosterResolver not initialized.");
            if (rosterIndex < 0 || rosterIndex >= PlayerCount)
                throw new ArgumentOutOfRangeException(nameof(rosterIndex),
                    $"rosterIndex {rosterIndex} out of range [0, {PlayerCount}).");
            return new IntPtr(ArrayBase.ToInt64() + (long)rosterIndex * GameOffsets.ROSTER_RECORD_STRIDE);
        }

        /// <summary>Reads the player's "Last, First" label for UI display. Safe — returns "(empty)" on read failure.</summary>
        public string FormatPlayerLabel(ProcessSession session, int rosterIndex)
        {
            IntPtr p = GetPlayer(rosterIndex);
            string last = TryReadName(session, p, GameOffsets.PLAYER_LAST_NAME, GameOffsets.PLAYER_LAST_NAME_BYTES);
            string first = TryReadName(session, p, GameOffsets.PLAYER_FIRST_NAME, GameOffsets.PLAYER_FIRST_NAME_BYTES);
            if (string.IsNullOrEmpty(last) && string.IsNullOrEmpty(first))
                return "(empty)";
            return string.IsNullOrEmpty(first) ? last : $"{last}, {first}";
        }

        /// <summary>
        /// Reverse lookup: given a roster index, return the <see cref="TeamGroup"/>
        /// whose contiguous range covers it. Linear scan over ~30 teams is
        /// trivially cheap; called once per player to build the Players-tab label
        /// cache. Returns null if <paramref name="rosterIndex"/> is out of range.
        /// </summary>
        public TeamGroup? FindTeamForPlayer(int rosterIndex)
        {
            foreach (var team in _teams)
            {
                if (rosterIndex >= team.FirstRosterIndex
                    && rosterIndex < team.FirstRosterIndex + team.PlayerCount)
                    return team;
            }
            return null;
        }

        public void Reset()
        {
            Initialized = false;
            ArrayBase = IntPtr.Zero;
            PlayerCount = 0;
            _teams.Clear();
        }

        // ─── Internals ─────────────────────────────────────────────────────────

        private void BuildTeamGroups(ProcessSession session)
        {
            _teams.Clear();

            IntPtr? currentTeamPtr = null;
            int teamFirstIndex = 0;
            int teamPlayerCount = 0;
            int teamSeq = 0;

            void Flush()
            {
                if (currentTeamPtr is IntPtr ptr && teamPlayerCount > 0)
                {
                    teamSeq++;
                    string realName = ResolveTeamName(session, ptr);
                    string label = string.IsNullOrEmpty(realName)
                        ? $"Team #{teamSeq} ({teamPlayerCount})"
                        : $"{realName} ({teamPlayerCount})";
                    _teams.Add(new TeamGroup(
                        MetadataPtr: ptr,
                        FirstRosterIndex: teamFirstIndex,
                        PlayerCount: teamPlayerCount,
                        DisplayName: label));
                }
            }

            // BuildTeamGroups runs before Initialized is set, so resolve the
            // player base inline rather than through GetPlayer() (which guards
            // on Initialized). Same arithmetic — ArrayBase + i * stride.
            long arrayBase = ArrayBase.ToInt64();
            long stride = GameOffsets.ROSTER_RECORD_STRIDE;
            for (int i = 0; i < PlayerCount; i++)
            {
                IntPtr playerBase = new IntPtr(arrayBase + i * stride);
                IntPtr teamPtr;
                try
                {
                    teamPtr = session.ReadPointer(
                        new IntPtr(playerBase.ToInt64() + GameOffsets.ROSTER_TEAM_PTR_OFFSET));
                }
                catch
                {
                    // Unreadable page → start a new group on the next valid record.
                    Flush();
                    currentTeamPtr = null;
                    teamPlayerCount = 0;
                    continue;
                }

                if (currentTeamPtr is null || teamPtr != currentTeamPtr.Value)
                {
                    Flush();
                    currentTeamPtr = teamPtr;
                    teamFirstIndex = i;
                    teamPlayerCount = 1;
                }
                else
                {
                    teamPlayerCount++;
                }
            }
            Flush();
        }

        /// <summary>
        /// Scans 1 KB of the team-metadata struct for the display-ready team
        /// name. The struct's layout (verified 2026-05-22 against OKC):
        ///   +0x00..+0x77  15 player-pointer entries (120 bytes)
        ///   +0x78..+0x9F  padding
        ///   +0xA0+        lowercase internal identifiers (arena, logo set)
        ///   ASCII regions logo .iff filenames
        ///   later         Capitalised "Thunder" (nickname), "Oklahoma City"
        ///                 (city), "OKC" (abbr) in that order
        /// We collect every capitalised UTF-16 string in scan range; the natural
        /// order in the struct is [Nickname, City, Abbr], so the display is
        /// "City Nickname" (e.g. "Oklahoma City Thunder"). The lowercase and
        /// ASCII identifier strings are filtered automatically by the validator
        /// (rejects non-ASCII-UTF16 + non-capitalised-first-char).
        /// </summary>
        private static string ResolveTeamName(ProcessSession session, IntPtr metadataPtr)
        {
            const int ScanBytes = 1024;
            byte[] block;
            try
            {
                block = session.ReadBytes(metadataPtr, ScanBytes);
            }
            catch
            {
                return string.Empty;
            }

            var found = new List<string>();
            int i = 0;
            while (i + 4 <= block.Length)
            {
                string candidate = ExtractTeamNameAt(block, i);
                if (candidate.Length >= 3)
                {
                    found.Add(candidate);
                    // Skip past this string and its null terminator so we don't
                    // misread sub-strings inside it.
                    i += (candidate.Length + 1) * 2;
                }
                else
                {
                    i += 2;
                }
            }

            if (found.Count == 0) return string.Empty;

            // Natural order: [Nickname, City, Abbr]. Build "City Nickname"
            // when both available; otherwise fall back to the first string.
            string nickname = found[0];
            string city = found.Count >= 2 ? found[1] : string.Empty;

            if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(nickname))
                return $"{city} {nickname}";
            return nickname;
        }

        private static string ExtractTeamNameAt(byte[] block, int offset)
        {
            var sb = new StringBuilder(40);
            int i = offset;
            while (i + 2 <= block.Length)
            {
                byte lo = block[i];
                byte hi = block[i + 1];
                if (lo == 0 && hi == 0) break;          // null terminator — accept what we have
                if (hi != 0) return string.Empty;       // non-ASCII wchar — abort
                bool isLetter = (lo >= 'A' && lo <= 'Z') || (lo >= 'a' && lo <= 'z');
                bool isPunct = lo == ' ' || lo == '-' || lo == '.' || lo == '\'';
                if (!isLetter && !isPunct) return string.Empty;
                sb.Append((char)lo);
                if (sb.Length > 40) return string.Empty; // too long to be a team name
                i += 2;
            }

            string s = sb.ToString().Trim();
            if (s.Length < 3) return string.Empty;
            if (!char.IsUpper(s[0])) return string.Empty; // team names start capitalised
            return s;
        }

        /// <summary>
        /// Heuristic: a record is a real player slot if (a) its last_name's first
        /// wchar is a printable ASCII letter and (b) its team-metadata pointer is
        /// non-null. Catches misaligned pages and the run of zeros at array end.
        /// </summary>
        private static bool IsValidRecord(ProcessSession session, IntPtr playerBase)
        {
            try
            {
                byte[] head = session.ReadBytes(playerBase, 2);
                if (head.Length < 2) return false;
                // UTF-16 LE: high byte must be 0 for ASCII letters; low byte must be a printable letter.
                if (head[1] != 0) return false;
                byte ch = head[0];
                bool isLetter = (ch >= 0x41 && ch <= 0x5A) || (ch >= 0x61 && ch <= 0x7A);
                if (!isLetter) return false;

                IntPtr teamPtr = session.ReadPointer(
                    new IntPtr(playerBase.ToInt64() + GameOffsets.ROSTER_TEAM_PTR_OFFSET));
                return teamPtr != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private static string TryReadName(ProcessSession session, IntPtr playerBase, int offset, int maxBytes)
        {
            try
            {
                return PlayerStructIO.ReadName(session, playerBase, offset, maxBytes);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
