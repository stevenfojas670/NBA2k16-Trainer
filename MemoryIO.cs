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

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (Handle != IntPtr.Zero)
                MemoryIO.CloseHandle(Handle);
        }
    }
}
