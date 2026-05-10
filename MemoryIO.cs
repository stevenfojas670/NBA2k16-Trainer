using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace NBA2k16_Trainer
{
    internal static class MemoryIO
    {
        public const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        public const uint PAGE_EXECUTE_READWRITE = 0x40;
        public const uint PAGE_READWRITE = 0x04;
        public const uint MEM_COMMIT = 0x1000;
        public const uint MEM_RESERVE = 0x2000;
        public const uint MEM_RELEASE = 0x8000;

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(
            IntPtr handle, IntPtr addr, byte[] buffer, int size, out IntPtr read);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WriteProcessMemory(
            IntPtr handle, IntPtr addr, byte[] buffer, int size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualProtectEx(
            IntPtr handle, IntPtr addr, UIntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr VirtualAllocEx(
            IntPtr handle, IntPtr addr, UIntPtr size, uint allocType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool VirtualFreeEx(
            IntPtr handle, IntPtr addr, UIntPtr size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern int VirtualQueryEx(
            IntPtr handle, IntPtr addr, out MEMORY_BASIC_INFORMATION info, uint dwLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORY_BASIC_INFORMATION
        {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public uint __alignmentPad;   // x64 alignment to next pointer-sized field
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        public const uint MEM_COMMIT_STATE = 0x1000;
        public const uint MEM_FREE         = 0x10000;

        public static string DescribeError(int err) => err switch
        {
            0 => "Success.",
            5 => "Access denied (5). The trainer must run as administrator.",
            6 => "Invalid handle (6). The game likely exited mid-operation; re-attach.",
            87 => "Invalid parameter (87).",
            299 => "Partial copy (299). The address is unmapped or out of range — verify the offset.",
            487 => "Invalid address (487). The module base may have shifted; re-attach.",
            _ => $"Win32 error {err}: {new Win32Exception(err).Message}",
        };
    }

    /// <summary>
    /// Owns a process handle for the duration of one batch of memory operations.
    /// Open it just before reads/writes and dispose it immediately after — never hold
    /// across UI events or game restarts. The caller owns the Process object and is
    /// responsible for disposing it.
    /// </summary>
    internal sealed class ProcessSession : IDisposable
    {
        public IntPtr Handle { get; }
        public IntPtr BaseAddress { get; }
        public int Pid { get; }

        private bool _disposed;

        private ProcessSession(int pid, IntPtr handle, IntPtr baseAddr)
        {
            Pid = pid;
            Handle = handle;
            BaseAddress = baseAddr;
        }

        /// <summary>Open the running game process. Throws on failure with a friendly message.</summary>
        public static ProcessSession Open(Process proc)
        {
            if (proc.HasExited)
                throw new InvalidOperationException("The game process has exited.");

            IntPtr handle = MemoryIO.OpenProcess(MemoryIO.PROCESS_ALL_ACCESS, false, proc.Id);
            if (handle == IntPtr.Zero)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    "OpenProcess failed: " + MemoryIO.DescribeError(err));
            }

            IntPtr baseAddr;
            try
            {
                // MainModule throws Win32Exception if the trainer's bitness disagrees with the target.
                baseAddr = proc.MainModule!.BaseAddress;
            }
            catch (Exception ex)
            {
                MemoryIO.CloseHandle(handle);
                throw new InvalidOperationException(
                    "Could not resolve MainModule. Build the trainer as x64 (NBA 2K16 is 64-bit). Underlying: "
                    + ex.Message, ex);
            }

            return new ProcessSession(proc.Id, handle, baseAddr);
        }

        public IntPtr ResolveOffset(long offset) =>
            new(BaseAddress.ToInt64() + offset);

        public byte[] ReadBytes(IntPtr addr, int count)
        {
            byte[] buf = new byte[count];
            if (!MemoryIO.ReadProcessMemory(Handle, addr, buf, count, out IntPtr read)
                || read.ToInt64() != count)
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"ReadProcessMemory failed at 0x{addr.ToInt64():X}: " + MemoryIO.DescribeError(err));
            }
            return buf;
        }

        public float ReadFloat(IntPtr addr)
        {
            byte[] buf = ReadBytes(addr, 4);
            return BitConverter.ToSingle(buf, 0);
        }

        public byte ReadByte(IntPtr addr) => ReadBytes(addr, 1)[0];

        public uint ReadUInt32(IntPtr addr)
        {
            byte[] buf = ReadBytes(addr, 4);
            return BitConverter.ToUInt32(buf, 0);
        }

        public long ReadInt64(IntPtr addr)
        {
            byte[] buf = ReadBytes(addr, 8);
            return BitConverter.ToInt64(buf, 0);
        }

        public IntPtr ReadPointer(IntPtr addr) => new IntPtr(ReadInt64(addr));

        public void WriteBytes(IntPtr addr, byte[] bytes)
        {
            if (!MemoryIO.VirtualProtectEx(Handle, addr, (UIntPtr)bytes.Length,
                    MemoryIO.PAGE_EXECUTE_READWRITE, out uint oldProtect))
            {
                int err = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(
                    $"VirtualProtectEx (RWX) failed at 0x{addr.ToInt64():X}: " + MemoryIO.DescribeError(err));
            }

            try
            {
                if (!MemoryIO.WriteProcessMemory(Handle, addr, bytes, bytes.Length, out IntPtr written)
                    || written.ToInt64() != bytes.Length)
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"WriteProcessMemory failed at 0x{addr.ToInt64():X}: " + MemoryIO.DescribeError(err));
                }
            }
            finally
            {
                MemoryIO.VirtualProtectEx(Handle, addr, (UIntPtr)bytes.Length, oldProtect, out _);
            }
        }

        public void WriteFloat(IntPtr addr, float value) =>
            WriteBytes(addr, BitConverter.GetBytes(value));

        public void WriteByte(IntPtr addr, byte value) =>
            WriteBytes(addr, new[] { value });

        public void WriteUInt32(IntPtr addr, uint value) =>
            WriteBytes(addr, BitConverter.GetBytes(value));

        public void WriteInt64(IntPtr addr, long value) =>
            WriteBytes(addr, BitConverter.GetBytes(value));

        /// <summary>
        /// Allocate executable memory in the target process. If <paramref name="nearAddr"/>
        /// is non-zero we walk a ±2 GB window around it in 64 KB strides so the resulting
        /// cave is reachable with a 32-bit relative jump from that address. Returns
        /// <see cref="IntPtr.Zero"/> if no slot was found.
        /// </summary>
        public IntPtr AllocateNearby(IntPtr nearAddr, int size)
        {
            const long Window = 0x40000000;   // ±1 GB on each side; rel32 reaches ±2 GB
            const long Stride = 0x10000;      // VirtualAlloc granularity

            if (nearAddr == IntPtr.Zero)
            {
                return MemoryIO.VirtualAllocEx(Handle, IntPtr.Zero, (UIntPtr)size,
                    MemoryIO.MEM_COMMIT | MemoryIO.MEM_RESERVE,
                    MemoryIO.PAGE_EXECUTE_READWRITE);
            }

            long origin = nearAddr.ToInt64();
            // Try addresses below the hook first (caves below code keep the JMP forward-going,
            // which feels more conventional but isn't required), then above.
            for (long delta = Stride; delta <= Window; delta += Stride)
            {
                foreach (long candidate in new[] { origin - delta, origin + delta })
                {
                    if (candidate < 0x10000) continue;
                    IntPtr p = MemoryIO.VirtualAllocEx(Handle, new IntPtr(candidate),
                        (UIntPtr)size, MemoryIO.MEM_COMMIT | MemoryIO.MEM_RESERVE,
                        MemoryIO.PAGE_EXECUTE_READWRITE);
                    if (p != IntPtr.Zero) return p;
                }
            }
            return IntPtr.Zero;
        }

        public bool FreeMemory(IntPtr addr)
        {
            if (addr == IntPtr.Zero) return true;
            return MemoryIO.VirtualFreeEx(Handle, addr, UIntPtr.Zero, MemoryIO.MEM_RELEASE);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Handle != IntPtr.Zero)
                MemoryIO.CloseHandle(Handle);
        }
    }
}
