using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
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

        // ─── UI: top bar + tabs ─────────────────────────────────────────────
        private Label _statusLabel = null!;
        private TabControl _tabs = null!;
        private TextBox _logBox = null!;
        private Button _clearLogBtn = null!;
        private System.Windows.Forms.Timer _attachTimer = null!;

        // Heights tab
        private NumericUpDown _maxBox = null!;
        private NumericUpDown _minBox = null!;
        private Label _liveMaxLabel = null!;
        private Label _liveMinLabel = null!;
        private Button _applyHeightBtn = null!;
        private Button _restoreHeightBtn = null!;
        private CheckBox _positionClampToggle = null!;
        private CheckBox _autoApplyToggle = null!;

        // Profile tab
        private TextBox _firstNameBox = null!;
        private TextBox _lastNameBox = null!;
        private ComboBox _primaryPosBox = null!;
        private ComboBox _secondaryPosBox = null!;
        private NumericUpDown _jerseyBox = null!;
        private NumericUpDown _weightBox = null!;
        private NumericUpDown _heightBox = null!;
        private NumericUpDown _wingspanBox = null!;
        private Label _liveProfileLabel = null!;
        private Button _applyProfileBtn = null!;
        private Button _revertProfileBtn = null!;
        private CheckBox _autoApplyProfileToggle = null!;

        // Ratings tab
        private readonly Dictionary<string, NumericUpDown> _ratingBoxes = new();
        private NumericUpDown _ratingOverrideBox = null!;
        private Button _ratingApplyOverrideBtn = null!;
        private Button _applyRatingsBtn = null!;
        private Button _revertRatingsBtn = null!;
        private CheckBox _autoApplyRatingsToggle = null!;

        // ─── State ──────────────────────────────────────────────────────────
        private readonly Settings _settings;
        private readonly FloatConstantCheat _maxHeightCheat;
        private readonly FloatConstantCheat _minHeightCheat;
        private readonly BytePatchCheat _positionClampCheat;
        private readonly PlayerResolver _resolver = new();
        private readonly PlayerProfileCheat _profileCheat = new();
        private readonly RatingsCheat _ratingsCheat = new();

        private int? _attachedPid;
        private IntPtr _lastPlayerBase = IntPtr.Zero;
        private bool _profileLoaded;       // probe completed at least once
        private bool _hookInstalled;
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

        // ─── UI construction ────────────────────────────────────────────────

        private void BuildUi()
        {
            Text = "NBA 2K16 Trainer";
            ClientSize = new Size(720, 720);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Font = new Font("Segoe UI", 9f);

            _statusLabel = new Label
            {
                Top = 12, Left = 12, Width = 696, Height = 22,
                Text = "Looking for nba2k16.exe...",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.DarkSlateGray,
            };
            Controls.Add(_statusLabel);

            _tabs = new TabControl
            {
                Top = 40, Left = 12, Width = 696, Height = 470,
            };
            Controls.Add(_tabs);

            BuildHeightsTab();
            BuildProfileTab();
            BuildRatingsTab();
            BuildBadgesTab();

            // ─── Log group spans the bottom ──────────────────────────────────
            var logGroup = new GroupBox
            {
                Text = "Log", Top = 520, Left = 12, Width = 696, Height = 188,
            };
            Controls.Add(logGroup);

            _logBox = new TextBox
            {
                Top = 22, Left = 12, Width = 672, Height = 130,
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

            _attachTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _attachTimer.Tick += (_, _) => OnTimerTick();

            SetGlobalControlsEnabled(false);
            SetPlayerControlsEnabled(false);
        }

        private void BuildHeightsTab()
        {
            var page = new TabPage("Heights (clamps)");
            _tabs.TabPages.Add(page);

            var box = new GroupBox
            {
                Text = "Global height clamp", Top = 8, Left = 8, Width = 672, Height = 170,
            };
            page.Controls.Add(box);

            box.Controls.Add(new Label { Text = "Max height (cm):", Top = 28, Left = 14, Width = 110 });
            _maxBox = new NumericUpDown
            {
                Top = 25, Left = 130, Width = 110,
                Minimum = 50, Maximum = 9999, DecimalPlaces = 2, Increment = 1,
                Value = (decimal)_settings.MaxHeight,
            };
            box.Controls.Add(_maxBox);
            _liveMaxLabel = new Label
            {
                Top = 28, Left = 260, Width = 280,
                Text = "Live: —", ForeColor = Color.DimGray,
            };
            box.Controls.Add(_liveMaxLabel);

            box.Controls.Add(new Label { Text = "Min height (cm):", Top = 60, Left = 14, Width = 110 });
            _minBox = new NumericUpDown
            {
                Top = 57, Left = 130, Width = 110,
                Minimum = 1, Maximum = 9999, DecimalPlaces = 2, Increment = 1,
                Value = (decimal)_settings.MinHeight,
            };
            box.Controls.Add(_minBox);
            _liveMinLabel = new Label
            {
                Top = 60, Left = 260, Width = 280,
                Text = "Live: —", ForeColor = Color.DimGray,
            };
            box.Controls.Add(_liveMinLabel);

            _applyHeightBtn = new Button { Text = "Apply", Top = 100, Left = 14, Width = 130, Height = 32 };
            _applyHeightBtn.Click += (_, _) => ApplyHeightConstants();
            box.Controls.Add(_applyHeightBtn);

            _restoreHeightBtn = new Button
            {
                Text = "Restore defaults (231.20 / 137.00)", Top = 100, Left = 154, Width = 240, Height = 32,
            };
            _restoreHeightBtn.Click += (_, _) => RestoreHeightConstants();
            box.Controls.Add(_restoreHeightBtn);

            box.Controls.Add(new Label
            {
                Top = 138, Left = 14, Width = 640, Height = 24,
                Text = "Tip: 1 inch ≈ 2.54 cm. NBA range is roughly 160–230 cm; pushing past that is the point.",
                ForeColor = Color.DimGray,
            });

            var cheatsBox = new GroupBox
            {
                Text = "Cheats", Top = 188, Left = 8, Width = 672, Height = 90,
            };
            page.Controls.Add(cheatsBox);

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
                Text = "Auto-apply saved height settings when game is detected",
                Top = 54, Left = 14, Width = 420, Height = 22,
                Checked = _settings.AutoApplyOnAttach,
            };
            _autoApplyToggle.CheckedChanged += (_, _) =>
            {
                _settings.AutoApplyOnAttach = _autoApplyToggle.Checked;
                _settings.Save();
            };
            cheatsBox.Controls.Add(_autoApplyToggle);
        }

        private void BuildProfileTab()
        {
            var page = new TabPage("Profile");
            _tabs.TabPages.Add(page);

            var idBox = new GroupBox
            {
                Text = "Identity", Top = 8, Left = 8, Width = 672, Height = 130,
            };
            page.Controls.Add(idBox);

            idBox.Controls.Add(new Label { Text = "First name:", Top = 28, Left = 14, Width = 80 });
            _firstNameBox = new TextBox { Top = 25, Left = 100, Width = 200, MaxLength = 19 };
            idBox.Controls.Add(_firstNameBox);

            idBox.Controls.Add(new Label { Text = "Last name:", Top = 60, Left = 14, Width = 80 });
            _lastNameBox = new TextBox { Top = 57, Left = 100, Width = 200, MaxLength = 17 };
            idBox.Controls.Add(_lastNameBox);

            idBox.Controls.Add(new Label { Text = "Jersey #:", Top = 28, Left = 320, Width = 60 });
            _jerseyBox = new NumericUpDown
            {
                Top = 25, Left = 385, Width = 70,
                Minimum = 0, Maximum = 99, Value = 0,
            };
            idBox.Controls.Add(_jerseyBox);

            idBox.Controls.Add(new Label { Text = "Primary pos:", Top = 60, Left = 320, Width = 90 });
            _primaryPosBox = new ComboBox
            {
                Top = 57, Left = 415, Width = 80,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _primaryPosBox.Items.AddRange(PositionNames.Display.Cast<object>().ToArray());
            idBox.Controls.Add(_primaryPosBox);

            idBox.Controls.Add(new Label { Text = "Secondary pos:", Top = 92, Left = 320, Width = 90 });
            _secondaryPosBox = new ComboBox
            {
                Top = 89, Left = 415, Width = 80,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _secondaryPosBox.Items.AddRange(PositionNames.Display.Cast<object>().ToArray());
            idBox.Controls.Add(_secondaryPosBox);

            var bodyBox = new GroupBox
            {
                Text = "Body", Top = 148, Left = 8, Width = 672, Height = 110,
            };
            page.Controls.Add(bodyBox);

            bodyBox.Controls.Add(new Label { Text = "Height (cm):", Top = 28, Left = 14, Width = 90 });
            _heightBox = new NumericUpDown
            {
                Top = 25, Left = 110, Width = 100,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_heightBox);

            bodyBox.Controls.Add(new Label { Text = "Wingspan (cm):", Top = 28, Left = 230, Width = 100 });
            _wingspanBox = new NumericUpDown
            {
                Top = 25, Left = 335, Width = 100,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_wingspanBox);

            bodyBox.Controls.Add(new Label { Text = "Weight (lbs):", Top = 60, Left = 14, Width = 90 });
            _weightBox = new NumericUpDown
            {
                Top = 57, Left = 110, Width = 100,
                Minimum = 50, Maximum = 800, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_weightBox);

            _liveProfileLabel = new Label
            {
                Top = 268, Left = 14, Width = 660, Height = 20,
                Text = "Live: —", ForeColor = Color.DimGray,
            };
            page.Controls.Add(_liveProfileLabel);

            _applyProfileBtn = new Button { Text = "Apply", Top = 295, Left = 14, Width = 130, Height = 32 };
            _applyProfileBtn.Click += (_, _) => ApplyProfile();
            page.Controls.Add(_applyProfileBtn);

            _revertProfileBtn = new Button
            {
                Text = "Revert to attach-time values", Top = 295, Left = 154, Width = 220, Height = 32,
            };
            _revertProfileBtn.Click += (_, _) => RevertProfile();
            page.Controls.Add(_revertProfileBtn);

            _autoApplyProfileToggle = new CheckBox
            {
                Text = "Auto-apply profile when player resolves",
                Top = 340, Left = 14, Width = 360, Height = 22,
                Checked = _settings.AutoApplyProfile,
            };
            _autoApplyProfileToggle.CheckedChanged += (_, _) =>
            {
                _settings.AutoApplyProfile = _autoApplyProfileToggle.Checked;
                _settings.Save();
            };
            page.Controls.Add(_autoApplyProfileToggle);

            page.Controls.Add(new Label
            {
                Top = 372, Left = 14, Width = 660, Height = 36,
                Text = "Note: per-player Height/Wingspan are written through the +0x80 sub-pointer. The global "
                     + "height clamps on the Heights tab still apply — raise them first if you want to push past "
                     + "the default 231.20 cm.",
                ForeColor = Color.DimGray,
            });
        }

        private void BuildRatingsTab()
        {
            var page = new TabPage("Ratings");
            _tabs.TabPages.Add(page);

            page.Controls.Add(new Label
            {
                Top = 8, Left = 8, Width = 660, Height = 18,
                Text = "Set every rating below, or use the override field to set them all to one value.",
                ForeColor = Color.DimGray,
            });

            page.Controls.Add(new Label { Text = "Override all to:", Top = 32, Left = 8, Width = 100 });
            _ratingOverrideBox = new NumericUpDown
            {
                Top = 28, Left = 115, Width = 70, Minimum = 0, Maximum = 99, Value = 99,
            };
            page.Controls.Add(_ratingOverrideBox);

            _ratingApplyOverrideBtn = new Button
            {
                Text = "Fill all", Top = 27, Left = 195, Width = 80, Height = 24,
            };
            _ratingApplyOverrideBtn.Click += (_, _) => FillAllRatings((byte)_ratingOverrideBox.Value);
            page.Controls.Add(_ratingApplyOverrideBtn);

            // Build a scrolling panel that holds one GroupBox per rating group.
            var scroll = new Panel
            {
                Top = 60, Left = 8, Width = 672, Height = 280,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            var groups = RatingsCheat.Ratings
                .GroupBy(r => r.Group)
                .OrderBy(g => g.Key)
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox
                {
                    Text = grp.Key, Top = top, Left = 4, Width = 644,
                };

                int innerTop = 22;
                int col = 0;
                foreach (var r in grp)
                {
                    int x = col == 0 ? 8 : 320;
                    int y = innerTop + (col == 0 ? 0 : 0);

                    gb.Controls.Add(new Label
                    {
                        Text = r.Name + ":", Top = y + 3, Left = x, Width = 200,
                    });
                    var num = new NumericUpDown
                    {
                        Top = y, Left = x + 200, Width = 70,
                        Minimum = 0, Maximum = 99, Value = 0,
                    };
                    _ratingBoxes[r.Name] = num;
                    gb.Controls.Add(num);

                    if (col == 1) innerTop += 28;
                    col = 1 - col;
                }
                if (col == 1) innerTop += 28; // half-row hanging
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);

                top += gb.Height + 6;
            }

            _applyRatingsBtn = new Button { Text = "Apply", Top = 348, Left = 8, Width = 130, Height = 32 };
            _applyRatingsBtn.Click += (_, _) => ApplyRatings();
            page.Controls.Add(_applyRatingsBtn);

            _revertRatingsBtn = new Button
            {
                Text = "Revert to attach-time values", Top = 348, Left = 148, Width = 220, Height = 32,
            };
            _revertRatingsBtn.Click += (_, _) => RevertRatings();
            page.Controls.Add(_revertRatingsBtn);

            _autoApplyRatingsToggle = new CheckBox
            {
                Text = "Auto-apply ratings when player resolves",
                Top = 386, Left = 8, Width = 360, Height = 22,
                Checked = _settings.AutoApplyRatings,
            };
            _autoApplyRatingsToggle.CheckedChanged += (_, _) =>
            {
                _settings.AutoApplyRatings = _autoApplyRatingsToggle.Checked;
                _settings.Save();
            };
            page.Controls.Add(_autoApplyRatingsToggle);

            page.Controls.Add(new Label
            {
                Top = 414, Left = 8, Width = 660, Height = 30,
                Text = "Caveat: the game re-clamps ratings on save reload. To keep changes, exit to main menu "
                     + "and reload the save right after Apply.",
                ForeColor = Color.Firebrick,
            });
        }

        private void BuildBadgesTab()
        {
            var page = new TabPage("Badges");
            _tabs.TabPages.Add(page);

            page.Controls.Add(new Label
            {
                Top = 16, Left = 16, Width = 660, Height = 200,
                Text = "Badges are scoped out but not yet wired. Each badge is a single bit at "
                     + "BadgePtr (= player + 0x419) with a fixed (byte, bit) offset. The full mapping "
                     + "lives in NBA2k16.ct (records 5032..5120) and will land here in a follow-up.",
                ForeColor = Color.DimGray,
            });
        }

        // ─── Lifecycle ──────────────────────────────────────────────────────

        private void OnFormReady()
        {
            if (!_settings.AcceptedDisclaimer)
            {
                if (!ShowDisclaimer())
                {
                    BeginInvoke(new Action(Close));
                    return;
                }
                _settings.AcceptedDisclaimer = true;
                _settings.Save();
            }

            _hotkeyRegistered = RegisterHotKey(Handle, HOTKEY_ID_TOGGLE_CLAMP, 0, (uint)Keys.F1);
            if (!_hotkeyRegistered)
                Log("Warning: F1 hotkey could not be registered (another app may own it).");

            _attachTimer.Start();
            OnTimerTick();
        }

        private void OnFormClosingInternal()
        {
            if (_hotkeyRegistered)
                UnregisterHotKey(Handle, HOTKEY_ID_TOGGLE_CLAMP);
            _attachTimer?.Stop();

            // Remove the resolver hook before we let go of the process.
            if (_hookInstalled && _attachedPid is int pid)
            {
                try
                {
                    using var proc = Process.GetProcessById(pid);
                    using var session = ProcessSession.Open(proc);
                    _resolver.Revert(session);
                    Log("Resolver hook reverted on close.");
                }
                catch
                {
                    // Game probably already exited; nothing to revert.
                }
                _hookInstalled = false;
            }

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

        // ─── Attach + resolve loop ──────────────────────────────────────────

        private void OnTimerTick()
        {
            var procs = Process.GetProcessesByName("nba2k16");
            try
            {
                if (procs.Length == 0)
                {
                    if (_attachedPid is not null) OnDetached();
                    return;
                }

                var proc = procs[0];
                if (_attachedPid != proc.Id)
                {
                    OnAttached(proc);
                }
                else if (_hookInstalled && !_profileLoaded)
                {
                    TryResolvePlayer();
                }
            }
            finally
            {
                foreach (var p in procs) p.Dispose();
            }
        }

        private void OnAttached(Process proc)
        {
            _attachedPid = proc.Id;
            SetGlobalControlsEnabled(true);
            Log($"Attached to PID {proc.Id}.");

            // Probe the global clamp constants and optionally re-apply them.
            try
            {
                using var session = ProcessSession.Open(proc);
                _maxHeightCheat.Probe(session);
                _minHeightCheat.Probe(session);
                _liveMaxLabel.Text = $"Live: {_maxHeightCheat.LiveValue:F2} cm";
                _liveMinLabel.Text = $"Live: {_minHeightCheat.LiveValue:F2} cm";

                if (_settings.AutoApplyOnAttach)
                {
                    Log("Auto-apply on attach enabled — re-applying saved height settings.");
                    ApplyHeightConstantsCore(session);
                    if (_settings.DisablePositionClamp)
                        _positionClampCheat.Apply(session);
                }

                // Install the resolver hook so the player struct slot starts filling.
                try
                {
                    _resolver.Install(session);
                    _hookInstalled = true;
                    UpdateStatusLabel();
                    Log($"Resolver hook installed at +0x{(_resolver.HookSite.ToInt64() - session.BaseAddress.ToInt64()):X} "
                        + $"(cave +0x{(_resolver.CaveBase.ToInt64() - session.BaseAddress.ToInt64()):X}).");
                }
                catch (Exception ex)
                {
                    Log("Resolver install failed: " + ex.Message);
                    UpdateStatusLabel();
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
            _hookInstalled = false;
            _profileLoaded = false;
            _lastPlayerBase = IntPtr.Zero;
            _liveMaxLabel.Text = "Live: —";
            _liveMinLabel.Text = "Live: —";
            _liveProfileLabel.Text = "Live: —";

            _positionClampCheat.ResetCapturedState();
            _profileCheat.ResetCapturedState();
            _ratingsCheat.ResetCapturedState();

            SetGlobalControlsEnabled(false);
            SetPlayerControlsEnabled(false);
            UpdateStatusLabel();
        }

        private void TryResolvePlayer()
        {
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                IntPtr p = _resolver.ReadPlayerPointer(session);
                if (p == IntPtr.Zero) return;

                _lastPlayerBase = p;

                try
                {
                    var profile = _profileCheat.Probe(session, p);
                    PopulateProfileInputs(profile);
                    _liveProfileLabel.Text = $"Live: {profile.FirstName} {profile.LastName} · "
                        + $"{PositionNames.Format(profile.PrimaryPosition)} #{profile.Jersey} · "
                        + $"{profile.Height:F2} cm / {profile.Wingspan:F2} cm wingspan / {profile.Weight:F2} lbs";

                    var ratings = _ratingsCheat.Probe(session, p);
                    PopulateRatingInputs(ratings);

                    _profileLoaded = true;
                    SetPlayerControlsEnabled(true);
                    UpdateStatusLabel();
                    Log($"Player resolved: {profile.FirstName} {profile.LastName} (struct @ 0x{p.ToInt64():X}).");

                    // Auto-apply if persisted settings exist.
                    if (_settings.AutoApplyProfile && SettingsHasProfile())
                    {
                        var desired = MergeProfileFromSettings(profile);
                        _profileCheat.Apply(session, p, desired);
                        Log("Auto-applied saved profile.");
                    }
                    if (_settings.AutoApplyRatings && _settings.RatingOverrides is { Count: > 0 })
                    {
                        var desired = new Dictionary<string, byte>(ratings);
                        foreach (var kv in _settings.RatingOverrides!) desired[kv.Key] = kv.Value;
                        _ratingsCheat.Apply(session, p, desired);
                        Log("Auto-applied saved rating overrides.");
                    }
                }
                catch (Exception ex)
                {
                    Log("Player resolve failed: " + ex.Message);
                }
            }
        }

        // ─── Existing height-clamp actions (preserved) ──────────────────────

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

        // ─── New profile/ratings actions ────────────────────────────────────

        private void ApplyProfile()
        {
            if (_lastPlayerBase == IntPtr.Zero)
            {
                Log("No player resolved yet — can't apply profile.");
                return;
            }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadProfileFromInputs();
                    _profileCheat.Apply(session, _lastPlayerBase, desired);
                    PersistProfileToSettings(desired);
                    Log($"Profile written: {desired.FirstName} {desired.LastName} · "
                        + $"{PositionNames.Format(desired.PrimaryPosition)}/{PositionNames.Format(desired.SecondaryPosition)} · "
                        + $"#{desired.Jersey} · {desired.Height:F2}cm / {desired.Wingspan:F2}cm wing / {desired.Weight:F2}lbs.");

                    // Refresh live label.
                    var live = _profileCheat.Read(session, _lastPlayerBase);
                    _liveProfileLabel.Text = $"Live: {live.FirstName} {live.LastName} · "
                        + $"{PositionNames.Format(live.PrimaryPosition)} #{live.Jersey} · "
                        + $"{live.Height:F2} cm / {live.Wingspan:F2} cm wingspan / {live.Weight:F2} lbs";
                }
                catch (Exception ex)
                {
                    Log("Profile apply failed: " + ex.Message);
                }
            }
        }

        private void RevertProfile()
        {
            if (_lastPlayerBase == IntPtr.Zero) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    _profileCheat.Revert(session, _lastPlayerBase);
                    if (_profileCheat.Original is { } original) PopulateProfileInputs(original);
                    Log("Profile reverted to attach-time values.");
                }
                catch (Exception ex)
                {
                    Log("Profile revert failed: " + ex.Message);
                }
            }
        }

        private void ApplyRatings()
        {
            if (_lastPlayerBase == IntPtr.Zero)
            {
                Log("No player resolved yet — can't apply ratings.");
                return;
            }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadRatingsFromInputs();
                    _ratingsCheat.Apply(session, _lastPlayerBase, desired);

                    // Persist only the entries that differ from "Original" (so we don't
                    // pin every rating across sessions).
                    var overrides = _ratingsCheat.Original is { } orig
                        ? desired.Where(kv => !orig.TryGetValue(kv.Key, out byte ov) || ov != kv.Value)
                                 .ToDictionary(kv => kv.Key, kv => kv.Value)
                        : desired;
                    _settings.RatingOverrides = overrides.Count > 0 ? overrides : null;
                    _settings.Save();
                    Log($"Ratings written ({overrides.Count} overrides). Reload the save to lock in.");
                }
                catch (Exception ex)
                {
                    Log("Ratings apply failed: " + ex.Message);
                }
            }
        }

        private void RevertRatings()
        {
            if (_lastPlayerBase == IntPtr.Zero) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    _ratingsCheat.Revert(session, _lastPlayerBase);
                    if (_ratingsCheat.Original is { } original) PopulateRatingInputs(original);
                    Log("Ratings reverted to attach-time values.");
                }
                catch (Exception ex)
                {
                    Log("Ratings revert failed: " + ex.Message);
                }
            }
        }

        private void FillAllRatings(byte v)
        {
            foreach (var box in _ratingBoxes.Values) box.Value = v;
        }

        // ─── Glue: settings ↔ inputs ────────────────────────────────────────

        private void PopulateProfileInputs(PlayerProfileSnapshot snap)
        {
            _firstNameBox.Text = snap.FirstName;
            _lastNameBox.Text = snap.LastName;
            _primaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.PrimaryPosition);
            _secondaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.SecondaryPosition);
            _jerseyBox.Value = snap.Jersey;
            _weightBox.Value = (decimal)Math.Clamp(snap.Weight, (float)_weightBox.Minimum, (float)_weightBox.Maximum);
            _heightBox.Value = (decimal)Math.Clamp(snap.Height, (float)_heightBox.Minimum, (float)_heightBox.Maximum);
            _wingspanBox.Value = (decimal)Math.Clamp(snap.Wingspan, (float)_wingspanBox.Minimum, (float)_wingspanBox.Maximum);
        }

        private void PopulateRatingInputs(Dictionary<string, byte> values)
        {
            foreach (var kv in values)
            {
                if (_ratingBoxes.TryGetValue(kv.Key, out var box))
                    box.Value = kv.Value;
            }
        }

        private PlayerProfileSnapshot ReadProfileFromInputs() => new(
            FirstName: _firstNameBox.Text,
            LastName: _lastNameBox.Text,
            PrimaryPosition: PositionNames.IndexToRaw(Math.Max(0, _primaryPosBox.SelectedIndex)),
            SecondaryPosition: PositionNames.IndexToRaw(Math.Max(0, _secondaryPosBox.SelectedIndex)),
            Weight: (float)_weightBox.Value,
            Jersey: (byte)_jerseyBox.Value,
            Height: (float)_heightBox.Value,
            Wingspan: (float)_wingspanBox.Value);

        private Dictionary<string, byte> ReadRatingsFromInputs()
        {
            var dict = new Dictionary<string, byte>(_ratingBoxes.Count);
            foreach (var kv in _ratingBoxes) dict[kv.Key] = (byte)kv.Value.Value;
            return dict;
        }

        private bool SettingsHasProfile() =>
            _settings.FirstName is not null
            || _settings.LastName is not null
            || _settings.PrimaryPosition is not null
            || _settings.SecondaryPosition is not null
            || _settings.Weight is not null
            || _settings.Jersey is not null
            || _settings.PerPlayerHeight is not null
            || _settings.Wingspan is not null;

        private PlayerProfileSnapshot MergeProfileFromSettings(PlayerProfileSnapshot live) => live with
        {
            FirstName = _settings.FirstName ?? live.FirstName,
            LastName = _settings.LastName ?? live.LastName,
            PrimaryPosition = _settings.PrimaryPosition ?? live.PrimaryPosition,
            SecondaryPosition = _settings.SecondaryPosition ?? live.SecondaryPosition,
            Weight = _settings.Weight ?? live.Weight,
            Jersey = _settings.Jersey ?? live.Jersey,
            Height = _settings.PerPlayerHeight ?? live.Height,
            Wingspan = _settings.Wingspan ?? live.Wingspan,
        };

        private void PersistProfileToSettings(PlayerProfileSnapshot v)
        {
            _settings.FirstName = v.FirstName;
            _settings.LastName = v.LastName;
            _settings.PrimaryPosition = v.PrimaryPosition;
            _settings.SecondaryPosition = v.SecondaryPosition;
            _settings.Weight = v.Weight;
            _settings.Jersey = v.Jersey;
            _settings.PerPlayerHeight = v.Height;
            _settings.Wingspan = v.Wingspan;
            _settings.Save();
        }

        private void PersistInputs()
        {
            _settings.MaxHeight = (float)_maxBox.Value;
            _settings.MinHeight = (float)_minBox.Value;
            _settings.DisablePositionClamp = _positionClampToggle.Checked;
            _settings.AutoApplyOnAttach = _autoApplyToggle.Checked;
            _settings.AutoApplyProfile = _autoApplyProfileToggle.Checked;
            _settings.AutoApplyRatings = _autoApplyRatingsToggle.Checked;
            _settings.Save();
        }

        // ─── Misc UI ────────────────────────────────────────────────────────

        private void SetGlobalControlsEnabled(bool attached)
        {
            _maxBox.Enabled = attached;
            _minBox.Enabled = attached;
            _applyHeightBtn.Enabled = attached;
            _restoreHeightBtn.Enabled = attached;
            _positionClampToggle.Enabled = attached;
        }

        private void SetPlayerControlsEnabled(bool resolved)
        {
            _firstNameBox.Enabled = resolved;
            _lastNameBox.Enabled = resolved;
            _primaryPosBox.Enabled = resolved;
            _secondaryPosBox.Enabled = resolved;
            _jerseyBox.Enabled = resolved;
            _weightBox.Enabled = resolved;
            _heightBox.Enabled = resolved;
            _wingspanBox.Enabled = resolved;
            _applyProfileBtn.Enabled = resolved;
            _revertProfileBtn.Enabled = resolved;

            foreach (var box in _ratingBoxes.Values) box.Enabled = resolved;
            _ratingOverrideBox.Enabled = resolved;
            _ratingApplyOverrideBtn.Enabled = resolved;
            _applyRatingsBtn.Enabled = resolved;
            _revertRatingsBtn.Enabled = resolved;
        }

        private void UpdateStatusLabel()
        {
            if (_attachedPid is null)
            {
                _statusLabel.Text = "nba2k16.exe is not running.";
                _statusLabel.ForeColor = Color.Firebrick;
            }
            else if (!_hookInstalled)
            {
                _statusLabel.Text = $"Attached to nba2k16.exe (PID {_attachedPid}). Heights tab works; profile features unavailable.";
                _statusLabel.ForeColor = Color.DarkOrange;
            }
            else if (!_profileLoaded)
            {
                _statusLabel.Text = $"Hook installed (PID {_attachedPid}). Waiting for the game to write player data — load a MyCareer match.";
                _statusLabel.ForeColor = Color.DarkSlateBlue;
            }
            else
            {
                _statusLabel.Text = $"Player resolved (PID {_attachedPid}). All controls live.";
                _statusLabel.ForeColor = Color.DarkGreen;
            }
        }

        private void Log(string message)
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            if (_logBox.IsHandleCreated) _logBox.AppendText(line);
            else _logBox.Text += line;
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
