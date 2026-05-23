using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Captures and persists per-player raw byte snapshots so any roster
    /// player can be reverted to their "original" state across trainer
    /// launches. "Original" is whatever the game held the first time the
    /// trainer attached with no existing baseline file — typically the
    /// launch-day defaults, or whatever the user's roster file was loading
    /// at that point.
    ///
    /// File layout: %LOCALAPPDATA%\NBA2K16Trainer\roster_baseline.bin
    ///
    ///   Header (24 bytes):
    ///     [+0..+3]   magic           = ASCII "RB16"
    ///     [+4..+7]   version         = uint32 (currently 2)
    ///     [+8..+15]  capturedAt      = int64 UTC ticks
    ///     [+16..+19] playerCount     = uint32
    ///     [+20..+23] physBufferSize  = uint32 (= 32 in v2)
    ///
    ///   Per-player entries (playerCount × (4 + 0x430 + physBufferSize) bytes):
    ///     [+0..+3]      roster_index  = int32
    ///     [+4..+0x433]  raw_bytes     = 0x430 bytes of the player record
    ///     [+0x434..]    phys_bytes    = physBufferSize bytes from *(player+0x80)
    ///                                   (height, wingspan, body length, etc.).
    ///                                   All-zeros if the phys pointer was null
    ///                                   at capture time.
    ///
    /// Total size for ~450 players: 24 + 450 × 1108 ≈ 487 KB.
    ///
    /// v1 files (no phys data) are rejected and the caller re-captures.
    /// </summary>
    internal sealed class RosterBaselineStore
    {
        private const int RecordSize = (int)GameOffsets.ROSTER_RECORD_STRIDE; // 0x430
        private const int PhysBufferSize = 32; // height, wingspan, body length, shoulder width + slack
        private const uint Version = 2;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("RB16");

        public bool IsLoaded { get; private set; }
        public DateTime CapturedAt { get; private set; }
        public int PlayerCount => _snapshots.Count;
        public string? LoadedFromPath { get; private set; }

        private readonly Dictionary<int, byte[]> _snapshots = new();
        private readonly Dictionary<int, byte[]> _physSnapshots = new();

        /// <summary>Default on-disk location, under %LOCALAPPDATA%.</summary>
        public static string DefaultPath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NBA2K16Trainer",
                "roster_baseline.bin");

        /// <summary>Returns the player-record bytes captured for <paramref name="rosterIndex"/>, or null if no snapshot exists.</summary>
        public byte[]? GetBaselineFor(int rosterIndex)
            => _snapshots.TryGetValue(rosterIndex, out var bytes) ? bytes : null;

        /// <summary>Returns the phys-attrs sub-buffer bytes for <paramref name="rosterIndex"/>, or null if not captured.</summary>
        public byte[]? GetPhysBaselineFor(int rosterIndex)
            => _physSnapshots.TryGetValue(rosterIndex, out var bytes) ? bytes : null;

        /// <summary>True if we have a snapshot for the given index that we can write back.</summary>
        public bool HasBaselineFor(int rosterIndex) => _snapshots.ContainsKey(rosterIndex);

        /// <summary>
        /// Loads a baseline file from <paramref name="path"/>. Returns false on
        /// any read error (missing file, magic mismatch, version mismatch,
        /// truncated entries) so the caller can decide to re-capture. v1 files
        /// are rejected — the caller will recapture in v2 format.
        /// </summary>
        public bool TryLoad(string path)
        {
            _snapshots.Clear();
            _physSnapshots.Clear();
            IsLoaded = false;
            LoadedFromPath = null;

            try
            {
                using var fs = File.OpenRead(path);
                using var br = new BinaryReader(fs);

                byte[] magic = br.ReadBytes(Magic.Length);
                if (magic.Length != Magic.Length) return false;
                for (int i = 0; i < Magic.Length; i++)
                    if (magic[i] != Magic[i]) return false;

                uint version = br.ReadUInt32();
                if (version != Version) return false;

                long ticks = br.ReadInt64();
                CapturedAt = new DateTime(ticks, DateTimeKind.Utc);

                uint count = br.ReadUInt32();
                if (count > 100_000) return false; // sanity bound

                uint physSize = br.ReadUInt32();
                if (physSize > 4096) return false; // sanity bound

                for (uint i = 0; i < count; i++)
                {
                    int rosterIndex = br.ReadInt32();

                    byte[] raw = br.ReadBytes(RecordSize);
                    if (raw.Length != RecordSize) return false;
                    _snapshots[rosterIndex] = raw;

                    if (physSize > 0)
                    {
                        byte[] phys = br.ReadBytes((int)physSize);
                        if (phys.Length != physSize) return false;
                        // All-zeros means "phys pointer was null at capture";
                        // skip storing so revert is a no-op for this player.
                        bool anyNonZero = false;
                        for (int j = 0; j < phys.Length; j++)
                            if (phys[j] != 0) { anyNonZero = true; break; }
                        if (anyNonZero) _physSnapshots[rosterIndex] = phys;
                    }
                }

                IsLoaded = true;
                LoadedFromPath = path;
                return true;
            }
            catch
            {
                _snapshots.Clear();
                _physSnapshots.Clear();
                IsLoaded = false;
                LoadedFromPath = null;
                return false;
            }
        }

        /// <summary>
        /// Reads all live records (player bytes + phys sub-buffer) via the
        /// resolver and stores them. Existing snapshot state is replaced.
        /// </summary>
        public void Capture(ProcessSession session, RosterResolver resolver)
        {
            _snapshots.Clear();
            _physSnapshots.Clear();
            int total = resolver.PlayerCount;
            for (int i = 0; i < total; i++)
            {
                IntPtr playerBase;
                try
                {
                    playerBase = resolver.GetPlayer(i);
                }
                catch
                {
                    continue;
                }

                try
                {
                    byte[] raw = session.ReadBytes(playerBase, RecordSize);
                    if (raw.Length == RecordSize)
                        _snapshots[i] = raw;
                }
                catch
                {
                    // Failed to read player record — skip phys too.
                    continue;
                }

                // Capture the phys-attrs sub-buffer (height/wingspan/body
                // length/shoulder width). The pointer at +0x80 may be null
                // for some records; just skip those — Revert will be a no-op
                // for phys but the rest of the record still reverts cleanly.
                try
                {
                    IntPtr physPtr = PlayerStructIO.ReadPhysAttrsPtr(session, playerBase);
                    if (physPtr != IntPtr.Zero)
                    {
                        byte[] phys = session.ReadBytes(physPtr, PhysBufferSize);
                        if (phys.Length == PhysBufferSize)
                            _physSnapshots[i] = phys;
                    }
                }
                catch
                {
                    // Phys read failed — leave that player without phys baseline.
                }
            }
            CapturedAt = DateTime.UtcNow;
            IsLoaded = true;
        }

        /// <summary>
        /// Writes the current snapshot set to <paramref name="path"/>. Creates
        /// the parent directory if needed; overwrites any existing file.
        /// </summary>
        public void Save(string path)
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var fs = File.Create(path);
            using var bw = new BinaryWriter(fs);

            bw.Write(Magic);
            bw.Write(Version);
            bw.Write(CapturedAt.ToUniversalTime().Ticks);
            bw.Write((uint)_snapshots.Count);
            bw.Write((uint)PhysBufferSize);

            // For each player, write the record bytes and the phys bytes.
            // If we don't have phys for that player (phys ptr was null or
            // read failed), write zeros — they'll be filtered back out on
            // load.
            byte[] physZeros = new byte[PhysBufferSize];
            foreach (var (rosterIndex, raw) in _snapshots)
            {
                bw.Write(rosterIndex);
                bw.Write(raw);
                byte[] phys = _physSnapshots.TryGetValue(rosterIndex, out var p) ? p : physZeros;
                bw.Write(phys);
            }

            LoadedFromPath = path;
        }

        /// <summary>Drop all in-memory snapshots; call on detach.</summary>
        public void Reset()
        {
            _snapshots.Clear();
            _physSnapshots.Clear();
            IsLoaded = false;
            CapturedAt = default;
            LoadedFromPath = null;
        }
    }
}
