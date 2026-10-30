using System;
using System.Drawing;
using System.Windows.Forms;

namespace Harmony
{
    public sealed class MainForm : Form
    {
        private readonly ToneGenerator _generator = new();

        private ListBox _lstPresets = null!;
        private Label _lblDescription = null!;
        private TrackBar _trkVolume = null!;
        private Label _lblVolume = null!;
        private Button _toggleButton = null!;
        private Label _lblInfo = null!;
        private NotifyIcon _trayIcon = null!;

        private TonePreset _currentPreset = TonePreset.Schumann;

        public MainForm()
        {
            // Load embedded icon for form and tray
            var stream = typeof(MainForm).Assembly.GetManifestResourceStream("Harmony.Harmony.ico");
            if (stream != null)
                Icon = new Icon(stream);

            InitializeUI();
            InitializeTrayIcon();
        }

        private void InitializeUI()
        {
            Text = "Harmony - Healing Tone Generator";
            Size = new Size(420, 420);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            int y = 15;

            // Preset list
            Controls.Add(new Label { Text = "Preset:", Location = new Point(15, y), AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
            y += 22;

            _lstPresets = new ListBox
            {
                Location = new Point(15, y),
                Size = new Size(370, 130)
            };
            foreach (var preset in TonePreset.AllPresets)
                _lstPresets.Items.Add(preset.Name);
            _lstPresets.SelectedIndex = 0;
            _lstPresets.SelectedIndexChanged += (s, e) =>
            {
                if (_lstPresets.SelectedIndex >= 0)
                {
                    _currentPreset = TonePreset.AllPresets[_lstPresets.SelectedIndex];
                    _lblDescription.Text = _currentPreset.Description;
                    UpdateInfo();
                    if (_generator.IsPlaying)
                        _generator.SetPreset(_currentPreset);
                }
            };
            Controls.Add(_lstPresets);
            y += 138;

            // Description
            _lblDescription = new Label
            {
                Text = _currentPreset.Description,
                Location = new Point(15, y),
                Size = new Size(370, 35),
                ForeColor = Color.DarkSlateGray
            };
            Controls.Add(_lblDescription);
            y += 40;

            // Frequency info
            _lblInfo = new Label
            {
                Location = new Point(15, y),
                Size = new Size(370, 18),
                ForeColor = Color.Gray,
                Font = new Font(Font.FontFamily, 8f)
            };
            UpdateInfo();
            Controls.Add(_lblInfo);
            y += 25;

            // Volume
            Controls.Add(new Label { Text = "Volume:", Location = new Point(15, y + 3), AutoSize = true });
            _trkVolume = new TrackBar
            {
                Location = new Point(75, y),
                Size = new Size(250, 30),
                Minimum = 0,
                Maximum = 100,
                Value = 15,
                TickFrequency = 10
            };
            _lblVolume = new Label { Text = "15%", Location = new Point(335, y + 3), AutoSize = true };
            _trkVolume.ValueChanged += (s, e) =>
            {
                _lblVolume.Text = $"{_trkVolume.Value}%";
                if (_generator.IsPlaying)
                    _generator.SetVolume(_trkVolume.Value / 100f);
            };
            Controls.Add(_trkVolume);
            Controls.Add(_lblVolume);
            y += 40;

            // Subliminal mode checkbox
            var chkSubliminal = new CheckBox
            {
                Text = "Subliminal mode (inaudible - volume locked at 1-2%)",
                Location = new Point(15, y),
                AutoSize = true
            };
            chkSubliminal.CheckedChanged += (s, e) =>
            {
                if (chkSubliminal.Checked)
                {
                    _trkVolume.Value = 1;
                    _trkVolume.Maximum = 3;
                    _trkVolume.Enabled = false;
                    _lblVolume.Text = "0.2% (subliminal)";
                    if (_generator.IsPlaying) _generator.SetVolume(0.002f);
                }
                else
                {
                    _trkVolume.Maximum = 100;
                    _trkVolume.Enabled = true;
                    _lblVolume.Text = $"{_trkVolume.Value}%";
                    if (_generator.IsPlaying) _generator.SetVolume(_trkVolume.Value / 100f);
                }
            };
            Controls.Add(chkSubliminal);
            y += 30;

            // Toggle button
            _toggleButton = new Button
            {
                Text = "Play",
                Location = new Point(15, y),
                Size = new Size(370, 40),
                BackColor = Color.FromArgb(40, 167, 69),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font(Font.FontFamily, 11, FontStyle.Bold)
            };
            _toggleButton.Click += (s, e) => Toggle();
            Controls.Add(_toggleButton);
            y += 55;

            // Footer
            Controls.Add(new Label
            {
                Text = "Plays alongside your normal audio. Use headphones for binaural beats.",
                Location = new Point(15, y),
                AutoSize = true,
                ForeColor = Color.DimGray,
                Font = new Font(Font.FontFamily, 7.5f)
            });
        }

        private void UpdateInfo()
        {
            var p = _currentPreset;
            string info = $"L: {p.FrequencyLeft} Hz";
            if (p.FrequencyRight > 0)
                info += $" | R: {p.FrequencyRight} Hz | Binaural: {p.BinauralBeatHz:F1} Hz";
            if (p.Layers.Length > 0)
                info += $" | +{p.Layers.Length} layers";
            _lblInfo.Text = info;
        }

        private void Toggle()
        {
            if (_generator.IsPlaying)
            {
                _generator.Stop();
                _toggleButton.Text = "Play";
                _toggleButton.BackColor = Color.FromArgb(40, 167, 69);
            }
            else
            {
                _generator.Start(_currentPreset, _trkVolume.Value / 100f);
                _toggleButton.Text = "Stop";
                _toggleButton.BackColor = Color.FromArgb(220, 53, 69);
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new NotifyIcon { Text = "Harmony", Icon = Icon, Visible = true };
            _trayIcon.DoubleClick += (s, e) => { Show(); WindowState = FormWindowState.Normal; };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Show", null, (s, e) => { Show(); WindowState = FormWindowState.Normal; });
            menu.Items.Add("Exit", null, (s, e) => { _generator.Dispose(); Application.Exit(); });
            _trayIcon.ContextMenuStrip = menu;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                _trayIcon.ShowBalloonTip(1000, "Harmony", "Still playing in the background.", ToolTipIcon.Info);
                return;
            }
            _generator.Dispose();
            _trayIcon.Dispose();
            base.OnFormClosing(e);
        }
    }
}
