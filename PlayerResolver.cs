using System;
using System.Diagnostics;

namespace NBA2k16_Trainer
{
    /// <summary>
    /// Locates the live MyPlayer struct by installing an AOB-anchored code-cave
    /// trampoline. The trampoline copies <c>rdi</c> (the player struct pointer
    /// the game holds at the moment <c>mov [rdx+0x84], ax</c> runs) into a slot
    /// we allocated. The slot is then polled by the form.
    ///
    /// Lifecycle:
    ///   <see cref="Install"/> ⇒ reads pattern bytes, allocates near, writes
    ///   trampoline, replaces hook-site bytes with a JMP. After this, the slot
    ///   is filled the next time the game updates the player.
    ///
    ///   <see cref="ReadPlayerPointer"/> ⇒ returns the captured pointer (or
    ///   <see cref="IntPtr.Zero"/> if the hook hasn't fired yet).
    ///
    ///   <see cref="Revert"/> ⇒ restores the 7 bytes at the hook site and frees
    ///   the cave. Always safe to call (no-ops if not installed).
    /// </summary>
    internal sealed class PlayerResolver
    {
        // Cave layout (256 bytes, allocated PAGE_EXECUTE_READWRITE near hook site):
        //   +0x00..0x07   PlayerInfoSlot (qword written by trampoline)
        //   +0x08..0x3F   reserved
        //   +0x40..       trampoline code
        private const int SlotOffset = 0x00;
        private const int TrampolineOffset = 0x40;
        private const int CaveSize = 256;

        public IntPtr HookSite { get; private set; }
        public IntPtr CaveBase { get; private set; }
        public bool Installed { get; private set; }

        /// <summary>
        /// Scans for the AOB pattern, allocates a cave, installs the trampoline,
        /// and replaces the original 7 bytes with a JMP. Throws on failure.
        /// </summary>
        public void Install(ProcessSession session)
        {
            if (Installed)
                throw new InvalidOperationException("Resolver is already installed.");

            // Scan only inside nba2k16.exe (the hint is module-relative).
            IntPtr hint = session.ResolveOffset(GameOffsets.HOOK_SITE_HINT);
            HookSite = ScanForHook(session, hint);
            if (HookSite == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Hook AOB pattern not found in nba2k16.exe. Most common cause: "
                    + "Cheat Engine's MyCareer script (NBA2k16.ct id 9123) is currently "
                    + "enabled and has overwritten the same bytes — disable that script in "
                    + "CE first. Less common: the game build differs from the one the "
                    + "trainer was tuned for.");

            // Verify the 7 bytes at the hook site are pristine. If they're not, the
            // game has already been patched by another trainer / CE script.
            byte[] live = session.ReadBytes(HookSite, GameOffsets.HOOK_ORIGINAL_BYTES.Length);
            if (!BytesEqual(live, GameOffsets.HOOK_ORIGINAL_BYTES))
                throw new InvalidOperationException(
                    "Hook site already patched. Disable Cheat Engine's MyCareer script "
                    + "(NBA2k16.ct) before enabling profile features here.");

            CaveBase = session.AllocateNearby(HookSite, CaveSize);
            if (CaveBase == IntPtr.Zero)
                throw new InvalidOperationException(
                    "Could not allocate a code cave within ±2 GB of the hook site.");

            // Zero-initialize the cave so the slot starts at 0 (no stale value).
            session.WriteBytes(CaveBase, new byte[CaveSize]);

            byte[] trampoline = BuildTrampoline(
                slotAddr: CaveBase,
                returnAddr: new IntPtr(HookSite.ToInt64() + GameOffsets.HOOK_ORIGINAL_BYTES.Length));
            session.WriteBytes(new IntPtr(CaveBase.ToInt64() + TrampolineOffset), trampoline);

            byte[] hookPatch = BuildHookJmp(
                hookSite: HookSite,
                target:   new IntPtr(CaveBase.ToInt64() + TrampolineOffset));
            session.WriteBytes(HookSite, hookPatch);

            Installed = true;
        }

        /// <summary>
        /// Returns the captured player struct pointer, or <see cref="IntPtr.Zero"/>
        /// if the hook hasn't fired yet (e.g. the game is still in the main menu
        /// and hasn't ticked the active player). Safe to call repeatedly.
        /// </summary>
        public IntPtr ReadPlayerPointer(ProcessSession session)
        {
            if (!Installed) return IntPtr.Zero;
            return session.ReadPointer(new IntPtr(CaveBase.ToInt64() + SlotOffset));
        }

        /// <summary>
        /// Reverts the hook-site bytes and frees the cave. Idempotent.
        /// </summary>
        public void Revert(ProcessSession session)
        {
            if (!Installed) return;

            try
            {
                session.WriteBytes(HookSite, GameOffsets.HOOK_ORIGINAL_BYTES);
            }
            catch
            {
                // Process may already be dead; nothing useful to do.
            }

            try { session.FreeMemory(CaveBase); } catch { /* best-effort */ }

            Installed = false;
            CaveBase = IntPtr.Zero;
            HookSite = IntPtr.Zero;
        }

        // ─── AOB scan ──────────────────────────────────────────────────────────

        private static IntPtr ScanForHook(ProcessSession session, IntPtr hint)
        {
            // Tight scan around the hint first (±256 KB) — the CT confirms the
            // layout's stable across builds.
            const long Tight = 0x40000;
            IntPtr tight = ScanRange(session,
                new IntPtr(hint.ToInt64() - Tight),
                new IntPtr(hint.ToInt64() + Tight),
                GameOffsets.HOOK_AOB_PATTERN);
            if (tight != IntPtr.Zero) return tight;

            // Fallback: walk the whole nba2k16.exe text region by enumerating
            // committed executable pages with VirtualQueryEx.
            using var proc = Process.GetProcessById(session.Pid);
            long modBase, modEnd;
            try
            {
                var mod = proc.MainModule!;
                modBase = mod.BaseAddress.ToInt64();
                modEnd = modBase + mod.ModuleMemorySize;
            }
            catch
            {
                return IntPtr.Zero;
            }

            IntPtr cur = new IntPtr(modBase);
            while (cur.ToInt64() < modEnd)
            {
                int sz = MemoryIO.VirtualQueryEx(session.Handle, cur,
                    out MemoryIO.MEMORY_BASIC_INFORMATION info,
                    (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemoryIO.MEMORY_BASIC_INFORMATION>());
                if (sz == 0) break;

                bool committed = info.State == MemoryIO.MEM_COMMIT_STATE;
                bool exec = (info.Protect & 0xF0) != 0; // PAGE_EXECUTE_* bits
                long regionStart = info.BaseAddress.ToInt64();
                long regionLen = (long)info.RegionSize.ToUInt64();
                long regionEnd = regionStart + regionLen;

                if (committed && exec)
                {
                    IntPtr hit = ScanRange(session, info.BaseAddress, new IntPtr(regionEnd),
                        GameOffsets.HOOK_AOB_PATTERN);
                    if (hit != IntPtr.Zero) return hit;
                }

                cur = new IntPtr(regionEnd);
            }
            return IntPtr.Zero;
        }

        private static IntPtr ScanRange(ProcessSession session, IntPtr start, IntPtr end, byte[] pattern)
        {
            const int Chunk = 0x100000; // 1 MB
            long s = start.ToInt64();
            long e = end.ToInt64();
            int patLen = pattern.Length;

            // Read in overlapping chunks so the pattern can straddle a boundary.
            byte[] tail = Array.Empty<byte>();
            long tailAddr = 0;

            for (long pos = s; pos < e; pos += Chunk)
            {
                int len = (int)Math.Min(Chunk, e - pos);
                byte[] buf;
                try { buf = session.ReadBytes(new IntPtr(pos), len); }
                catch { tail = Array.Empty<byte>(); continue; }

                // Splice the previous tail (last patLen-1 bytes) onto the front so
                // we catch matches that span the chunk boundary.
                if (tail.Length > 0 && tailAddr + tail.Length == pos)
                {
                    byte[] joined = new byte[tail.Length + buf.Length];
                    Buffer.BlockCopy(tail, 0, joined, 0, tail.Length);
                    Buffer.BlockCopy(buf, 0, joined, tail.Length, buf.Length);
                    int idx = IndexOf(joined, pattern);
                    if (idx >= 0) return new IntPtr(tailAddr + idx);
                }
                else
                {
                    int idx = IndexOf(buf, pattern);
                    if (idx >= 0) return new IntPtr(pos + idx);
                }

                int saveLen = Math.Min(buf.Length, patLen - 1);
                tail = new byte[saveLen];
                Buffer.BlockCopy(buf, buf.Length - saveLen, tail, 0, saveLen);
                tailAddr = pos + buf.Length - saveLen;
            }
            return IntPtr.Zero;
        }

        private static int IndexOf(byte[] hay, byte[] needle)
        {
            int last = hay.Length - needle.Length;
            for (int i = 0; i <= last; i++)
            {
                int j = 0;
                while (j < needle.Length && hay[i + j] == needle[j]) j++;
                if (j == needle.Length) return i;
            }
            return -1;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        // ─── Trampoline assembly ───────────────────────────────────────────────

        /// <summary>
        /// Builds the 36-byte trampoline:
        ///   push rax
        ///   mov [rdx+0x84], ax            ; original instruction
        ///   mov rax, &slotAddr            ; absolute 64-bit immediate
        ///   mov [rax], rdi                ; capture player ptr
        ///   pop rax
        ///   jmp qword ptr [rip+0]         ; absolute 64-bit indirect jump
        ///   dq returnAddr
        /// </summary>
        private static byte[] BuildTrampoline(IntPtr slotAddr, IntPtr returnAddr)
        {
            var bytes = new byte[36];
            int o = 0;

            // push rax
            bytes[o++] = 0x50;

            // mov [rdx+0x84], ax  (original 7 bytes)
            bytes[o++] = 0x66; bytes[o++] = 0x89; bytes[o++] = 0x82;
            bytes[o++] = 0x84; bytes[o++] = 0x00; bytes[o++] = 0x00; bytes[o++] = 0x00;

            // mov rax, imm64
            bytes[o++] = 0x48; bytes[o++] = 0xB8;
            WriteInt64(bytes, ref o, slotAddr.ToInt64());

            // mov [rax], rdi
            bytes[o++] = 0x48; bytes[o++] = 0x89; bytes[o++] = 0x38;

            // pop rax
            bytes[o++] = 0x58;

            // jmp qword ptr [rip+0]
            bytes[o++] = 0xFF; bytes[o++] = 0x25;
            bytes[o++] = 0x00; bytes[o++] = 0x00; bytes[o++] = 0x00; bytes[o++] = 0x00;

            // dq return_addr
            WriteInt64(bytes, ref o, returnAddr.ToInt64());

            if (o != bytes.Length)
                throw new InvalidOperationException($"Trampoline length mismatch: {o} != {bytes.Length}");
            return bytes;
        }

        /// <summary>
        /// Builds the 7-byte hook-site replacement: a 5-byte rel32 JMP plus
        /// 2 NOPs to fully cover the original instruction.
        /// </summary>
        private static byte[] BuildHookJmp(IntPtr hookSite, IntPtr target)
        {
            long rel = target.ToInt64() - (hookSite.ToInt64() + 5);
            if (rel < int.MinValue || rel > int.MaxValue)
                throw new InvalidOperationException(
                    "Cave is more than ±2 GB from the hook site — JMP rel32 cannot reach.");

            return new byte[]
            {
                0xE9,
                (byte)(rel & 0xFF),
                (byte)((rel >>  8) & 0xFF),
                (byte)((rel >> 16) & 0xFF),
                (byte)((rel >> 24) & 0xFF),
                0x90, 0x90,
            };
        }

        private static void WriteInt64(byte[] dst, ref int offset, long value)
        {
            for (int i = 0; i < 8; i++)
                dst[offset++] = (byte)((value >> (i * 8)) & 0xFF);
        }
    }
}
