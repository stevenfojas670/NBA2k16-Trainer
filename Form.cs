using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NBA2k16_Trainer
{
    public class Form1 : Form
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool WriteProcessMemory(IntPtr h, IntPtr addr, byte[] buf, int size, out int written);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool VirtualProtectEx(IntPtr h, IntPtr addr, UIntPtr size, uint newProt, out uint oldProt);

        const uint PROCESS_ALL_ACCESS = 0x1F0FFF;
        const uint PAGE_EXECUTE_READWRITE = 0x40;

        // Module-relative offsets discovered in CE
        const long OFFSET_MAX_HEIGHT = 0x1FEA3F8;   // float, default 231.20
        const long OFFSET_MIN_HEIGHT = 0x1DC6A5C;   // float, default 137.00

        NumericUpDown maxBox = null!, minBox = null!;
        Label statusLabel = null!;

        public Form1()
        {
            Text = "NBA 2K16 Height Trainer";
            Width = 360; Height = 220;
            StartPosition = FormStartPosition.CenterScreen;

            Controls.Add(new Label { Text = "Max height (cm):", Top = 20, Left = 20, Width = 130 });
            maxBox = new NumericUpDown
            {
                Top = 18,
                Left = 160,
                Width = 150,
                Minimum = 50,
                Maximum = 9999,
                DecimalPlaces = 2,
                Increment = 1,
                Value = 300
            };
            Controls.Add(maxBox);

            Controls.Add(new Label { Text = "Min height (cm):", Top = 55, Left = 20, Width = 130 });
            minBox = new NumericUpDown
            {
                Top = 53,
                Left = 160,
                Width = 150,
                Minimum = 1,
                Maximum = 9999,
                DecimalPlaces = 2,
                Increment = 1,
                Value = 100
            };
            Controls.Add(minBox);

            var apply = new Button { Text = "Apply to running game", Top = 95, Left = 20, Width = 290, Height = 30 };
            apply.Click += (s, e) => Apply();
            Controls.Add(apply);

            statusLabel = new Label { Top = 140, Left = 20, Width = 310, Text = "Idle." };
            Controls.Add(statusLabel);
        }

        void Apply()
        {
            var procs = Process.GetProcessesByName("nba2k16");
            if (procs.Length == 0) { statusLabel.Text = "nba2k16.exe is not running."; return; }
            var proc = procs[0];

            IntPtr handle = OpenProcess(PROCESS_ALL_ACCESS, false, proc.Id);
            if (handle == IntPtr.Zero)
            {
                statusLabel.Text = $"OpenProcess failed (err {Marshal.GetLastWin32Error()}). Run as admin.";
                return;
            }

            try
            {
                IntPtr baseAddr = proc.MainModule!.BaseAddress;
                WriteFloat(handle, baseAddr + (int)OFFSET_MAX_HEIGHT, (float)maxBox.Value);
                WriteFloat(handle, baseAddr + (int)OFFSET_MIN_HEIGHT, (float)minBox.Value);
                statusLabel.Text = $"Patched. Max={maxBox.Value}  Min={minBox.Value}";
            }
            catch (Exception ex)
            {
                statusLabel.Text = "Error: " + ex.Message;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        static void WriteFloat(IntPtr handle, IntPtr addr, float value)
        {
            VirtualProtectEx(handle, addr, (UIntPtr)4, PAGE_EXECUTE_READWRITE, out uint old);
            byte[] bytes = BitConverter.GetBytes(value);
            WriteProcessMemory(handle, addr, bytes, bytes.Length, out _);
            VirtualProtectEx(handle, addr, (UIntPtr)4, old, out _);
        }
    }
}