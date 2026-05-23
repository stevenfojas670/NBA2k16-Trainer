using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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
        private Button _copyLogBtn = null!;
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
        private NumericUpDown _gameplayHeightBox = null!;
        private NumericUpDown _gameplayWingspanBox = null!;
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

        // Badges tab
        private readonly Dictionary<string, ComboBox> _badgeBoxes = new();
        private Button _applyBadgesBtn = null!;
        private Button _revertBadgesBtn = null!;
        private CheckBox _autoApplyBadgesToggle = null!;

        // Roster tab — picker bar
        private ComboBox _rosterTeamCombo = null!;
        private ListBox _rosterPlayerList = null!;
        private Button _rosterRefreshBtn = null!;
        private Button _revertRosterToBaselineBtn = null!;
        private Label _rosterStatusLabel = null!;
        private TabControl _rosterSubTabs = null!;

        // Roster tab — Profile sub-page
        private TextBox _rosterFirstNameBox = null!;
        private TextBox _rosterLastNameBox = null!;
        private NumericUpDown _rosterJerseyBox = null!;
        private ComboBox _rosterPrimaryPosBox = null!;
        private ComboBox _rosterSecondaryPosBox = null!;
        private NumericUpDown _rosterWeightBox = null!;
        private NumericUpDown _rosterHeightBox = null!;
        private NumericUpDown _rosterWingspanBox = null!;
        private Button _applyRosterProfileBtn = null!;
        private Button _revertRosterProfileBtn = null!;

        // Roster tab — Ratings sub-page
        private readonly Dictionary<string, NumericUpDown> _rosterRatingBoxes = new();
        private NumericUpDown _rosterRatingOverrideBox = null!;
        private Button _rosterRatingApplyOverrideBtn = null!;
        private Button _applyRosterRatingsBtn = null!;
        private Button _revertRosterRatingsBtn = null!;

        // Roster tab — Badges sub-page
        private readonly Dictionary<string, ComboBox> _rosterBadgeBoxes = new();
        private Button _applyRosterBadgesBtn = null!;
        private Button _revertRosterBadgesBtn = null!;
        private Button _maxAllRosterBadgesBtn = null!;

        // Roster tab — Tendencies sub-page
        private readonly Dictionary<string, NumericUpDown> _rosterTendencyBoxes = new();
        private NumericUpDown _rosterTendencyOverrideBox = null!;
        private Button _rosterTendencyApplyOverrideBtn = null!;
        private Button _applyRosterTendenciesBtn = null!;
        private Button _revertRosterTendenciesBtn = null!;

        // ─── State ──────────────────────────────────────────────────────────
        private readonly Settings _settings;
        private readonly FloatConstantCheat _maxHeightCheat;
        private readonly FloatConstantCheat _minHeightCheat;
        private readonly BytePatchCheat _positionClampCheat;
        private readonly PlayerResolver _resolver = new();
        private readonly PlayerProfileCheat _profileCheat = new();
        private readonly RatingsCheat _ratingsCheat = new();
        private readonly BadgesCheat _badgesCheat = new();

        // Separate instances so editing a roster player never touches the
        // MyPlayer-resolver tab state. Same I/O code, different snapshots.
        // Roster/Players Ratings uses StaticRosterRatingsCheat (+0x388, scaled
        // UI 25..99 -> byte 0..222) NOT RatingsCheat which targets the heap
        // MyPlayer's +0x3C4 layout (and which in the static roster turned out
        // to be the tendency block, not ratings).
        private readonly RosterResolver _rosterResolver = new();
        private readonly PlayerProfileCheat _rosterProfileCheat = new();
        private readonly StaticRosterRatingsCheat _rosterRatingsCheat = new();
        private readonly BadgesCheat _rosterBadgesCheat = new();
        private readonly TendenciesCheat _rosterTendenciesCheat = new();

        // Persists raw 0x430-byte snapshots so we can revert any roster
        // player to their "original" state across trainer launches.
        private readonly RosterBaselineStore _baselineStore = new();

        // Currently-loaded roster player. -1 means "no player selected".
        private int _rosterSelectedIndex = -1;
        private bool _rosterSuppressEvents;

        // ─── Players tab (flat searchable list) ─────────────────────────────
        private TextBox _playerListSearchBox = null!;
        private ListBox _playerListListBox = null!;
        private Button _playerListRefreshBtn = null!;
        private Button _revertPlayerListToBaselineBtn = null!;
        private Label _playerListStatusLabel = null!;
        private TabControl _playerListSubTabs = null!;

        // Profile sub-page fields
        private TextBox _playerListFirstNameBox = null!;
        private TextBox _playerListLastNameBox = null!;
        private NumericUpDown _playerListJerseyBox = null!;
        private ComboBox _playerListPrimaryPosBox = null!;
        private ComboBox _playerListSecondaryPosBox = null!;
        private NumericUpDown _playerListWeightBox = null!;
        private NumericUpDown _playerListHeightBox = null!;
        private NumericUpDown _playerListWingspanBox = null!;
        private Button _applyPlayerListProfileBtn = null!;
        private Button _revertPlayerListProfileBtn = null!;

        // Ratings sub-page fields
        private readonly Dictionary<string, NumericUpDown> _playerListRatingBoxes = new();
        private NumericUpDown _playerListRatingOverrideBox = null!;
        private Button _playerListRatingApplyOverrideBtn = null!;
        private Button _applyPlayerListRatingsBtn = null!;
        private Button _revertPlayerListRatingsBtn = null!;

        // Badges sub-page fields
        private readonly Dictionary<string, ComboBox> _playerListBadgeBoxes = new();
        private Button _applyPlayerListBadgesBtn = null!;
        private Button _revertPlayerListBadgesBtn = null!;
        private Button _maxAllPlayerListBadgesBtn = null!;

        // Tendencies sub-page fields
        private readonly Dictionary<string, NumericUpDown> _playerListTendencyBoxes = new();
        private NumericUpDown _playerListTendencyOverrideBox = null!;
        private Button _playerListTendencyApplyOverrideBtn = null!;
        private Button _applyPlayerListTendenciesBtn = null!;
        private Button _revertPlayerListTendenciesBtn = null!;

        // Third independent set of cheats so Players tab tracks its own
        // captured-original state. Mirrors how the Roster tab is isolated
        // from the MyPlayer tab. Ratings cheat is StaticRosterRatingsCheat
        // (see comment on _rosterRatingsCheat for why).
        private readonly PlayerProfileCheat _playerListProfileCheat = new();
        private readonly StaticRosterRatingsCheat _playerListRatingsCheat = new();
        private readonly BadgesCheat _playerListBadgesCheat = new();
        private readonly TendenciesCheat _playerListTendenciesCheat = new();

        // Currently-loaded player (real roster index). -1 = none selected.
        private int _playerListSelectedRosterIndex = -1;
        // Cached "Last, First — Team" labels, one per roster index.
        // Built once at attach; search filter projects from this.
        private string[] _playerListAllLabels = Array.Empty<string>();
        // ListBox row → real roster index, rebuilt by search filter.
        private int[] _playerListVisibleIndices = Array.Empty<int>();
        private bool _playerListSuppressEvents;

        private int? _attachedPid;
        private IntPtr _lastPlayerBase = IntPtr.Zero;
        // All discovered copies of the active MyPlayer struct in process memory.
        // Cheats write to every copy so save-reads pick up the edit regardless
        // of which copy the save serializer pulls from. Always contains the
        // active copy as element 0; refreshed when the active ptr changes.
        private IReadOnlyList<IntPtr> _playerCopies = Array.Empty<IntPtr>();
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
                Top = 40, Left = 12, Width = 696, Height = 668,
            };
            Controls.Add(_tabs);

            BuildHeightsTab();
            BuildProfileTab();
            BuildRatingsTab();
            BuildBadgesTab();
            BuildRosterTab();
            BuildPlayerListTab();
            BuildLogTab();

            _attachTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _attachTimer.Tick += (_, _) => OnTimerTick();

            SetGlobalControlsEnabled(false);
            SetPlayerControlsEnabled(false);
            SetRosterTeamControlsEnabled(false);
            SetRosterPlayerControlsEnabled(false);
            SetPlayerListSearchControlsEnabled(false);
            SetPlayerListPlayerControlsEnabled(false);
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
                Minimum = 0, Maximum = 255, Value = 0,
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
                Text = "Body", Top = 148, Left = 8, Width = 672, Height = 142,
            };
            page.Controls.Add(bodyBox);

            // Row 1 — visual height/wingspan. These feed the heap-resident player
            // struct copies; the mesh re-instantiates from them at halftime / replay.
            bodyBox.Controls.Add(new Label { Text = "Height (visual):", Top = 28, Left = 14, Width = 90 });
            _heightBox = new NumericUpDown
            {
                Top = 25, Left = 110, Width = 100,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_heightBox);

            bodyBox.Controls.Add(new Label { Text = "Wingspan (visual):", Top = 28, Left = 230, Width = 100 });
            _wingspanBox = new NumericUpDown
            {
                Top = 25, Left = 335, Width = 100,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_wingspanBox);

            // Row 2 — gameplay height/wingspan. These feed the .rdata-pointed copy
            // that FUN_140c0a8e0 reads every frame for the reach / max-step formula.
            // Tall values here amplify movement during dunk animations.
            bodyBox.Controls.Add(new Label { Text = "Height (gameplay):", Top = 60, Left = 14, Width = 100 });
            _gameplayHeightBox = new NumericUpDown
            {
                Top = 57, Left = 110, Width = 100,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_gameplayHeightBox);

            bodyBox.Controls.Add(new Label { Text = "Wingspan (gameplay):", Top = 60, Left = 230, Width = 110 });
            _gameplayWingspanBox = new NumericUpDown
            {
                Top = 57, Left = 335, Width = 100,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_gameplayWingspanBox);

            bodyBox.Controls.Add(new Label { Text = "Weight (lbs):", Top = 92, Left = 14, Width = 90 });
            _weightBox = new NumericUpDown
            {
                Top = 89, Left = 110, Width = 100,
                Minimum = 50, Maximum = 800, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_weightBox);

            _liveProfileLabel = new Label
            {
                Top = 300, Left = 14, Width = 660, Height = 20,
                Text = "Live: —", ForeColor = Color.DimGray,
            };
            page.Controls.Add(_liveProfileLabel);

            _applyProfileBtn = new Button { Text = "Apply", Top = 327, Left = 14, Width = 130, Height = 32 };
            _applyProfileBtn.Click += (_, _) => ApplyProfile();
            page.Controls.Add(_applyProfileBtn);

            _revertProfileBtn = new Button
            {
                Text = "Revert to attach-time values", Top = 327, Left = 154, Width = 220, Height = 32,
            };
            _revertProfileBtn.Click += (_, _) => RevertProfile();
            page.Controls.Add(_revertProfileBtn);

            _autoApplyProfileToggle = new CheckBox
            {
                Text = "Auto-apply profile when player resolves",
                Top = 372, Left = 14, Width = 360, Height = 22,
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
                Top = 404, Left = 14, Width = 660, Height = 50,
                Text = "Visual values feed the heap copies (mesh refreshes at halftime). Gameplay values feed the "
                     + ".rdata-pointed copy used by the per-frame reach formula — tall gameplay heights amplify "
                     + "step distance during dunks. Keep them equal for the original behaviour; lower gameplay "
                     + "height to keep dunks at normal speed while looking tall. Global height clamps on the "
                     + "Heights tab still apply.",
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

            // Scrolling panel of grouped badges — same shape as the Ratings tab so
            // the two screens behave identically (scroll, GroupBox per category,
            // two columns of editor rows).
            var scroll = new Panel
            {
                Top = 8, Left = 8, Width = 688, Height = 332,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            string[] tierItems = { "OFF", "Bronze", "Silver", "Gold" };
            string[] toggleItems = { "OFF", "ON" };

            var groups = BadgesCheat.Badges
                .GroupBy(b => b.Group)
                .OrderBy(g => g.Key)
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox
                {
                    Text = grp.Key, Top = top, Left = 4, Width = 660,
                };

                int innerTop = 22;
                int col = 0;
                foreach (var b in grp)
                {
                    int x = col == 0 ? 8 : 328;
                    int y = innerTop;

                    gb.Controls.Add(new Label
                    {
                        Text = b.Name + ":", Top = y + 3, Left = x, Width = 220,
                        AutoEllipsis = true,
                    });
                    var combo = new ComboBox
                    {
                        Top = y, Left = x + 220, Width = 90,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                    };
                    combo.Items.AddRange(b.BitLength == 1 ? toggleItems : tierItems);
                    combo.SelectedIndex = 0;
                    _badgeBoxes[b.Name] = combo;
                    gb.Controls.Add(combo);

                    if (col == 1) innerTop += 28;
                    col = 1 - col;
                }
                if (col == 1) innerTop += 28;
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);

                top += gb.Height + 6;
            }

            _applyBadgesBtn = new Button { Text = "Apply", Top = 348, Left = 8, Width = 130, Height = 32 };
            _applyBadgesBtn.Click += (_, _) => ApplyBadges();
            page.Controls.Add(_applyBadgesBtn);

            _revertBadgesBtn = new Button
            {
                Text = "Revert to attach-time values", Top = 348, Left = 148, Width = 220, Height = 32,
            };
            _revertBadgesBtn.Click += (_, _) => RevertBadges();
            page.Controls.Add(_revertBadgesBtn);

            _autoApplyBadgesToggle = new CheckBox
            {
                Text = "Auto-apply badges when player resolves",
                Top = 386, Left = 8, Width = 360, Height = 22,
                Checked = _settings.AutoApplyBadges,
            };
            _autoApplyBadgesToggle.CheckedChanged += (_, _) =>
            {
                _settings.AutoApplyBadges = _autoApplyBadgesToggle.Checked;
                _settings.Save();
            };
            page.Controls.Add(_autoApplyBadgesToggle);

            page.Controls.Add(new Label
            {
                Top = 414, Left = 8, Width = 670, Height = 30,
                Text = "Heads up: badges with [Exclusive with ...] in the name share a slot — turning one off in-game "
                     + "may auto-enable its partner. Save & reload to lock changes in.",
                ForeColor = Color.Firebrick,
            });
        }

        private void BuildRosterTab()
        {
            var page = new TabPage("Roster");
            _tabs.TabPages.Add(page);

            // ── Picker bar: Team + Player dropdowns + Refresh ───────────────
            page.Controls.Add(new Label { Text = "Team:", Top = 12, Left = 8, Width = 46 });
            _rosterTeamCombo = new ComboBox
            {
                Top = 9, Left = 56, Width = 220,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _rosterTeamCombo.SelectedIndexChanged += (_, _) => OnRosterTeamSelected();
            page.Controls.Add(_rosterTeamCombo);

            _rosterRefreshBtn = new Button
            {
                Text = "Refresh", Top = 8, Left = 290, Width = 100, Height = 24,
            };
            _rosterRefreshBtn.Click += (_, _) => RefreshRosterFromGame();
            page.Controls.Add(_rosterRefreshBtn);

            _revertRosterToBaselineBtn = new Button
            {
                Text = "Revert to original", Top = 8, Left = 400, Width = 160, Height = 24,
            };
            _revertRosterToBaselineBtn.Click += (_, _) => RevertRosterToBaseline();
            page.Controls.Add(_revertRosterToBaselineBtn);

            _rosterStatusLabel = new Label
            {
                Top = 38, Left = 8, Width = 672, Height = 18,
                Text = "Waiting for game attach...",
                ForeColor = Color.DimGray,
            };
            page.Controls.Add(_rosterStatusLabel);

            // ── Body: player list (left) + sub-tabs (right) ─────────────────
            _rosterPlayerList = new ListBox
            {
                Top = 60, Left = 8, Width = 180, Height = 560,
                IntegralHeight = false,
            };
            _rosterPlayerList.SelectedIndexChanged += (_, _) => OnRosterPlayerSelected();
            page.Controls.Add(_rosterPlayerList);

            _rosterSubTabs = new TabControl
            {
                Top = 60, Left = 192, Width = 488, Height = 560,
            };
            page.Controls.Add(_rosterSubTabs);

            BuildRosterProfileSubPage();
            BuildRosterRatingsSubPage();
            BuildRosterBadgesSubPage();
            BuildRosterTendenciesSubPage();

            page.Controls.Add(new Label
            {
                Top = 624, Left = 8, Width = 672, Height = 18,
                Text = "Edits write to the in-memory roster table. Use Options → Roster → Save in-game to persist across launches.",
                ForeColor = Color.DimGray,
            });
        }

        private void BuildRosterProfileSubPage()
        {
            var page = new TabPage("Profile");
            _rosterSubTabs.TabPages.Add(page);

            var idBox = new GroupBox
            {
                Text = "Identity", Top = 8, Left = 8, Width = 464, Height = 130,
            };
            page.Controls.Add(idBox);

            idBox.Controls.Add(new Label { Text = "First name:", Top = 28, Left = 8, Width = 80 });
            _rosterFirstNameBox = new TextBox { Top = 25, Left = 90, Width = 150, MaxLength = 19 };
            idBox.Controls.Add(_rosterFirstNameBox);

            idBox.Controls.Add(new Label { Text = "Last name:", Top = 60, Left = 8, Width = 80 });
            _rosterLastNameBox = new TextBox { Top = 57, Left = 90, Width = 150, MaxLength = 17 };
            idBox.Controls.Add(_rosterLastNameBox);

            idBox.Controls.Add(new Label { Text = "Jersey #:", Top = 28, Left = 252, Width = 60 });
            _rosterJerseyBox = new NumericUpDown
            {
                Top = 25, Left = 320, Width = 60,
                Minimum = 0, Maximum = 255, Value = 0,
            };
            idBox.Controls.Add(_rosterJerseyBox);

            idBox.Controls.Add(new Label { Text = "Primary:", Top = 60, Left = 252, Width = 60 });
            _rosterPrimaryPosBox = new ComboBox
            {
                Top = 57, Left = 320, Width = 70,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _rosterPrimaryPosBox.Items.AddRange(PositionNames.Display.Cast<object>().ToArray());
            idBox.Controls.Add(_rosterPrimaryPosBox);

            idBox.Controls.Add(new Label { Text = "Secondary:", Top = 92, Left = 252, Width = 60 });
            _rosterSecondaryPosBox = new ComboBox
            {
                Top = 89, Left = 320, Width = 70,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _rosterSecondaryPosBox.Items.AddRange(PositionNames.Display.Cast<object>().ToArray());
            idBox.Controls.Add(_rosterSecondaryPosBox);

            var bodyBox = new GroupBox
            {
                Text = "Body", Top = 148, Left = 8, Width = 464, Height = 100,
            };
            page.Controls.Add(bodyBox);

            // Static-roster records have one phys-attrs buffer (the in-module
            // one), so we expose a single Height/Wingspan pair here. Apply sets
            // both Height and GameplayHeight in the snapshot to the same value
            // — PlayerProfileCheat.Write picks GameplayHeight when the indirect
            // ptr is module-resident, which it always is for static records.
            bodyBox.Controls.Add(new Label { Text = "Height (cm):", Top = 28, Left = 8, Width = 80 });
            _rosterHeightBox = new NumericUpDown
            {
                Top = 25, Left = 92, Width = 80,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_rosterHeightBox);

            bodyBox.Controls.Add(new Label { Text = "Wingspan:", Top = 28, Left = 184, Width = 70 });
            _rosterWingspanBox = new NumericUpDown
            {
                Top = 25, Left = 258, Width = 80,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_rosterWingspanBox);

            bodyBox.Controls.Add(new Label { Text = "Weight (lbs):", Top = 60, Left = 8, Width = 90 });
            _rosterWeightBox = new NumericUpDown
            {
                Top = 57, Left = 100, Width = 80,
                Minimum = 50, Maximum = 800, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_rosterWeightBox);

            _applyRosterProfileBtn = new Button
            {
                Text = "Apply profile", Top = 260, Left = 8, Width = 130, Height = 32,
            };
            _applyRosterProfileBtn.Click += (_, _) => ApplyRosterProfile();
            page.Controls.Add(_applyRosterProfileBtn);

            _revertRosterProfileBtn = new Button
            {
                Text = "Revert to load-time", Top = 260, Left = 146, Width = 170, Height = 32,
            };
            _revertRosterProfileBtn.Click += (_, _) => RevertRosterProfile();
            page.Controls.Add(_revertRosterProfileBtn);
        }

        private void BuildRosterRatingsSubPage()
        {
            var page = new TabPage("Ratings");
            _rosterSubTabs.TabPages.Add(page);

            page.Controls.Add(new Label { Text = "Override all to:", Top = 12, Left = 8, Width = 100 });
            _rosterRatingOverrideBox = new NumericUpDown
            {
                Top = 8, Left = 115, Width = 70,
                Minimum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MIN,
                Maximum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MAX,
                Value = GameOffsets.PLAYER_STATIC_RATINGS_UI_MAX,
            };
            page.Controls.Add(_rosterRatingOverrideBox);

            _rosterRatingApplyOverrideBtn = new Button
            {
                Text = "Fill all", Top = 7, Left = 195, Width = 80, Height = 24,
            };
            _rosterRatingApplyOverrideBtn.Click += (_, _) =>
            {
                foreach (var box in _rosterRatingBoxes.Values)
                    box.Value = (byte)_rosterRatingOverrideBox.Value;
            };
            page.Controls.Add(_rosterRatingApplyOverrideBtn);

            var scroll = new Panel
            {
                Top = 40, Left = 8, Width = 464, Height = 440,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            // Preserve first-occurrence order so tabs appear in the same
            // order the in-game editor uses (Offense first).
            var groups = StaticRosterRatingsCheat.StaticRatings
                .Select((r, i) => new { r, i })
                .GroupBy(x => x.r.Group)
                .OrderBy(g => g.First().i)
                .Select(g => new { Key = g.Key, Items = g.Select(x => x.r).ToList() })
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox { Text = grp.Key, Top = top, Left = 4, Width = 436 };
                int innerTop = 22;
                foreach (var r in grp.Items)
                {
                    gb.Controls.Add(new Label
                    {
                        Text = r.Name + ":", Top = innerTop + 3, Left = 8, Width = 240,
                    });
                    var num = new NumericUpDown
                    {
                        Top = innerTop, Left = 252, Width = 70,
                        Minimum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MIN,
                        Maximum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MAX,
                        Value = GameOffsets.PLAYER_STATIC_RATINGS_UI_MIN,
                    };
                    _rosterRatingBoxes[r.Name] = num;
                    gb.Controls.Add(num);
                    innerTop += 28;
                }
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);
                top += gb.Height + 6;
            }

            _applyRosterRatingsBtn = new Button
            {
                Text = "Apply ratings", Top = 510, Left = 8, Width = 130, Height = 32,
            };
            _applyRosterRatingsBtn.Click += (_, _) => ApplyRosterRatings();
            page.Controls.Add(_applyRosterRatingsBtn);

            _revertRosterRatingsBtn = new Button
            {
                Text = "Revert to load-time", Top = 510, Left = 146, Width = 170, Height = 32,
            };
            _revertRosterRatingsBtn.Click += (_, _) => RevertRosterRatings();
            page.Controls.Add(_revertRosterRatingsBtn);
        }

        private void BuildRosterBadgesSubPage()
        {
            var page = new TabPage("Badges");
            _rosterSubTabs.TabPages.Add(page);

            var scroll = new Panel
            {
                Top = 8, Left = 8, Width = 464, Height = 470,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            string[] tierItems = { "OFF", "Bronze", "Silver", "Gold" };
            string[] toggleItems = { "OFF", "ON" };

            var groups = BadgesCheat.Badges
                .GroupBy(b => b.Group)
                .OrderBy(g => g.Key)
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox { Text = grp.Key, Top = top, Left = 4, Width = 436 };
                int innerTop = 22;
                foreach (var b in grp)
                {
                    gb.Controls.Add(new Label
                    {
                        Text = b.Name + ":", Top = innerTop + 3, Left = 8, Width = 260,
                        AutoEllipsis = true,
                    });
                    var combo = new ComboBox
                    {
                        Top = innerTop, Left = 272, Width = 90,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                    };
                    combo.Items.AddRange(b.BitLength == 1 ? toggleItems : tierItems);
                    combo.SelectedIndex = 0;
                    _rosterBadgeBoxes[b.Name] = combo;
                    gb.Controls.Add(combo);
                    innerTop += 28;
                }
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);
                top += gb.Height + 6;
            }

            _applyRosterBadgesBtn = new Button
            {
                Text = "Apply badges", Top = 510, Left = 8, Width = 130, Height = 32,
            };
            _applyRosterBadgesBtn.Click += (_, _) => ApplyRosterBadges();
            page.Controls.Add(_applyRosterBadgesBtn);

            _revertRosterBadgesBtn = new Button
            {
                Text = "Revert to load-time", Top = 510, Left = 146, Width = 170, Height = 32,
            };
            _revertRosterBadgesBtn.Click += (_, _) => RevertRosterBadges();
            page.Controls.Add(_revertRosterBadgesBtn);

            _maxAllRosterBadgesBtn = new Button
            {
                Text = "Max all", Top = 510, Left = 324, Width = 130, Height = 32,
            };
            _maxAllRosterBadgesBtn.Click += (_, _) => MaxAllRosterBadges();
            page.Controls.Add(_maxAllRosterBadgesBtn);
        }

        private void BuildRosterTendenciesSubPage()
        {
            var page = new TabPage("Tendencies");
            _rosterSubTabs.TabPages.Add(page);

            page.Controls.Add(new Label { Text = "Override all to:", Top = 12, Left = 8, Width = 100 });
            _rosterTendencyOverrideBox = new NumericUpDown
            {
                Top = 8, Left = 115, Width = 70, Minimum = 0, Maximum = 100, Value = 50,
            };
            page.Controls.Add(_rosterTendencyOverrideBox);

            _rosterTendencyApplyOverrideBtn = new Button
            {
                Text = "Fill all", Top = 7, Left = 195, Width = 80, Height = 24,
            };
            _rosterTendencyApplyOverrideBtn.Click += (_, _) =>
            {
                foreach (var box in _rosterTendencyBoxes.Values)
                    box.Value = (byte)_rosterTendencyOverrideBox.Value;
            };
            page.Controls.Add(_rosterTendencyApplyOverrideBtn);

            var scroll = new Panel
            {
                Top = 40, Left = 8, Width = 464, Height = 440,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            // Preserve first-occurrence order so the in-editor tab order
            // (Jump Shooting first) is mirrored here instead of alphabetical.
            var groups = TendenciesCheat.Tendencies
                .Select((t, i) => new { t, i })
                .GroupBy(x => x.t.Group)
                .OrderBy(g => g.First().i)
                .Select(g => new { Key = g.Key, Items = g.Select(x => x.t).ToList() })
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox { Text = grp.Key, Top = top, Left = 4, Width = 436 };
                int innerTop = 22;
                foreach (var t in grp.Items)
                {
                    gb.Controls.Add(new Label
                    {
                        Text = t.Name + ":", Top = innerTop + 3, Left = 8, Width = 240,
                        AutoEllipsis = true,
                    });
                    var num = new NumericUpDown
                    {
                        Top = innerTop, Left = 252, Width = 70,
                        Minimum = 0, Maximum = 100, Value = 0,
                    };
                    _rosterTendencyBoxes[t.Name] = num;
                    gb.Controls.Add(num);
                    innerTop += 28;
                }
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);
                top += gb.Height + 6;
            }

            _applyRosterTendenciesBtn = new Button
            {
                Text = "Apply tendencies", Top = 510, Left = 8, Width = 130, Height = 32,
            };
            _applyRosterTendenciesBtn.Click += (_, _) => ApplyRosterTendencies();
            page.Controls.Add(_applyRosterTendenciesBtn);

            _revertRosterTendenciesBtn = new Button
            {
                Text = "Revert to load-time", Top = 510, Left = 146, Width = 170, Height = 32,
            };
            _revertRosterTendenciesBtn.Click += (_, _) => RevertRosterTendencies();
            page.Controls.Add(_revertRosterTendenciesBtn);
        }

        private void BuildPlayerListTab()
        {
            var page = new TabPage("Players");
            _tabs.TabPages.Add(page);

            // ── Top: search box + refresh button ───────────────────────────
            page.Controls.Add(new Label { Text = "Search:", Top = 12, Left = 8, Width = 50 });
            _playerListSearchBox = new TextBox
            {
                Top = 9, Left = 60, Width = 220,
            };
            _playerListSearchBox.TextChanged += (_, _) => OnPlayerListSearchChanged();
            page.Controls.Add(_playerListSearchBox);

            _playerListRefreshBtn = new Button
            {
                Text = "Refresh", Top = 8, Left = 290, Width = 100, Height = 24,
            };
            _playerListRefreshBtn.Click += (_, _) => RefreshPlayerListFromGame();
            page.Controls.Add(_playerListRefreshBtn);

            _revertPlayerListToBaselineBtn = new Button
            {
                Text = "Revert to original", Top = 8, Left = 400, Width = 160, Height = 24,
            };
            _revertPlayerListToBaselineBtn.Click += (_, _) => RevertPlayerListToBaseline();
            page.Controls.Add(_revertPlayerListToBaselineBtn);

            _playerListStatusLabel = new Label
            {
                Top = 38, Left = 8, Width = 672, Height = 18,
                Text = "Waiting for game attach...",
                ForeColor = Color.DimGray,
            };
            page.Controls.Add(_playerListStatusLabel);

            // ── Body: list (left) + sub-tabs (right) ──────────────────────
            _playerListListBox = new ListBox
            {
                Top = 60, Left = 8, Width = 180, Height = 560,
                IntegralHeight = false,
            };
            _playerListListBox.SelectedIndexChanged += (_, _) => OnPlayerListSelected();
            page.Controls.Add(_playerListListBox);

            _playerListSubTabs = new TabControl
            {
                Top = 60, Left = 192, Width = 488, Height = 560,
            };
            page.Controls.Add(_playerListSubTabs);

            BuildPlayerListProfileSubPage();
            BuildPlayerListRatingsSubPage();
            BuildPlayerListBadgesSubPage();
            BuildPlayerListTendenciesSubPage();

            page.Controls.Add(new Label
            {
                Top = 624, Left = 8, Width = 672, Height = 18,
                Text = "Your MyPlayer isn't here (he's heap-resident — edit via the Profile/Ratings/Badges tabs at top).",
                ForeColor = Color.DimGray,
            });
        }

        private void BuildPlayerListProfileSubPage()
        {
            var page = new TabPage("Profile");
            _playerListSubTabs.TabPages.Add(page);

            var idBox = new GroupBox
            {
                Text = "Identity", Top = 8, Left = 8, Width = 464, Height = 130,
            };
            page.Controls.Add(idBox);

            idBox.Controls.Add(new Label { Text = "First name:", Top = 28, Left = 8, Width = 80 });
            _playerListFirstNameBox = new TextBox { Top = 25, Left = 90, Width = 150, MaxLength = 19 };
            idBox.Controls.Add(_playerListFirstNameBox);

            idBox.Controls.Add(new Label { Text = "Last name:", Top = 60, Left = 8, Width = 80 });
            _playerListLastNameBox = new TextBox { Top = 57, Left = 90, Width = 150, MaxLength = 17 };
            idBox.Controls.Add(_playerListLastNameBox);

            idBox.Controls.Add(new Label { Text = "Jersey #:", Top = 28, Left = 252, Width = 60 });
            _playerListJerseyBox = new NumericUpDown
            {
                Top = 25, Left = 320, Width = 60,
                Minimum = 0, Maximum = 255, Value = 0,
            };
            idBox.Controls.Add(_playerListJerseyBox);

            idBox.Controls.Add(new Label { Text = "Primary:", Top = 60, Left = 252, Width = 60 });
            _playerListPrimaryPosBox = new ComboBox
            {
                Top = 57, Left = 320, Width = 70,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _playerListPrimaryPosBox.Items.AddRange(PositionNames.Display.Cast<object>().ToArray());
            idBox.Controls.Add(_playerListPrimaryPosBox);

            idBox.Controls.Add(new Label { Text = "Secondary:", Top = 92, Left = 252, Width = 60 });
            _playerListSecondaryPosBox = new ComboBox
            {
                Top = 89, Left = 320, Width = 70,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };
            _playerListSecondaryPosBox.Items.AddRange(PositionNames.Display.Cast<object>().ToArray());
            idBox.Controls.Add(_playerListSecondaryPosBox);

            var bodyBox = new GroupBox
            {
                Text = "Body", Top = 148, Left = 8, Width = 464, Height = 100,
            };
            page.Controls.Add(bodyBox);

            bodyBox.Controls.Add(new Label { Text = "Height (cm):", Top = 28, Left = 8, Width = 80 });
            _playerListHeightBox = new NumericUpDown
            {
                Top = 25, Left = 92, Width = 80,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_playerListHeightBox);

            bodyBox.Controls.Add(new Label { Text = "Wingspan:", Top = 28, Left = 184, Width = 70 });
            _playerListWingspanBox = new NumericUpDown
            {
                Top = 25, Left = 258, Width = 80,
                Minimum = 50, Maximum = 350, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_playerListWingspanBox);

            bodyBox.Controls.Add(new Label { Text = "Weight (lbs):", Top = 60, Left = 8, Width = 90 });
            _playerListWeightBox = new NumericUpDown
            {
                Top = 57, Left = 100, Width = 80,
                Minimum = 50, Maximum = 800, DecimalPlaces = 2, Increment = 1m,
            };
            bodyBox.Controls.Add(_playerListWeightBox);

            _applyPlayerListProfileBtn = new Button
            {
                Text = "Apply profile", Top = 260, Left = 8, Width = 130, Height = 32,
            };
            _applyPlayerListProfileBtn.Click += (_, _) => ApplyPlayerListProfile();
            page.Controls.Add(_applyPlayerListProfileBtn);

            _revertPlayerListProfileBtn = new Button
            {
                Text = "Revert to load-time", Top = 260, Left = 146, Width = 170, Height = 32,
            };
            _revertPlayerListProfileBtn.Click += (_, _) => RevertPlayerListProfile();
            page.Controls.Add(_revertPlayerListProfileBtn);
        }

        private void BuildPlayerListRatingsSubPage()
        {
            var page = new TabPage("Ratings");
            _playerListSubTabs.TabPages.Add(page);

            page.Controls.Add(new Label { Text = "Override all to:", Top = 12, Left = 8, Width = 100 });
            _playerListRatingOverrideBox = new NumericUpDown
            {
                Top = 8, Left = 115, Width = 70,
                Minimum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MIN,
                Maximum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MAX,
                Value = GameOffsets.PLAYER_STATIC_RATINGS_UI_MAX,
            };
            page.Controls.Add(_playerListRatingOverrideBox);

            _playerListRatingApplyOverrideBtn = new Button
            {
                Text = "Fill all", Top = 7, Left = 195, Width = 80, Height = 24,
            };
            _playerListRatingApplyOverrideBtn.Click += (_, _) =>
            {
                foreach (var box in _playerListRatingBoxes.Values)
                    box.Value = (byte)_playerListRatingOverrideBox.Value;
            };
            page.Controls.Add(_playerListRatingApplyOverrideBtn);

            var scroll = new Panel
            {
                Top = 40, Left = 8, Width = 464, Height = 440,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            var groups = StaticRosterRatingsCheat.StaticRatings
                .Select((r, i) => new { r, i })
                .GroupBy(x => x.r.Group)
                .OrderBy(g => g.First().i)
                .Select(g => new { Key = g.Key, Items = g.Select(x => x.r).ToList() })
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox { Text = grp.Key, Top = top, Left = 4, Width = 436 };
                int innerTop = 22;
                foreach (var r in grp.Items)
                {
                    gb.Controls.Add(new Label
                    {
                        Text = r.Name + ":", Top = innerTop + 3, Left = 8, Width = 240,
                    });
                    var num = new NumericUpDown
                    {
                        Top = innerTop, Left = 252, Width = 70,
                        Minimum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MIN,
                        Maximum = GameOffsets.PLAYER_STATIC_RATINGS_UI_MAX,
                        Value = GameOffsets.PLAYER_STATIC_RATINGS_UI_MIN,
                    };
                    _playerListRatingBoxes[r.Name] = num;
                    gb.Controls.Add(num);
                    innerTop += 28;
                }
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);
                top += gb.Height + 6;
            }

            _applyPlayerListRatingsBtn = new Button
            {
                Text = "Apply ratings", Top = 510, Left = 8, Width = 130, Height = 32,
            };
            _applyPlayerListRatingsBtn.Click += (_, _) => ApplyPlayerListRatings();
            page.Controls.Add(_applyPlayerListRatingsBtn);

            _revertPlayerListRatingsBtn = new Button
            {
                Text = "Revert to load-time", Top = 510, Left = 146, Width = 170, Height = 32,
            };
            _revertPlayerListRatingsBtn.Click += (_, _) => RevertPlayerListRatings();
            page.Controls.Add(_revertPlayerListRatingsBtn);
        }

        private void BuildPlayerListBadgesSubPage()
        {
            var page = new TabPage("Badges");
            _playerListSubTabs.TabPages.Add(page);

            var scroll = new Panel
            {
                Top = 8, Left = 8, Width = 464, Height = 470,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            string[] tierItems = { "OFF", "Bronze", "Silver", "Gold" };
            string[] toggleItems = { "OFF", "ON" };

            var groups = BadgesCheat.Badges
                .GroupBy(b => b.Group)
                .OrderBy(g => g.Key)
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox { Text = grp.Key, Top = top, Left = 4, Width = 436 };
                int innerTop = 22;
                foreach (var b in grp)
                {
                    gb.Controls.Add(new Label
                    {
                        Text = b.Name + ":", Top = innerTop + 3, Left = 8, Width = 260,
                        AutoEllipsis = true,
                    });
                    var combo = new ComboBox
                    {
                        Top = innerTop, Left = 272, Width = 90,
                        DropDownStyle = ComboBoxStyle.DropDownList,
                    };
                    combo.Items.AddRange(b.BitLength == 1 ? toggleItems : tierItems);
                    combo.SelectedIndex = 0;
                    _playerListBadgeBoxes[b.Name] = combo;
                    gb.Controls.Add(combo);
                    innerTop += 28;
                }
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);
                top += gb.Height + 6;
            }

            _applyPlayerListBadgesBtn = new Button
            {
                Text = "Apply badges", Top = 510, Left = 8, Width = 130, Height = 32,
            };
            _applyPlayerListBadgesBtn.Click += (_, _) => ApplyPlayerListBadges();
            page.Controls.Add(_applyPlayerListBadgesBtn);

            _revertPlayerListBadgesBtn = new Button
            {
                Text = "Revert to load-time", Top = 510, Left = 146, Width = 170, Height = 32,
            };
            _revertPlayerListBadgesBtn.Click += (_, _) => RevertPlayerListBadges();
            page.Controls.Add(_revertPlayerListBadgesBtn);

            _maxAllPlayerListBadgesBtn = new Button
            {
                Text = "Max all", Top = 510, Left = 324, Width = 130, Height = 32,
            };
            _maxAllPlayerListBadgesBtn.Click += (_, _) => MaxAllPlayerListBadges();
            page.Controls.Add(_maxAllPlayerListBadgesBtn);
        }

        private void BuildPlayerListTendenciesSubPage()
        {
            var page = new TabPage("Tendencies");
            _playerListSubTabs.TabPages.Add(page);

            page.Controls.Add(new Label { Text = "Override all to:", Top = 12, Left = 8, Width = 100 });
            _playerListTendencyOverrideBox = new NumericUpDown
            {
                Top = 8, Left = 115, Width = 70, Minimum = 0, Maximum = 100, Value = 50,
            };
            page.Controls.Add(_playerListTendencyOverrideBox);

            _playerListTendencyApplyOverrideBtn = new Button
            {
                Text = "Fill all", Top = 7, Left = 195, Width = 80, Height = 24,
            };
            _playerListTendencyApplyOverrideBtn.Click += (_, _) =>
            {
                foreach (var box in _playerListTendencyBoxes.Values)
                    box.Value = (byte)_playerListTendencyOverrideBox.Value;
            };
            page.Controls.Add(_playerListTendencyApplyOverrideBtn);

            var scroll = new Panel
            {
                Top = 40, Left = 8, Width = 464, Height = 440,
                AutoScroll = true,
                BorderStyle = BorderStyle.FixedSingle,
            };
            page.Controls.Add(scroll);

            var groups = TendenciesCheat.Tendencies
                .Select((t, i) => new { t, i })
                .GroupBy(x => x.t.Group)
                .OrderBy(g => g.First().i)
                .Select(g => new { Key = g.Key, Items = g.Select(x => x.t).ToList() })
                .ToList();

            int top = 4;
            foreach (var grp in groups)
            {
                var gb = new GroupBox { Text = grp.Key, Top = top, Left = 4, Width = 436 };
                int innerTop = 22;
                foreach (var t in grp.Items)
                {
                    gb.Controls.Add(new Label
                    {
                        Text = t.Name + ":", Top = innerTop + 3, Left = 8, Width = 240,
                        AutoEllipsis = true,
                    });
                    var num = new NumericUpDown
                    {
                        Top = innerTop, Left = 252, Width = 70,
                        Minimum = 0, Maximum = 100, Value = 0,
                    };
                    _playerListTendencyBoxes[t.Name] = num;
                    gb.Controls.Add(num);
                    innerTop += 28;
                }
                gb.Height = innerTop + 8;
                scroll.Controls.Add(gb);
                top += gb.Height + 6;
            }

            _applyPlayerListTendenciesBtn = new Button
            {
                Text = "Apply tendencies", Top = 510, Left = 8, Width = 130, Height = 32,
            };
            _applyPlayerListTendenciesBtn.Click += (_, _) => ApplyPlayerListTendencies();
            page.Controls.Add(_applyPlayerListTendenciesBtn);

            _revertPlayerListTendenciesBtn = new Button
            {
                Text = "Revert to load-time", Top = 510, Left = 146, Width = 170, Height = 32,
            };
            _revertPlayerListTendenciesBtn.Click += (_, _) => RevertPlayerListTendencies();
            page.Controls.Add(_revertPlayerListTendenciesBtn);
        }

        private void BuildLogTab()
        {
            var page = new TabPage("Log");
            _tabs.TabPages.Add(page);

            _logBox = new TextBox
            {
                Top = 8, Left = 8, Width = 672, Height = 590,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.75f),
            };
            page.Controls.Add(_logBox);

            _clearLogBtn = new Button
            {
                Text = "Clear log", Top = 608, Left = 8, Width = 100, Height = 28,
            };
            _clearLogBtn.Click += (_, _) => _logBox.Clear();
            page.Controls.Add(_clearLogBtn);

            _copyLogBtn = new Button
            {
                Text = "Copy last 20", Top = 608, Left = 116, Width = 110, Height = 28,
            };
            _copyLogBtn.Click += (_, _) => CopyRecentLog(20);
            page.Controls.Add(_copyLogBtn);
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

                // Map the static roster table — runs independently of the
                // MyPlayer hook, so even if the hook fails (e.g. a CE script is
                // holding the bytes) the Roster tab still lights up.
                InitializeRosterFromGame(session);
                SetRosterTeamControlsEnabled(_rosterResolver.Initialized);

                // Players tab piggybacks on the resolver — once teams are mapped,
                // build the flat searchable label cache for the Players tab.
                if (_rosterResolver.Initialized)
                {
                    InitializePlayerListFromGame(session);
                    SetPlayerListSearchControlsEnabled(true);
                    InitializeBaselineStore(session);
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
            _playerCopies = Array.Empty<IntPtr>();
            _liveMaxLabel.Text = "Live: —";
            _liveMinLabel.Text = "Live: —";
            _liveProfileLabel.Text = "Live: —";

            _positionClampCheat.ResetCapturedState();
            _profileCheat.ResetCapturedState();
            _ratingsCheat.ResetCapturedState();
            _badgesCheat.ResetCapturedState();

            _rosterResolver.Reset();
            _rosterProfileCheat.ResetCapturedState();
            _rosterRatingsCheat.ResetCapturedState();
            _rosterBadgesCheat.ResetCapturedState();
            _rosterTendenciesCheat.ResetCapturedState();
            _rosterSelectedIndex = -1;
            _rosterSuppressEvents = true;
            try
            {
                _rosterTeamCombo.Items.Clear();
                _rosterPlayerList.Items.Clear();
            }
            finally { _rosterSuppressEvents = false; }
            _rosterStatusLabel.Text = "Waiting for game attach...";
            _rosterStatusLabel.ForeColor = Color.DimGray;

            _playerListProfileCheat.ResetCapturedState();
            _playerListRatingsCheat.ResetCapturedState();
            _playerListBadgesCheat.ResetCapturedState();
            _playerListTendenciesCheat.ResetCapturedState();
            _playerListSelectedRosterIndex = -1;
            _playerListAllLabels = Array.Empty<string>();
            _playerListVisibleIndices = Array.Empty<int>();

            _baselineStore.Reset();
            _playerListSuppressEvents = true;
            try
            {
                _playerListSearchBox.Text = string.Empty;
                _playerListListBox.Items.Clear();
            }
            finally { _playerListSuppressEvents = false; }
            _playerListStatusLabel.Text = "Waiting for game attach...";
            _playerListStatusLabel.ForeColor = Color.DimGray;

            SetGlobalControlsEnabled(false);
            SetPlayerControlsEnabled(false);
            SetRosterTeamControlsEnabled(false);
            SetRosterPlayerControlsEnabled(false);
            SetPlayerListSearchControlsEnabled(false);
            SetPlayerListPlayerControlsEnabled(false);
            UpdateStatusLabel();
        }

        private void TryResolvePlayer()
        {
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                IntPtr p = _resolver.ReadPlayerPointer(session);
                if (p == IntPtr.Zero) return;

                bool ptrChanged = _lastPlayerBase != p;
                _lastPlayerBase = p;

                try
                {
                    var profile = _profileCheat.Probe(session, p);

                    // Discover all parallel copies of this MyPlayer struct so
                    // writes survive save/load (game saves from a different
                    // copy than the live in-game one). Re-scan only when the
                    // active pointer moves — otherwise the list is stable.
                    if (ptrChanged || _playerCopies.Count == 0)
                    {
                        _playerCopies = PlayerStructScanner.FindCopies(session, p);
                        Log($"Scanned for player copies: {_playerCopies.Count} found.");
                    }

                    PopulateProfileInputs(profile);
                    _liveProfileLabel.Text = $"Live: {profile.FirstName} {profile.LastName} · "
                        + $"{PositionNames.Format(profile.PrimaryPosition)} #{profile.Jersey} · "
                        + $"{profile.Height:F2} cm / {profile.Wingspan:F2} cm wingspan / {profile.Weight:F2} lbs";

                    var ratings = _ratingsCheat.Probe(session, p);
                    PopulateRatingInputs(ratings);

                    var badges = _badgesCheat.Probe(session, p);
                    PopulateBadgeInputs(badges);

                    _profileLoaded = true;
                    SetPlayerControlsEnabled(true);
                    UpdateStatusLabel();
                    Log($"Player resolved: {profile.FirstName} {profile.LastName} (struct @ 0x{p.ToInt64():X}).");

                    // Auto-apply if persisted settings exist. Writes are
                    // fanned across every discovered struct copy so they
                    // survive save/load.
                    if (_settings.AutoApplyProfile && SettingsHasProfile())
                    {
                        var desired = MergeProfileFromSettings(profile);
                        int ok = ApplyToCopies(c => _profileCheat.Apply(session, c, desired));
                        Log($"Auto-applied saved profile to {ok}/{_playerCopies.Count} copies.");
                    }
                    if (_settings.AutoApplyRatings && _settings.RatingOverrides is { Count: > 0 })
                    {
                        var desired = new Dictionary<string, byte>(ratings);
                        foreach (var kv in _settings.RatingOverrides!) desired[kv.Key] = kv.Value;
                        int ok = ApplyToCopies(c => _ratingsCheat.Apply(session, c, desired));
                        Log($"Auto-applied saved rating overrides to {ok}/{_playerCopies.Count} copies.");
                    }
                    if (_settings.AutoApplyBadges && _settings.BadgeOverrides is { Count: > 0 })
                    {
                        var desired = new Dictionary<string, byte>(badges);
                        foreach (var kv in _settings.BadgeOverrides!) desired[kv.Key] = kv.Value;
                        int ok = ApplyToCopies(c => _badgesCheat.Apply(session, c, desired));
                        Log($"Auto-applied saved badge overrides to {ok}/{_playerCopies.Count} copies.");
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
                    int okProfile = ApplyToCopies(c => _profileCheat.Apply(session, c, desired));
                    PersistProfileToSettings(desired);
                    Log($"Profile written ({okProfile}/{_playerCopies.Count} copies): {desired.FirstName} {desired.LastName} · "
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
                    int okRevert = ApplyToCopies(c => _profileCheat.Revert(session, c));
                    if (_profileCheat.Original is { } original) PopulateProfileInputs(original);
                    Log($"Profile reverted on {okRevert}/{_playerCopies.Count} copies.");
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
                    int okRatings = ApplyToCopies(c => _ratingsCheat.Apply(session, c, desired));

                    // Persist only the entries that differ from "Original" (so we don't
                    // pin every rating across sessions).
                    var overrides = _ratingsCheat.Original is { } orig
                        ? desired.Where(kv => !orig.TryGetValue(kv.Key, out byte ov) || ov != kv.Value)
                                 .ToDictionary(kv => kv.Key, kv => kv.Value)
                        : desired;
                    _settings.RatingOverrides = overrides.Count > 0 ? overrides : null;
                    _settings.Save();
                    Log($"Ratings written ({overrides.Count} overrides, {okRatings}/{_playerCopies.Count} copies). Reload the save to lock in.");
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
                    int okRatingsRevert = ApplyToCopies(c => _ratingsCheat.Revert(session, c));
                    if (_ratingsCheat.Original is { } original) PopulateRatingInputs(original);
                    Log($"Ratings reverted on {okRatingsRevert}/{_playerCopies.Count} copies.");
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

        private void ApplyBadges()
        {
            if (_lastPlayerBase == IntPtr.Zero)
            {
                Log("No player resolved yet — can't apply badges.");
                return;
            }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadBadgesFromInputs();
                    int okBadges = ApplyToCopies(c => _badgesCheat.Apply(session, c, desired));

                    var overrides = _badgesCheat.Original is { } orig
                        ? desired.Where(kv => !orig.TryGetValue(kv.Key, out byte ov) || ov != kv.Value)
                                 .ToDictionary(kv => kv.Key, kv => kv.Value)
                        : desired;
                    _settings.BadgeOverrides = overrides.Count > 0 ? overrides : null;
                    _settings.Save();
                    Log($"Badges written ({overrides.Count} overrides, {okBadges}/{_playerCopies.Count} copies). Reload the save to lock in.");
                }
                catch (Exception ex)
                {
                    Log("Badges apply failed: " + ex.Message);
                }
            }
        }

        private void RevertBadges()
        {
            if (_lastPlayerBase == IntPtr.Zero) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    int okRevert = ApplyToCopies(c => _badgesCheat.Revert(session, c));
                    if (_badgesCheat.Original is { } original) PopulateBadgeInputs(original);
                    Log($"Badges reverted on {okRevert}/{_playerCopies.Count} copies.");
                }
                catch (Exception ex)
                {
                    Log("Badges revert failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Runs <paramref name="action"/> against every discovered player copy,
        /// swallowing per-copy exceptions so one bad address doesn't abort
        /// the rest. Returns the number of copies the action ran cleanly on.
        ///
        /// The scanner is best-effort by design — a stale or memory-mapped
        /// region can occasionally pass the heuristic checks and survive into
        /// <see cref="_playerCopies"/>. Treating each copy independently means
        /// the writes that matter (the 2-3 real MyPlayer copies) still happen
        /// even if a phantom entry has an unmapped address.
        /// </summary>
        private int ApplyToCopies(Action<IntPtr> action)
        {
            int ok = 0;
            foreach (var copy in _playerCopies)
            {
                try
                {
                    action(copy);
                    ok++;
                }
                catch (Exception ex)
                {
                    Log($"  Skipped copy 0x{copy.ToInt64():X}: {ex.Message}");
                }
            }
            return ok;
        }

        // ─── Roster tab actions ─────────────────────────────────────────────

        private void InitializeRosterFromGame(ProcessSession session)
        {
            try
            {
                _rosterResolver.Initialize(session);
                PopulateRosterTeamDropdown();
                _rosterStatusLabel.Text =
                    $"Roster: {_rosterResolver.PlayerCount} players across {_rosterResolver.Teams.Count} teams. "
                    + "Pick a team to load its players.";
                _rosterStatusLabel.ForeColor = Color.DarkGreen;
                Log($"Roster table mapped: base @ 0x{_rosterResolver.ArrayBase.ToInt64():X} "
                    + $"(+0x{(_rosterResolver.ArrayBase.ToInt64() - session.BaseAddress.ToInt64()):X}), "
                    + $"{_rosterResolver.PlayerCount} players, {_rosterResolver.Teams.Count} teams.");
            }
            catch (Exception ex)
            {
                _rosterStatusLabel.Text = "Roster: failed to map table. " + ex.Message;
                _rosterStatusLabel.ForeColor = Color.Firebrick;
                Log("Roster init failed: " + ex.Message);
            }
        }

        private void RefreshRosterFromGame()
        {
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                _rosterResolver.Reset();
                _rosterProfileCheat.ResetCapturedState();
                _rosterRatingsCheat.ResetCapturedState();
                _rosterBadgesCheat.ResetCapturedState();
                _rosterTendenciesCheat.ResetCapturedState();
                _rosterSelectedIndex = -1;
                InitializeRosterFromGame(session);
            }
        }

        private void PopulateRosterTeamDropdown()
        {
            _rosterSuppressEvents = true;
            try
            {
                _rosterTeamCombo.Items.Clear();
                foreach (var team in _rosterResolver.Teams)
                    _rosterTeamCombo.Items.Add(team.DisplayName);
                _rosterPlayerList.Items.Clear();
                if (_rosterTeamCombo.Items.Count > 0)
                    _rosterTeamCombo.SelectedIndex = 0;
            }
            finally
            {
                _rosterSuppressEvents = false;
            }

            OnRosterTeamSelected();
        }

        private void OnRosterTeamSelected()
        {
            if (_rosterSuppressEvents) return;
            if (!_rosterResolver.Initialized) return;
            int teamIdx = _rosterTeamCombo.SelectedIndex;
            if (teamIdx < 0 || teamIdx >= _rosterResolver.Teams.Count) return;

            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                var team = _rosterResolver.Teams[teamIdx];
                _rosterSuppressEvents = true;
                try
                {
                    _rosterPlayerList.Items.Clear();
                    for (int i = 0; i < team.PlayerCount; i++)
                    {
                        int rosterIndex = team.FirstRosterIndex + i;
                        _rosterPlayerList.Items.Add(_rosterResolver.FormatPlayerLabel(session, rosterIndex));
                    }
                    if (_rosterPlayerList.Items.Count > 0)
                        _rosterPlayerList.SelectedIndex = 0;
                }
                finally
                {
                    _rosterSuppressEvents = false;
                }

                OnRosterPlayerSelected();
            }
        }

        private void OnRosterPlayerSelected()
        {
            if (_rosterSuppressEvents) return;
            if (!_rosterResolver.Initialized) return;
            int teamIdx = _rosterTeamCombo.SelectedIndex;
            int slot = _rosterPlayerList.SelectedIndex;
            if (teamIdx < 0 || slot < 0) return;
            var team = _rosterResolver.Teams[teamIdx];
            int rosterIndex = team.FirstRosterIndex + slot;
            if (rosterIndex < 0 || rosterIndex >= _rosterResolver.PlayerCount) return;

            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    // Switching player ⇒ drop captured originals so Revert
                    // restores the new player's load-time values, not the old one's.
                    _rosterProfileCheat.ResetCapturedState();
                    _rosterRatingsCheat.ResetCapturedState();
                    _rosterBadgesCheat.ResetCapturedState();
                    _rosterTendenciesCheat.ResetCapturedState();

                    IntPtr playerBase = _rosterResolver.GetPlayer(rosterIndex);
                    var profile = _rosterProfileCheat.Probe(session, playerBase);
                    var ratings = _rosterRatingsCheat.Probe(session, playerBase);
                    var badges = _rosterBadgesCheat.Probe(session, playerBase);
                    var tendencies = _rosterTendenciesCheat.Probe(session, playerBase);

                    PopulateRosterProfileInputs(profile);
                    PopulateRosterRatingInputs(ratings);
                    PopulateRosterBadgeInputs(badges);
                    PopulateRosterTendencyInputs(tendencies);

                    _rosterSelectedIndex = rosterIndex;
                    SetRosterPlayerControlsEnabled(true);
                    _rosterStatusLabel.Text =
                        $"Loaded: {profile.FirstName} {profile.LastName} · "
                        + $"{PositionNames.Format(profile.PrimaryPosition)} #{profile.Jersey} · "
                        + $"{profile.Height:F2} cm @ 0x{playerBase.ToInt64():X}";
                    _rosterStatusLabel.ForeColor = Color.DarkSlateBlue;
                }
                catch (Exception ex)
                {
                    Log("Roster player load failed: " + ex.Message);
                    _rosterStatusLabel.Text = "Failed to load player: " + ex.Message;
                    _rosterStatusLabel.ForeColor = Color.Firebrick;
                    SetRosterPlayerControlsEnabled(false);
                }
            }
        }

        private void ApplyRosterProfile()
        {
            if (_rosterSelectedIndex < 0) { Log("No roster player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadRosterProfileFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterProfileCheat.Apply(session, playerBase, desired);

                    // Refresh combo label since the name may have changed.
                    var live = _rosterProfileCheat.Read(session, playerBase);
                    RefreshRosterPlayerLabel(live);

                    Log($"Roster profile written for index {_rosterSelectedIndex}: "
                        + $"{desired.FirstName} {desired.LastName} · "
                        + $"{PositionNames.Format(desired.PrimaryPosition)}/{PositionNames.Format(desired.SecondaryPosition)} · "
                        + $"#{desired.Jersey} · {desired.Height:F2}cm / {desired.Wingspan:F2}cm wing / {desired.Weight:F2}lbs.");
                }
                catch (Exception ex)
                {
                    Log("Roster profile apply failed: " + ex.Message);
                }
            }
        }

        private void RevertRosterProfile()
        {
            if (_rosterSelectedIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterProfileCheat.Revert(session, playerBase);
                    if (_rosterProfileCheat.Original is { } original)
                    {
                        PopulateRosterProfileInputs(original);
                        RefreshRosterPlayerLabel(original);
                    }
                    Log($"Roster profile reverted for index {_rosterSelectedIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Roster profile revert failed: " + ex.Message);
                }
            }
        }

        private void ApplyRosterRatings()
        {
            if (_rosterSelectedIndex < 0) { Log("No roster player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadRosterRatingsFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterRatingsCheat.Apply(session, playerBase, desired);
                    Log($"Roster ratings written for index {_rosterSelectedIndex} ({desired.Count} values).");
                }
                catch (Exception ex)
                {
                    Log("Roster ratings apply failed: " + ex.Message);
                }
            }
        }

        private void RevertRosterRatings()
        {
            if (_rosterSelectedIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterRatingsCheat.Revert(session, playerBase);
                    if (_rosterRatingsCheat.Original is { } original)
                        PopulateRosterRatingInputs(original);
                    Log($"Roster ratings reverted for index {_rosterSelectedIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Roster ratings revert failed: " + ex.Message);
                }
            }
        }

        private void ApplyRosterBadges()
        {
            if (_rosterSelectedIndex < 0) { Log("No roster player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadRosterBadgesFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterBadgesCheat.Apply(session, playerBase, desired);
                    Log($"Roster badges written for index {_rosterSelectedIndex} ({desired.Count} values).");
                }
                catch (Exception ex)
                {
                    Log("Roster badges apply failed: " + ex.Message);
                }
            }
        }

        private void RevertRosterBadges()
        {
            if (_rosterSelectedIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterBadgesCheat.Revert(session, playerBase);
                    if (_rosterBadgesCheat.Original is { } original)
                        PopulateRosterBadgeInputs(original);
                    Log($"Roster badges reverted for index {_rosterSelectedIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Roster badges revert failed: " + ex.Message);
                }
            }
        }

        /// <summary>Fills every Roster-tab badge combo to its max tier. User applies separately.</summary>
        private void MaxAllRosterBadges()
        {
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_rosterBadgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                int maxIndex = Math.Min((int)BadgesCheat.MaxTierFor(b), combo.Items.Count - 1);
                combo.SelectedIndex = maxIndex;
            }
        }

        private void ApplyRosterTendencies()
        {
            if (_rosterSelectedIndex < 0) { Log("No roster player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadRosterTendenciesFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterTendenciesCheat.Apply(session, playerBase, desired);
                    Log($"Roster tendencies written for index {_rosterSelectedIndex} ({desired.Count} values).");
                }
                catch (Exception ex)
                {
                    Log("Roster tendencies apply failed: " + ex.Message);
                }
            }
        }

        private void RevertRosterTendencies()
        {
            if (_rosterSelectedIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_rosterSelectedIndex);
                    _rosterTendenciesCheat.Revert(session, playerBase);
                    if (_rosterTendenciesCheat.Original is { } original)
                        PopulateRosterTendencyInputs(original);
                    Log($"Roster tendencies reverted for index {_rosterSelectedIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Roster tendencies revert failed: " + ex.Message);
                }
            }
        }

        // ─── Roster: input <-> snapshot glue ────────────────────────────────

        private void PopulateRosterProfileInputs(PlayerProfileSnapshot snap)
        {
            _rosterFirstNameBox.Text = snap.FirstName;
            _rosterLastNameBox.Text = snap.LastName;
            _rosterPrimaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.PrimaryPosition);
            _rosterSecondaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.SecondaryPosition);
            _rosterJerseyBox.Value = Math.Clamp((decimal)snap.Jersey, _rosterJerseyBox.Minimum, _rosterJerseyBox.Maximum);
            _rosterWeightBox.Value = Math.Clamp((decimal)snap.Weight, _rosterWeightBox.Minimum, _rosterWeightBox.Maximum);
            _rosterHeightBox.Value = Math.Clamp((decimal)snap.Height, _rosterHeightBox.Minimum, _rosterHeightBox.Maximum);
            _rosterWingspanBox.Value = Math.Clamp((decimal)snap.Wingspan, _rosterWingspanBox.Minimum, _rosterWingspanBox.Maximum);
        }

        private PlayerProfileSnapshot ReadRosterProfileFromInputs()
        {
            // For static-roster records the phys sub-buffer is module-resident,
            // so PlayerProfileCheat.Write picks the GameplayHeight branch. Set
            // both Height and GameplayHeight (same for Wingspan) to the user's
            // single value so the right one is used regardless of which branch
            // the write code takes.
            float height = (float)_rosterHeightBox.Value;
            float wingspan = (float)_rosterWingspanBox.Value;
            return new PlayerProfileSnapshot(
                FirstName: _rosterFirstNameBox.Text,
                LastName: _rosterLastNameBox.Text,
                PrimaryPosition: PositionNames.IndexToRaw(Math.Max(0, _rosterPrimaryPosBox.SelectedIndex)),
                SecondaryPosition: PositionNames.IndexToRaw(Math.Max(0, _rosterSecondaryPosBox.SelectedIndex)),
                Weight: (float)_rosterWeightBox.Value,
                Jersey: (byte)_rosterJerseyBox.Value,
                Height: height,
                Wingspan: wingspan,
                GameplayHeight: height,
                GameplayWingspan: wingspan);
        }

        private void PopulateRosterRatingInputs(Dictionary<string, byte> values)
        {
            foreach (var kv in values)
            {
                if (_rosterRatingBoxes.TryGetValue(kv.Key, out var box))
                    box.Value = Math.Clamp((decimal)kv.Value, box.Minimum, box.Maximum);
            }
        }

        private Dictionary<string, byte> ReadRosterRatingsFromInputs()
        {
            var dict = new Dictionary<string, byte>(_rosterRatingBoxes.Count);
            foreach (var kv in _rosterRatingBoxes) dict[kv.Key] = (byte)kv.Value.Value;
            return dict;
        }

        private void PopulateRosterTendencyInputs(Dictionary<string, byte> values)
        {
            foreach (var kv in values)
            {
                if (_rosterTendencyBoxes.TryGetValue(kv.Key, out var box))
                    box.Value = Math.Clamp((decimal)kv.Value, box.Minimum, box.Maximum);
            }
        }

        private Dictionary<string, byte> ReadRosterTendenciesFromInputs()
        {
            var dict = new Dictionary<string, byte>(_rosterTendencyBoxes.Count);
            foreach (var kv in _rosterTendencyBoxes) dict[kv.Key] = (byte)kv.Value.Value;
            return dict;
        }

        private void PopulateRosterBadgeInputs(Dictionary<string, byte> values)
        {
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_rosterBadgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                if (!values.TryGetValue(b.Name, out byte v)) continue;
                int max = combo.Items.Count - 1;
                combo.SelectedIndex = Math.Clamp(v, 0, max);
            }
        }

        private Dictionary<string, byte> ReadRosterBadgesFromInputs()
        {
            var dict = new Dictionary<string, byte>(_rosterBadgeBoxes.Count);
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_rosterBadgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                int idx = Math.Max(0, combo.SelectedIndex);
                dict[b.Name] = (byte)idx;
            }
            return dict;
        }

        private void RefreshRosterPlayerLabel(PlayerProfileSnapshot snap)
        {
            int slot = _rosterPlayerList.SelectedIndex;
            if (slot < 0 || slot >= _rosterPlayerList.Items.Count) return;
            string label = string.IsNullOrEmpty(snap.LastName) && string.IsNullOrEmpty(snap.FirstName)
                ? "(empty)"
                : $"{snap.LastName}, {snap.FirstName}";
            _rosterSuppressEvents = true;
            try { _rosterPlayerList.Items[slot] = label; }
            finally { _rosterSuppressEvents = false; }
        }

        private void SetRosterTeamControlsEnabled(bool enabled)
        {
            _rosterTeamCombo.Enabled = enabled;
            _rosterPlayerList.Enabled = enabled;
            _rosterRefreshBtn.Enabled = enabled;
        }

        private void SetRosterPlayerControlsEnabled(bool enabled)
        {
            _rosterFirstNameBox.Enabled = enabled;
            _rosterLastNameBox.Enabled = enabled;
            _rosterJerseyBox.Enabled = enabled;
            _rosterPrimaryPosBox.Enabled = enabled;
            _rosterSecondaryPosBox.Enabled = enabled;
            _rosterWeightBox.Enabled = enabled;
            _rosterHeightBox.Enabled = enabled;
            _rosterWingspanBox.Enabled = enabled;
            _applyRosterProfileBtn.Enabled = enabled;
            _revertRosterProfileBtn.Enabled = enabled;

            foreach (var box in _rosterRatingBoxes.Values) box.Enabled = enabled;
            _rosterRatingOverrideBox.Enabled = enabled;
            _rosterRatingApplyOverrideBtn.Enabled = enabled;
            _applyRosterRatingsBtn.Enabled = enabled;
            _revertRosterRatingsBtn.Enabled = enabled;

            foreach (var combo in _rosterBadgeBoxes.Values) combo.Enabled = enabled;
            _applyRosterBadgesBtn.Enabled = enabled;
            _revertRosterBadgesBtn.Enabled = enabled;
            _maxAllRosterBadgesBtn.Enabled = enabled;

            foreach (var box in _rosterTendencyBoxes.Values) box.Enabled = enabled;
            _rosterTendencyOverrideBox.Enabled = enabled;
            _rosterTendencyApplyOverrideBtn.Enabled = enabled;
            _applyRosterTendenciesBtn.Enabled = enabled;
            _revertRosterTendenciesBtn.Enabled = enabled;

            _revertRosterToBaselineBtn.Enabled = enabled;
        }

        // ─── Baseline store (per-player "revert to original") ──────────────

        private void InitializeBaselineStore(ProcessSession session)
        {
            string path = RosterBaselineStore.DefaultPath;
            if (File.Exists(path) && _baselineStore.TryLoad(path))
            {
                Log($"Baseline loaded from {path}: {_baselineStore.PlayerCount} players, captured {_baselineStore.CapturedAt:yyyy-MM-dd HH:mm} UTC.");
                return;
            }

            try
            {
                _baselineStore.Capture(session, _rosterResolver);
                _baselineStore.Save(path);
                Log($"Baseline captured ({_baselineStore.PlayerCount} players) and saved to {path}.");
            }
            catch (Exception ex)
            {
                Log("Baseline capture/save failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Writes the captured baseline bytes back to a player's memory and
        /// re-probes the cheat snapshots so all three sub-tabs (profile,
        /// ratings, badges) show the restored state. Tab-agnostic — caller
        /// passes the right resolver/cheats/populate-fns.
        /// </summary>
        private bool RevertPlayerToBaseline(
            int rosterIndex,
            PlayerProfileCheat profileCheat,
            PlayerCheatBase<Dictionary<string, byte>> ratingsCheat,
            PlayerCheatBase<Dictionary<string, byte>> badgesCheat,
            PlayerCheatBase<Dictionary<string, byte>> tendenciesCheat,
            Action<PlayerProfileSnapshot> populateProfile,
            Action<Dictionary<string, byte>> populateRatings,
            Action<Dictionary<string, byte>> populateBadges,
            Action<Dictionary<string, byte>> populateTendencies,
            string tabLabel)
        {
            byte[]? raw = _baselineStore.GetBaselineFor(rosterIndex);
            if (raw is null)
            {
                Log($"{tabLabel}: no baseline snapshot for index {rosterIndex} — nothing to revert to.");
                return false;
            }
            if (!TryGetSession(out var session) || session is null) return false;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(rosterIndex);
                    session.WriteBytes(playerBase, raw);

                    // Phys-attrs (height, wingspan, body length, shoulder width)
                    // lives in a separate sub-buffer pointed to by +0x80. The
                    // bytes we just wrote restored the pointer (unchanged for
                    // static-roster records), so deref it now and write the
                    // captured phys bytes if we have them.
                    byte[]? phys = _baselineStore.GetPhysBaselineFor(rosterIndex);
                    if (phys is not null)
                    {
                        try
                        {
                            IntPtr physPtr = PlayerStructIO.ReadPhysAttrsPtr(session, playerBase);
                            if (physPtr != IntPtr.Zero)
                                session.WriteBytes(physPtr, phys);
                        }
                        catch
                        {
                            // Phys write failed; player record still reverted.
                        }
                    }

                    // Drop captured originals so subsequent "Revert to load-time"
                    // restores to the baseline rather than to whatever the user
                    // had edited before this baseline-revert.
                    profileCheat.ResetCapturedState();
                    ratingsCheat.ResetCapturedState();
                    badgesCheat.ResetCapturedState();
                    tendenciesCheat.ResetCapturedState();

                    var profile = profileCheat.Probe(session, playerBase);
                    var ratings = ratingsCheat.Probe(session, playerBase);
                    var badges = badgesCheat.Probe(session, playerBase);
                    var tendencies = tendenciesCheat.Probe(session, playerBase);

                    populateProfile(profile);
                    populateRatings(ratings);
                    populateBadges(badges);
                    populateTendencies(tendencies);

                    Log($"{tabLabel}: reverted index {rosterIndex} to original "
                        + $"({profile.FirstName} {profile.LastName}).");
                    return true;
                }
                catch (Exception ex)
                {
                    Log($"{tabLabel}: revert-to-baseline failed: {ex.Message}");
                    return false;
                }
            }
        }

        private void RevertPlayerListToBaseline()
        {
            if (_playerListSelectedRosterIndex < 0) { Log("No player selected."); return; }
            if (RevertPlayerToBaseline(
                _playerListSelectedRosterIndex,
                _playerListProfileCheat, _playerListRatingsCheat, _playerListBadgesCheat,
                _playerListTendenciesCheat,
                PopulatePlayerListProfileInputs,
                PopulatePlayerListRatingInputs,
                PopulatePlayerListBadgeInputs,
                PopulatePlayerListTendencyInputs,
                "Players-tab"))
            {
                // Name may have reverted; rebuild the visible label so the list
                // shows the original name again.
                if (TryGetSession(out var session) && session is not null)
                {
                    using (session)
                    {
                        var live = _playerListProfileCheat.Read(session, _rosterResolver.GetPlayer(_playerListSelectedRosterIndex));
                        UpdatePlayerListLabelForCurrentSelection(session, live);
                    }
                }
            }
        }

        private void RevertRosterToBaseline()
        {
            if (_rosterSelectedIndex < 0) { Log("No player selected."); return; }
            if (RevertPlayerToBaseline(
                _rosterSelectedIndex,
                _rosterProfileCheat, _rosterRatingsCheat, _rosterBadgesCheat,
                _rosterTendenciesCheat,
                PopulateRosterProfileInputs,
                PopulateRosterRatingInputs,
                PopulateRosterBadgeInputs,
                PopulateRosterTendencyInputs,
                "Roster-tab"))
            {
                if (TryGetSession(out var session) && session is not null)
                {
                    using (session)
                    {
                        var live = _rosterProfileCheat.Read(session, _rosterResolver.GetPlayer(_rosterSelectedIndex));
                        RefreshRosterPlayerLabel(live);
                    }
                }
            }
        }

        // ─── Players tab actions ────────────────────────────────────────────

        private void InitializePlayerListFromGame(ProcessSession session)
        {
            if (!_rosterResolver.Initialized)
            {
                _playerListStatusLabel.Text = "Players list unavailable — roster not mapped.";
                _playerListStatusLabel.ForeColor = Color.Firebrick;
                return;
            }
            try
            {
                _playerListAllLabels = BuildAllPlayerLabels(session);
                FilterPlayerList(_playerListSearchBox.Text ?? string.Empty);
                _playerListStatusLabel.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                _playerListStatusLabel.Text = "Players list failed to build: " + ex.Message;
                _playerListStatusLabel.ForeColor = Color.Firebrick;
                Log("Players list init failed: " + ex.Message);
            }
        }

        private void RefreshPlayerListFromGame()
        {
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                _playerListProfileCheat.ResetCapturedState();
                _playerListRatingsCheat.ResetCapturedState();
                _playerListBadgesCheat.ResetCapturedState();
                _playerListTendenciesCheat.ResetCapturedState();
                _playerListSelectedRosterIndex = -1;
                InitializePlayerListFromGame(session);
            }
        }

        private string[] BuildAllPlayerLabels(ProcessSession session)
        {
            int count = _rosterResolver.PlayerCount;
            var labels = new string[count];
            for (int i = 0; i < count; i++)
            {
                labels[i] = BuildPlayerLabel(session, i);
            }
            return labels;
        }

        private string BuildPlayerLabel(ProcessSession session, int rosterIndex)
        {
            string playerLabel = _rosterResolver.FormatPlayerLabel(session, rosterIndex);
            var team = _rosterResolver.FindTeamForPlayer(rosterIndex);
            // Strip the trailing "(15)" count from the team display since it's
            // noise per-row; the list shows one player at a time, not the
            // whole team.
            string teamName = team?.DisplayName ?? string.Empty;
            int parenIdx = teamName.LastIndexOf(" (", StringComparison.Ordinal);
            if (parenIdx > 0) teamName = teamName.Substring(0, parenIdx);
            return string.IsNullOrEmpty(teamName)
                ? playerLabel
                : $"{playerLabel} — {teamName}";  // " — " (em dash)
        }

        private void OnPlayerListSearchChanged()
        {
            if (_playerListSuppressEvents) return;
            FilterPlayerList(_playerListSearchBox.Text ?? string.Empty);
        }

        private void FilterPlayerList(string searchText)
        {
            int total = _playerListAllLabels.Length;
            if (total == 0)
            {
                _playerListSuppressEvents = true;
                try
                {
                    _playerListListBox.Items.Clear();
                    _playerListVisibleIndices = Array.Empty<int>();
                }
                finally { _playerListSuppressEvents = false; }
                _playerListStatusLabel.Text = "No players to show.";
                return;
            }

            string needle = (searchText ?? string.Empty).Trim();
            bool hasFilter = needle.Length > 0;

            var visible = new List<int>(total);
            for (int i = 0; i < total; i++)
            {
                string label = _playerListAllLabels[i];
                if (!hasFilter
                    || label.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    visible.Add(i);
                }
            }

            _playerListSuppressEvents = true;
            try
            {
                _playerListListBox.BeginUpdate();
                _playerListListBox.Items.Clear();
                foreach (int idx in visible)
                    _playerListListBox.Items.Add(_playerListAllLabels[idx]);
                _playerListListBox.EndUpdate();
                _playerListVisibleIndices = visible.ToArray();
            }
            finally { _playerListSuppressEvents = false; }

            _playerListStatusLabel.Text = hasFilter
                ? $"Showing {visible.Count} of {total} players matching \"{needle}\". Click a name, then use the Profile / Ratings / Badges sub-tabs."
                : $"Showing {total} of {total} players. Click a name, then use the Profile / Ratings / Badges sub-tabs to edit.";

            _playerListSelectedRosterIndex = -1;
            SetPlayerListPlayerControlsEnabled(false);
        }

        private void OnPlayerListSelected()
        {
            if (_playerListSuppressEvents) return;
            int row = _playerListListBox.SelectedIndex;
            if (row < 0 || row >= _playerListVisibleIndices.Length) return;
            int rosterIndex = _playerListVisibleIndices[row];
            if (rosterIndex < 0 || rosterIndex >= _rosterResolver.PlayerCount) return;

            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    // Drop captured originals so Revert restores the newly-selected
                    // player's load-time values, not whoever was selected before.
                    _playerListProfileCheat.ResetCapturedState();
                    _playerListRatingsCheat.ResetCapturedState();
                    _playerListBadgesCheat.ResetCapturedState();
                    _playerListTendenciesCheat.ResetCapturedState();

                    IntPtr playerBase = _rosterResolver.GetPlayer(rosterIndex);
                    var profile = _playerListProfileCheat.Probe(session, playerBase);
                    var ratings = _playerListRatingsCheat.Probe(session, playerBase);
                    var badges = _playerListBadgesCheat.Probe(session, playerBase);
                    var tendencies = _playerListTendenciesCheat.Probe(session, playerBase);

                    PopulatePlayerListProfileInputs(profile);
                    PopulatePlayerListRatingInputs(ratings);
                    PopulatePlayerListBadgeInputs(badges);
                    PopulatePlayerListTendencyInputs(tendencies);

                    _playerListSelectedRosterIndex = rosterIndex;
                    SetPlayerListPlayerControlsEnabled(true);
                }
                catch (Exception ex)
                {
                    Log("Players-tab player load failed: " + ex.Message);
                    SetPlayerListPlayerControlsEnabled(false);
                }
            }
        }

        private void ApplyPlayerListProfile()
        {
            if (_playerListSelectedRosterIndex < 0) { Log("No player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadPlayerListProfileFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListProfileCheat.Apply(session, playerBase, desired);

                    // Update the cached label + the ListBox row if the name changed.
                    var live = _playerListProfileCheat.Read(session, playerBase);
                    UpdatePlayerListLabelForCurrentSelection(session, live);

                    Log($"Players-tab profile written for index {_playerListSelectedRosterIndex}: "
                        + $"{desired.FirstName} {desired.LastName} · "
                        + $"{PositionNames.Format(desired.PrimaryPosition)}/{PositionNames.Format(desired.SecondaryPosition)} · "
                        + $"#{desired.Jersey} · {desired.Height:F2}cm / {desired.Wingspan:F2}cm wing / {desired.Weight:F2}lbs.");
                }
                catch (Exception ex)
                {
                    Log("Players-tab profile apply failed: " + ex.Message);
                }
            }
        }

        private void RevertPlayerListProfile()
        {
            if (_playerListSelectedRosterIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListProfileCheat.Revert(session, playerBase);
                    if (_playerListProfileCheat.Original is { } original)
                    {
                        PopulatePlayerListProfileInputs(original);
                        UpdatePlayerListLabelForCurrentSelection(session, original);
                    }
                    Log($"Players-tab profile reverted for index {_playerListSelectedRosterIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Players-tab profile revert failed: " + ex.Message);
                }
            }
        }

        private void ApplyPlayerListRatings()
        {
            if (_playerListSelectedRosterIndex < 0) { Log("No player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadPlayerListRatingsFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListRatingsCheat.Apply(session, playerBase, desired);
                    Log($"Players-tab ratings written for index {_playerListSelectedRosterIndex} ({desired.Count} values).");
                }
                catch (Exception ex)
                {
                    Log("Players-tab ratings apply failed: " + ex.Message);
                }
            }
        }

        private void RevertPlayerListRatings()
        {
            if (_playerListSelectedRosterIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListRatingsCheat.Revert(session, playerBase);
                    if (_playerListRatingsCheat.Original is { } original)
                        PopulatePlayerListRatingInputs(original);
                    Log($"Players-tab ratings reverted for index {_playerListSelectedRosterIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Players-tab ratings revert failed: " + ex.Message);
                }
            }
        }

        private void ApplyPlayerListBadges()
        {
            if (_playerListSelectedRosterIndex < 0) { Log("No player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadPlayerListBadgesFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListBadgesCheat.Apply(session, playerBase, desired);
                    Log($"Players-tab badges written for index {_playerListSelectedRosterIndex} ({desired.Count} values).");
                }
                catch (Exception ex)
                {
                    Log("Players-tab badges apply failed: " + ex.Message);
                }
            }
        }

        private void RevertPlayerListBadges()
        {
            if (_playerListSelectedRosterIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListBadgesCheat.Revert(session, playerBase);
                    if (_playerListBadgesCheat.Original is { } original)
                        PopulatePlayerListBadgeInputs(original);
                    Log($"Players-tab badges reverted for index {_playerListSelectedRosterIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Players-tab badges revert failed: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// Fills every badge combo to its maximum tier (Gold for 2-bit, ON for
        /// 1-bit). Does NOT auto-apply — user clicks "Apply badges" after
        /// reviewing, matching the Ratings "Fill all" two-step pattern.
        /// </summary>
        private void MaxAllPlayerListBadges()
        {
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_playerListBadgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                int maxIndex = Math.Min((int)BadgesCheat.MaxTierFor(b), combo.Items.Count - 1);
                combo.SelectedIndex = maxIndex;
            }
        }

        private void ApplyPlayerListTendencies()
        {
            if (_playerListSelectedRosterIndex < 0) { Log("No player selected."); return; }
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    var desired = ReadPlayerListTendenciesFromInputs();
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListTendenciesCheat.Apply(session, playerBase, desired);
                    Log($"Players-tab tendencies written for index {_playerListSelectedRosterIndex} ({desired.Count} values).");
                }
                catch (Exception ex)
                {
                    Log("Players-tab tendencies apply failed: " + ex.Message);
                }
            }
        }

        private void RevertPlayerListTendencies()
        {
            if (_playerListSelectedRosterIndex < 0) return;
            if (!TryGetSession(out var session) || session is null) return;
            using (session)
            {
                try
                {
                    IntPtr playerBase = _rosterResolver.GetPlayer(_playerListSelectedRosterIndex);
                    _playerListTendenciesCheat.Revert(session, playerBase);
                    if (_playerListTendenciesCheat.Original is { } original)
                        PopulatePlayerListTendencyInputs(original);
                    Log($"Players-tab tendencies reverted for index {_playerListSelectedRosterIndex}.");
                }
                catch (Exception ex)
                {
                    Log("Players-tab tendencies revert failed: " + ex.Message);
                }
            }
        }

        // ─── Players: input <-> snapshot glue ───────────────────────────────

        private void PopulatePlayerListProfileInputs(PlayerProfileSnapshot snap)
        {
            _playerListFirstNameBox.Text = snap.FirstName;
            _playerListLastNameBox.Text = snap.LastName;
            _playerListPrimaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.PrimaryPosition);
            _playerListSecondaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.SecondaryPosition);
            _playerListJerseyBox.Value = Math.Clamp((decimal)snap.Jersey, _playerListJerseyBox.Minimum, _playerListJerseyBox.Maximum);
            _playerListWeightBox.Value = Math.Clamp((decimal)snap.Weight, _playerListWeightBox.Minimum, _playerListWeightBox.Maximum);
            _playerListHeightBox.Value = Math.Clamp((decimal)snap.Height, _playerListHeightBox.Minimum, _playerListHeightBox.Maximum);
            _playerListWingspanBox.Value = Math.Clamp((decimal)snap.Wingspan, _playerListWingspanBox.Minimum, _playerListWingspanBox.Maximum);
        }

        private PlayerProfileSnapshot ReadPlayerListProfileFromInputs()
        {
            float height = (float)_playerListHeightBox.Value;
            float wingspan = (float)_playerListWingspanBox.Value;
            return new PlayerProfileSnapshot(
                FirstName: _playerListFirstNameBox.Text,
                LastName: _playerListLastNameBox.Text,
                PrimaryPosition: PositionNames.IndexToRaw(Math.Max(0, _playerListPrimaryPosBox.SelectedIndex)),
                SecondaryPosition: PositionNames.IndexToRaw(Math.Max(0, _playerListSecondaryPosBox.SelectedIndex)),
                Weight: (float)_playerListWeightBox.Value,
                Jersey: (byte)_playerListJerseyBox.Value,
                Height: height,
                Wingspan: wingspan,
                GameplayHeight: height,
                GameplayWingspan: wingspan);
        }

        private void PopulatePlayerListRatingInputs(Dictionary<string, byte> values)
        {
            foreach (var kv in values)
            {
                if (_playerListRatingBoxes.TryGetValue(kv.Key, out var box))
                    box.Value = Math.Clamp((decimal)kv.Value, box.Minimum, box.Maximum);
            }
        }

        private void PopulatePlayerListTendencyInputs(Dictionary<string, byte> values)
        {
            foreach (var kv in values)
            {
                if (_playerListTendencyBoxes.TryGetValue(kv.Key, out var box))
                    box.Value = Math.Clamp((decimal)kv.Value, box.Minimum, box.Maximum);
            }
        }

        private Dictionary<string, byte> ReadPlayerListTendenciesFromInputs()
        {
            var dict = new Dictionary<string, byte>(_playerListTendencyBoxes.Count);
            foreach (var kv in _playerListTendencyBoxes) dict[kv.Key] = (byte)kv.Value.Value;
            return dict;
        }

        private Dictionary<string, byte> ReadPlayerListRatingsFromInputs()
        {
            var dict = new Dictionary<string, byte>(_playerListRatingBoxes.Count);
            foreach (var kv in _playerListRatingBoxes) dict[kv.Key] = (byte)kv.Value.Value;
            return dict;
        }

        private void PopulatePlayerListBadgeInputs(Dictionary<string, byte> values)
        {
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_playerListBadgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                if (!values.TryGetValue(b.Name, out byte v)) continue;
                int max = combo.Items.Count - 1;
                combo.SelectedIndex = Math.Clamp(v, 0, max);
            }
        }

        private Dictionary<string, byte> ReadPlayerListBadgesFromInputs()
        {
            var dict = new Dictionary<string, byte>(_playerListBadgeBoxes.Count);
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_playerListBadgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                int idx = Math.Max(0, combo.SelectedIndex);
                dict[b.Name] = (byte)idx;
            }
            return dict;
        }

        private void UpdatePlayerListLabelForCurrentSelection(ProcessSession session, PlayerProfileSnapshot live)
        {
            int row = _playerListListBox.SelectedIndex;
            if (row < 0 || row >= _playerListVisibleIndices.Length) return;
            int rosterIndex = _playerListVisibleIndices[row];
            if (rosterIndex < 0 || rosterIndex >= _playerListAllLabels.Length) return;

            string newLabel = BuildPlayerLabel(session, rosterIndex);
            _playerListAllLabels[rosterIndex] = newLabel;

            _playerListSuppressEvents = true;
            try { _playerListListBox.Items[row] = newLabel; }
            finally { _playerListSuppressEvents = false; }
        }

        private void SetPlayerListSearchControlsEnabled(bool enabled)
        {
            _playerListSearchBox.Enabled = enabled;
            _playerListListBox.Enabled = enabled;
            _playerListRefreshBtn.Enabled = enabled;
        }

        private void SetPlayerListPlayerControlsEnabled(bool enabled)
        {
            _playerListFirstNameBox.Enabled = enabled;
            _playerListLastNameBox.Enabled = enabled;
            _playerListJerseyBox.Enabled = enabled;
            _playerListPrimaryPosBox.Enabled = enabled;
            _playerListSecondaryPosBox.Enabled = enabled;
            _playerListWeightBox.Enabled = enabled;
            _playerListHeightBox.Enabled = enabled;
            _playerListWingspanBox.Enabled = enabled;
            _applyPlayerListProfileBtn.Enabled = enabled;
            _revertPlayerListProfileBtn.Enabled = enabled;

            foreach (var box in _playerListRatingBoxes.Values) box.Enabled = enabled;
            _playerListRatingOverrideBox.Enabled = enabled;
            _playerListRatingApplyOverrideBtn.Enabled = enabled;
            _applyPlayerListRatingsBtn.Enabled = enabled;
            _revertPlayerListRatingsBtn.Enabled = enabled;

            foreach (var combo in _playerListBadgeBoxes.Values) combo.Enabled = enabled;
            _applyPlayerListBadgesBtn.Enabled = enabled;
            _revertPlayerListBadgesBtn.Enabled = enabled;
            _maxAllPlayerListBadgesBtn.Enabled = enabled;

            foreach (var box in _playerListTendencyBoxes.Values) box.Enabled = enabled;
            _playerListTendencyOverrideBox.Enabled = enabled;
            _playerListTendencyApplyOverrideBtn.Enabled = enabled;
            _applyPlayerListTendenciesBtn.Enabled = enabled;
            _revertPlayerListTendenciesBtn.Enabled = enabled;

            _revertPlayerListToBaselineBtn.Enabled = enabled;
        }

        // ─── Glue: settings ↔ inputs ────────────────────────────────────────

        private void PopulateProfileInputs(PlayerProfileSnapshot snap)
        {
            _firstNameBox.Text = snap.FirstName;
            _lastNameBox.Text = snap.LastName;
            _primaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.PrimaryPosition);
            _secondaryPosBox.SelectedIndex = PositionNames.RawToIndex(snap.SecondaryPosition);
            _jerseyBox.Value = Math.Clamp((decimal)snap.Jersey, _jerseyBox.Minimum, _jerseyBox.Maximum);
            _weightBox.Value = Math.Clamp((decimal)snap.Weight, _weightBox.Minimum, _weightBox.Maximum);
            _heightBox.Value = Math.Clamp((decimal)snap.Height, _heightBox.Minimum, _heightBox.Maximum);
            _wingspanBox.Value = Math.Clamp((decimal)snap.Wingspan, _wingspanBox.Minimum, _wingspanBox.Maximum);
            _gameplayHeightBox.Value = Math.Clamp((decimal)snap.GameplayHeight, _gameplayHeightBox.Minimum, _gameplayHeightBox.Maximum);
            _gameplayWingspanBox.Value = Math.Clamp((decimal)snap.GameplayWingspan, _gameplayWingspanBox.Minimum, _gameplayWingspanBox.Maximum);
        }

        private void PopulateRatingInputs(Dictionary<string, byte> values)
        {
            foreach (var kv in values)
            {
                if (_ratingBoxes.TryGetValue(kv.Key, out var box))
                    box.Value = Math.Clamp((decimal)kv.Value, box.Minimum, box.Maximum);
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
            Wingspan: (float)_wingspanBox.Value,
            GameplayHeight: (float)_gameplayHeightBox.Value,
            GameplayWingspan: (float)_gameplayWingspanBox.Value);

        private Dictionary<string, byte> ReadRatingsFromInputs()
        {
            var dict = new Dictionary<string, byte>(_ratingBoxes.Count);
            foreach (var kv in _ratingBoxes) dict[kv.Key] = (byte)kv.Value.Value;
            return dict;
        }

        private void PopulateBadgeInputs(Dictionary<string, byte> values)
        {
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_badgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                if (!values.TryGetValue(b.Name, out byte v)) continue;
                int max = combo.Items.Count - 1;
                combo.SelectedIndex = Math.Clamp(v, 0, max);
            }
        }

        private Dictionary<string, byte> ReadBadgesFromInputs()
        {
            var dict = new Dictionary<string, byte>(_badgeBoxes.Count);
            foreach (var b in BadgesCheat.Badges)
            {
                if (!_badgeBoxes.TryGetValue(b.Name, out var combo)) continue;
                int idx = Math.Max(0, combo.SelectedIndex);
                dict[b.Name] = (byte)idx;
            }
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
            || _settings.Wingspan is not null
            || _settings.PerPlayerGameplayHeight is not null
            || _settings.GameplayWingspan is not null;

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
            GameplayHeight = _settings.PerPlayerGameplayHeight ?? live.GameplayHeight,
            GameplayWingspan = _settings.GameplayWingspan ?? live.GameplayWingspan,
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
            _settings.PerPlayerGameplayHeight = v.GameplayHeight;
            _settings.GameplayWingspan = v.GameplayWingspan;
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
            _settings.AutoApplyBadges = _autoApplyBadgesToggle.Checked;
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
            _gameplayHeightBox.Enabled = resolved;
            _gameplayWingspanBox.Enabled = resolved;
            _applyProfileBtn.Enabled = resolved;
            _revertProfileBtn.Enabled = resolved;

            foreach (var box in _ratingBoxes.Values) box.Enabled = resolved;
            _ratingOverrideBox.Enabled = resolved;
            _ratingApplyOverrideBtn.Enabled = resolved;
            _applyRatingsBtn.Enabled = resolved;
            _revertRatingsBtn.Enabled = resolved;

            foreach (var combo in _badgeBoxes.Values) combo.Enabled = resolved;
            _applyBadgesBtn.Enabled = resolved;
            _revertBadgesBtn.Enabled = resolved;
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

        private void CopyRecentLog(int n)
        {
            var lines = _logBox.Text
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                Log("No log lines to copy.");
                return;
            }
            var tail = lines.Skip(Math.Max(0, lines.Length - n));
            string payload = string.Join(Environment.NewLine, tail);
            try
            {
                Clipboard.SetText(payload);
                Log($"Copied last {Math.Min(n, lines.Length)} log line(s) to clipboard.");
            }
            catch (Exception ex)
            {
                Log("Clipboard copy failed: " + ex.Message);
            }
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
