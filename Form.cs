using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NBA2k16_Trainer
{
    public class Form1 : Form
    {
        // Win32 hotkey API for F1 toggle.
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID_TOGGLE_CLAMP = 1;

        // UI
        private NumericUpDown _maxBox = null!;
        private NumericUpDown _minBox = null!;
        private Label _liveMaxLabel = null!;
        private Label _liveMinLabel = null!;
        private Label _statusLabel = null!;
        private Button _applyHeightBtn = null!;
        private Button _restoreHeightBtn = null!;
        private CheckBox _positionClampToggle = null!;
        private CheckBox _autoApplyToggle = null!;
        private TextBox _logBox = null!;
        private Button _clearLogBtn = null!;
        private System.Windows.Forms.Timer _attachTimer = null!;

        // State
        private readonly Settings _settings;
        private readonly FloatConstantCheat _maxHeightCheat;
        private readonly FloatConstantCheat _minHeightCheat;
        private readonly BytePatchCheat _positionClampCheat;
        private int? _attachedPid;
        private bool _suppressToggleEvent;
        private bool _hotkeyRegistered;

        public Form1()
        {
            _settings = Settings.Load();

            _maxHeightCheat = new FloatConstantCheat(
                "Hard max height",
                "Lifts the global maximum height clamp (default 231.20 cm).",
                GameOffsets.HARD_MAX_HEIGHT, GameOffsets.DEFAULT_MAX_HEIGHT,
                _settings.MaxHeight);

            _minHeightCheat = new FloatConstantCheat(
                "Hard min height",
                "Lowers the global minimum height clamp (default 137.00 cm).",
                GameOffsets.HARD_MIN_HEIGHT, GameOffsets.DEFAULT_MIN_HEIGHT,
                _settings.MinHeight);

            _positionClampCheat = new BytePatchCheat(
                "Disable per-position height clamp",
                "Patches cmp eax,02 to cmp eax,FF in Get/SetHeight so Create Player accepts any height for any position.",
                new PatchSite(GameOffsets.PATCH_SET_HEIGHT_MODE_CMP, GameOffsets.CMP_PATCHED, GameOffsets.CMP_ORIGINAL),
                new PatchSite(GameOffsets.PATCH_GET_HEIGHT_MODE_CMP, GameOffsets.CMP_PATCHED, GameOffsets.CMP_ORIGINAL));

            BuildUi();

            Load += (_, _) => OnFormReady();
            FormClosing += (_, _) => OnFormClosingInternal();
        }

        private void BuildUi()
        {
            Text = "NBA 2K16 Trainer";
            ClientSize = new Size(580, 520);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);

            _statusLabel = new Label
            {
                Top = 12, Left = 12, Width = 556, Height = 22,
                Text = "Looking for nba2k16.exe...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
            };
            Controls.Add(_statusLabel);

            // ─── Height clamp box ──────────────────────────────
            var heightBox = new GroupBox
            {
                Text = "Global height clamp", Top = 40, Left = 12, Width = 556, Height = 170,
            };
            Controls.Add(heightBox);

            heightBox.Controls.Add(new Label { Text = "Max height (cm):", Top = 28, Left = 14, Width = 110 });
            _maxBox = new NumericUpDown
            {
                Top = 25, Left = 130, Width = 110,
                Minimum = 50, Maximum = 9999, DecimalPlaces = 2, Increment = 1,
                Value = (decimal)_settings.MaxHeight,
            };
            heightBox.Controls.Add(_maxBox);
            _liveMaxLabel = new Label
            {
                Top = 28, Left = 260, Width = 280,
                Text = "Live: —",
                ForeColor = Color.DimGray,
            };
            heightBox.Controls.Add(_liveMaxLabel);

            heightBox.Controls.Add(new Label { Text = "Min height (cm):", Top = 60, Left = 14, Width = 110 });
            _minBox = new NumericUpDown
            {
                Top = 57, Left = 130, Width = 110,
                Minimum = 1, Maximum = 9999, DecimalPlaces = 2, Increment = 1,
                Value = (decimal)_settings.MinHeight,
            };
            heightBox.Controls.Add(_minBox);
            _liveMinLabel = new Label
            {
                Top = 60, Left = 260, Width = 280,
                Text = "Live: —",
                ForeColor = Color.DimGray,
            };
            heightBox.Controls.Add(_liveMinLabel);

            _applyHeightBtn = new Button
            {
                Text = "Apply", Top = 100, Left = 14, Width = 130, Height = 32,
            };
            _applyHeightBtn.Click += (_, _) => ApplyHeightConstants();
            heightBox.Controls.Add(_applyHeightBtn);

            _restoreHeightBtn = new Button
            {
                Text = "Restore defaults (231.20 / 137.00)", Top = 100, Left = 154, Width = 240, Height = 32,
            };
            _restoreHeightBtn.Click += (_, _) => RestoreHeightConstants();
            heightBox.Controls.Add(_restoreHeightBtn);

            heightBox.Controls.Add(new Label
            {
                Top = 138, Left = 14, Width = 530, Height = 24,
                Text = "Tip: 1 inch ≈ 2.54 cm. NBA range is roughly 160–230 cm; pushing past that is the point.",
                ForeColor = Color.DimGray,
            });

            // ─── Cheats box ───────────────────────────────────
            var cheatsBox = new GroupBox
            {
                Text = "Cheats", Top = 220, Left = 12, Width = 556, Height = 90,
            };
            Controls.Add(cheatsBox);

            _positionClampToggle = new CheckBox
            {
                Text = "Disable per-position height clamp  (hotkey: F1)",
                Top = 26, Left = 14, Width = 380, Height = 22,
                Checked = _settings.DisablePositionClamp,
            };
            _positionClampToggle.CheckedChanged += (_, _) => OnPositionClampToggleChanged();
            cheatsBox.Controls.Add(_positionClampToggle);

            _autoApplyToggle = new CheckBox
            {
                Text = "Auto-apply saved settings when game is detected",
                Top = 54, Left = 14, Width = 380, Height = 22,
                Checked = _settings.AutoApplyOnAttach,
            };
            _autoApplyToggle.CheckedChanged += (_, _) =>
            {
                _settings.AutoApplyOnAttach = _autoApplyToggle.Checked;
                _settings.Save();
            };
            cheatsBox.Controls.Add(_autoApplyToggle);

            // ─── Log box ──────────────────────────────────────
            var logGroup = new GroupBox
            {
                Text = "Log", Top = 320, Left = 12, Width = 556, Height = 188,
            };
            Controls.Add(logGroup);

            _logBox = new TextBox
            {
                Top = 22, Left = 12, Width = 532, Height = 130,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.75f),
            };
            logGroup.Controls.Add(_logBox);

            _clearLogBtn = new Button
            {
                Text = "Clear log", Top = 156, Left = 12, Width = 100, Height = 24,
            };
            _clearLogBtn.Click += (_, _) => _logBox.Clear();
            logGroup.Controls.Add(_clearLogBtn);

            // ─── Attach timer ─────────────────────────────────
            _attachTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _attachTimer.Tick += (_, _) => PollForGame();

            // Start disabled; OnAttached will enable.
            SetControlsEnabled(false);
        }

        private void OnFormReady()
        {
            if (!_settings.AcceptedDisclaimer)
            {
                if (!ShowDisclaimer())
                {
                    // User declined; close the trainer.
                    BeginInvoke(new Action(Close));
                    return;
                }
                _settings.AcceptedDisclaimer = true;
                _settings.Save();
            }

            _hotkeyRegistered = RegisterHotKey(Handle, HOTKEY_ID_TOGGLE_CLAMP, 0, (uint)Keys.F1);
            if (!_hotkeyRegistered)
                Log("Warning: F1 hotkey could not be registered (another app may own it).");

            SetControlsEnabled(false);
            _attachTimer.Start();
            PollForGame();
        }

        private void OnFormClosingInternal()
        {
            if (_hotkeyRegistered)
                UnregisterHotKey(Handle, HOTKEY_ID_TOGGLE_CLAMP);
            _attachTimer?.Stop();
            PersistInputs();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID_TOGGLE_CLAMP)
            {
                _positionClampToggle.Checked = !_positionClampToggle.Checked;
                return;
            }
            base.WndProc(ref m);
        }

        // ─── Attach loop ──────────────────────────────────────

        private void PollForGame()
        {
            var procs = Process.GetProcessesByName("nba2k16");
            try
            {
                if (procs.Length == 0)
                {
                    if (_attachedPid is not null)
                        OnDetached();
                    return;
                }

                var proc = procs[0];
                if (_attachedPid != proc.Id)
                {
                    OnAttached(proc);
                }
            }
            finally
            {
                // Process objects from GetProcessesByName hold handles; release them.
                foreach (var p in procs) p.Dispose();
            }
        }

        private void OnAttached(Process proc)
        {
            _attachedPid = proc.Id;
            _statusLabel.Text = $"Attached to nba2k16.exe (PID {proc.Id}).";
            _statusLabel.ForeColor = Color.DarkGreen;
            Log($"Attached to PID {proc.Id}.");
            SetControlsEnabled(true);

            // Probe the live constants so the user sees what's currently in memory.
            try
            {
                using var session = ProcessSession.Open(proc);
                _maxHeightCheat.Probe(session);
                _minHeightCheat.Probe(session);
                _liveMaxLabel.Text = $"Live: {_maxHeightCheat.LiveValue:F2} cm";
                _liveMinLabel.Text = $"Live: {_minHeightCheat.LiveValue:F2} cm";

                if (_settings.AutoApplyOnAttach)
                {
                    Log("Auto-apply on attach enabled — re-applying saved settings.");
                    ApplyHeightConstantsCore(session);
                    if (_settings.DisablePositionClamp)
                        _positionClampCheat.Apply(session);
                }
            }
            catch (Exception ex)
            {
                Log("Probe failed: " + ex.Message);
                _liveMaxLabel.Text = "Live: ?";
                _liveMinLabel.Text = "Live: ?";
            }
        }

        private void OnDetached()
        {
            Log($"Lost PID {_attachedPid}.");
            _attachedPid = null;
            _statusLabel.Text = "nba2k16.exe is not running.";
            _statusLabel.ForeColor = Color.Firebrick;
            _liveMaxLabel.Text = "Live: —";
            _liveMinLabel.Text = "Live: —";
            // Forget captured original bytes — next attach starts clean.
            _positionClampCheat.ResetCapturedState();
            SetControlsEnabled(false);
        }

        private void SetControlsEnabled(bool attached)
        {
            _maxBox.Enabled = attached;
            _minBox.Enabled = attached;
            _applyHeightBtn.Enabled = attached;
            _restoreHeightBtn.Enabled = attached;
            _positionClampToggle.Enabled = attached;
        }

        // ─── Cheat actions ────────────────────────────────────

        private bool TryGetSession(out ProcessSession? session)
        {
            session = null;
            var procs = Process.GetProcessesByName("nba2k16");
            try
            {
                if (procs.Length == 0)
                {
                    Log("No game process — cannot apply.");
                    return false;
                }
                session = ProcessSession.Open(procs[0]);
                return true;
            }
            catch (Exception ex)
            {
                Log("Open failed: " + ex.Message);
                return false;
            }
            finally
            {
                // Open() has already extracted everything it needs from the Process; drop the
                // .NET Process handles immediately. Our kernel handle lives in `session`.
                foreach (var p in procs) p.Dispose();
            }
        }

        private void ApplyHeightConstants()
        {
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    ApplyHeightConstantsCore(session);
                    PersistInputs();
                }
                catch (Exception ex)
                {
                    Log("Apply failed: " + ex.Message);
                }
            }
        }

        private void ApplyHeightConstantsCore(ProcessSession session)
        {
            float max = (float)_maxBox.Value;
            float min = (float)_minBox.Value;
            _maxHeightCheat.DesiredValue = max;
            _minHeightCheat.DesiredValue = min;

            _maxHeightCheat.Apply(session);
            _minHeightCheat.Apply(session);
            _liveMaxLabel.Text = $"Live: {_maxHeightCheat.LiveValue:F2} cm";
            _liveMinLabel.Text = $"Live: {_minHeightCheat.LiveValue:F2} cm";

            Log($"Wrote max={max:F2} (+0x{GameOffsets.HARD_MAX_HEIGHT:X}), min={min:F2} (+0x{GameOffsets.HARD_MIN_HEIGHT:X}).");
        }

        private void RestoreHeightConstants()
        {
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    _maxHeightCheat.Revert(session);
                    _minHeightCheat.Revert(session);
                    _maxBox.Value = (decimal)GameOffsets.DEFAULT_MAX_HEIGHT;
                    _minBox.Value = (decimal)GameOffsets.DEFAULT_MIN_HEIGHT;
                    _liveMaxLabel.Text = $"Live: {_maxHeightCheat.LiveValue:F2} cm";
                    _liveMinLabel.Text = $"Live: {_minHeightCheat.LiveValue:F2} cm";
                    PersistInputs();
                    Log("Restored hard min/max to game defaults.");
                }
                catch (Exception ex)
                {
                    Log("Restore failed: " + ex.Message);
                }
            }
        }

        private void OnPositionClampToggleChanged()
        {
            if (_suppressToggleEvent) return;
            bool desired = _positionClampToggle.Checked;

            if (!TryGetSession(out var session) || session is null)
            {
                // Roll back the visual toggle so it matches reality.
                _suppressToggleEvent = true;
                _positionClampToggle.Checked = !desired;
                _suppressToggleEvent = false;
                return;
            }

            using (session)
            {
                try
                {
                    if (desired) _positionClampCheat.Apply(session);
                    else _positionClampCheat.Revert(session);
                    _settings.DisablePositionClamp = desired;
                    _settings.Save();
                    Log(desired
                        ? "Per-position clamp disabled (cmp eax,02 → FF at +A30F42 and +A3FB12)."
                        : "Per-position clamp restored to original bytes.");
                }
                catch (Exception ex)
                {
                    Log("Toggle failed: " + ex.Message);
                    _suppressToggleEvent = true;
                    _positionClampToggle.Checked = !desired;
                    _suppressToggleEvent = false;
                }
            }
        }

        // ─── Misc ─────────────────────────────────────────────

        private void PersistInputs()
        {
            _settings.MaxHeight = (float)_maxBox.Value;
            _settings.MinHeight = (float)_minBox.Value;
            _settings.DisablePositionClamp = _positionClampToggle.Checked;
            _settings.AutoApplyOnAttach = _autoApplyToggle.Checked;
            _settings.Save();
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            if (_logBox.IsHandleCreated)
                _logBox.AppendText(line);
            else
                _logBox.Text += line;
        }

        private static bool ShowDisclaimer()
        {
            const string text =
                "NBA 2K16 Trainer — offline single-player only.\n\n"
                + "This tool modifies the running nba2k16.exe process. Using it in any online mode "
                + "(MyTeam, Pro-Am, MyPark, etc.) is likely to result in an account ban.\n\n"
                + "Only use this trainer in:\n"
                + "  • MyCareer offline\n"
                + "  • Play Now (offline)\n"
                + "  • Create Player\n\n"
                + "Click OK to accept and continue, or Cancel to exit.";
            var result = MessageBox.Show(text, "NBA 2K16 Trainer — disclaimer",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            return result == DialogResult.OK;
        }
    }
}
