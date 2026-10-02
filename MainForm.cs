using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using LSL;

namespace LSLStreamViewer
{
    public class MainForm : Form
    {
        // ---- Continuous Stream Discovery ----
        private ContinuousResolver _resolver;
        private System.Windows.Forms.Timer _discoveryTimer;
        private System.Windows.Forms.Timer _statsTimer;
        private System.Windows.Forms.Timer _chartRefreshTimer;
        private readonly List<StreamInfo> _discoveredStreams = new List<StreamInfo>();

        // ---- Active Reader ----
        private StreamReaderWorker _reader;
        private StreamInfo _activeStreamInfo;
        private bool _isStringStream;

        // ---- Built-in Test Simulator ----
        private TestStreamGenerator _testGenerator;

        // ---- UI Controls ----
        private Panel _topBar;
        private Panel _leftPanel;
        private SplitContainer _rightSplit;

        // Left Panel
        private ListView _lvStreams;
        private Button _btnRefresh;
        private Label _lblDiscovery;

        // Top Toolbar
        private Button _btnConnect;
        private Button _btnDisconnect;
        private Button _btnPause;
        private Button _btnClearData;
        private Button _btnModeToggle;
        private Button _btnTestStream;
        private ComboBox _cmbMaxSamples;
        private CheckBox _chkAutoScroll;

        // Right Panel - Top: View Tabs
        private TabControl _tabView;
        private StreamChartControl _chart;
        private RichTextBox _rtbData;

        // Right Panel - Bottom: Info
        private RichTextBox _rtbInfo;

        // Status Bar
        private Panel _statusBar;
        private Label _lblStatus;
        private Label _lblSampleRate;
        private Label _lblSampleCount;

        // Stats tracking
        private long _lastSampleCount;
        private DateTime _lastStatTime;

        public MainForm()
        {
            InitializeForm();
            InitializeComponents();
            StartDiscovery();
        }

        #region Form Setup

        private void InitializeForm()
        {
            Text = "LSL Stream Viewer";
            Size = new Size(1320, 840);
            MinimumSize = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9.5f);
            BackColor = Color.FromArgb(24, 24, 32);
            ForeColor = Color.FromArgb(230, 230, 235);
            DoubleBuffered = true;

            // Load app icon
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var iconStream = asm.GetManifestResourceStream("app.ico");
                if (iconStream != null)
                    Icon = new Icon(iconStream);
            }
            catch { }
        }

        private void InitializeComponents()
        {
            // ========== TOP BAR ==========
            _topBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Color.FromArgb(32, 32, 42),
                Padding = new Padding(12, 10, 12, 10),
            };

            var lblTitle = new Label
            {
                Text = "📡 LSL Stream Viewer",
                Font = new Font("Segoe UI", 14f, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 210, 130),
                AutoSize = true,
                Location = new Point(12, 11),
            };
            _topBar.Controls.Add(lblTitle);

            int x = 230;

            _btnConnect = CreateButton("▶ Bağlan", x, 10, Color.FromArgb(39, 174, 96), 95);
            _btnConnect.Click += BtnConnect_Click;
            _topBar.Controls.Add(_btnConnect);
            x += 102;

            _btnDisconnect = CreateButton("⏹ Kes", x, 10, Color.FromArgb(192, 57, 43), 75);
            _btnDisconnect.Enabled = false;
            _btnDisconnect.Click += BtnDisconnect_Click;
            _topBar.Controls.Add(_btnDisconnect);
            x += 82;

            _btnPause = CreateButton("⏸ Dondur", x, 10, Color.FromArgb(211, 84, 0), 90);
            _btnPause.Click += BtnPause_Click;
            _topBar.Controls.Add(_btnPause);
            x += 98;

            _btnModeToggle = CreateButton("☰ Görünüm: Ayrık", x, 10, Color.FromArgb(41, 128, 185), 140);
            _btnModeToggle.Click += BtnModeToggle_Click;
            _topBar.Controls.Add(_btnModeToggle);
            x += 148;

            _btnClearData = CreateButton("🗑 Temizle", x, 10, Color.FromArgb(108, 122, 137), 85);
            _btnClearData.Click += (s, e) => ClearData();
            _topBar.Controls.Add(_btnClearData);
            x += 93;

            var lblSamples = new Label
            {
                Text = "Örnek:",
                AutoSize = true,
                ForeColor = Color.FromArgb(160, 160, 175),
                Font = new Font("Segoe UI", 8.5f),
                Location = new Point(x, 17),
            };
            _topBar.Controls.Add(lblSamples);
            x += 46;

            _cmbMaxSamples = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(x, 12),
                Size = new Size(80, 26),
                BackColor = Color.FromArgb(45, 45, 58),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f),
                FlatStyle = FlatStyle.Flat,
            };
            _cmbMaxSamples.Items.AddRange(new object[] { "100", "250", "500", "1000", "2000", "5000" });
            _cmbMaxSamples.SelectedItem = "500";
            _cmbMaxSamples.SelectedIndexChanged += (s, e) =>
            {
                if (_chart != null && int.TryParse(_cmbMaxSamples.SelectedItem?.ToString(), out int val))
                    _chart.MaxSamples = val;
            };
            _topBar.Controls.Add(_cmbMaxSamples);
            x += 88;

            _chkAutoScroll = new CheckBox
            {
                Text = "↕ Oto-Kaydır",
                Checked = true,
                AutoSize = true,
                ForeColor = Color.FromArgb(200, 200, 215),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Location = new Point(x, 15),
                Cursor = Cursors.Hand,
            };
            _topBar.Controls.Add(_chkAutoScroll);

            // Right-aligned Test Stream button
            _btnTestStream = CreateButton("🧪 Test Stream Başlat", 0, 10, Color.FromArgb(142, 68, 173), 150);
            _btnTestStream.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnTestStream.Click += BtnTestStream_Click;
            _topBar.Controls.Add(_btnTestStream);

            // ========== LEFT PANEL (Discovered Streams) ==========
            _leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 330,
                BackColor = Color.FromArgb(28, 28, 38),
                Padding = new Padding(8, 8, 4, 8),
            };

            var streamHeaderPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(28, 28, 38),
            };

            var lblStreamsHeader = new Label
            {
                Text = "🔎 Ağdaki Stream'ler",
                Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(80, 210, 130),
                AutoSize = true,
                Location = new Point(4, 4),
            };
            streamHeaderPanel.Controls.Add(lblStreamsHeader);

            _btnRefresh = CreateButton("🔄 Yenile", 240, 4, Color.FromArgb(52, 152, 219), 75);
            _btnRefresh.Size = new Size(75, 26);
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.Click += (s, e) => RefreshStreamsList();
            streamHeaderPanel.Controls.Add(_btnRefresh);

            _lblDiscovery = new Label
            {
                Text = "Stream'ler aranıyor...",
                AutoSize = true,
                ForeColor = Color.FromArgb(140, 140, 160),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Italic),
                Location = new Point(6, 32),
            };
            streamHeaderPanel.Controls.Add(_lblDiscovery);
            _leftPanel.Controls.Add(streamHeaderPanel);

            _lvStreams = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                MultiSelect = false,
                BackColor = Color.FromArgb(32, 32, 44),
                ForeColor = Color.FromArgb(225, 225, 235),
                Font = new Font("Segoe UI", 9f),
                BorderStyle = BorderStyle.None,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                OwnerDraw = true,
            };
            _lvStreams.Columns.Add("Stream Adı", 125);
            _lvStreams.Columns.Add("Tip", 65);
            _lvStreams.Columns.Add("Ch", 38);
            _lvStreams.Columns.Add("Hz", 50);

            _lvStreams.DrawColumnHeader += (s, e) =>
            {
                using var bgBrush = new SolidBrush(Color.FromArgb(40, 40, 54));
                e.Graphics.FillRectangle(bgBrush, e.Bounds);
                using var textBrush = new SolidBrush(Color.FromArgb(160, 205, 255));
                using var font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
                e.Graphics.DrawString(e.Header.Text, font, textBrush, e.Bounds.Left + 4, e.Bounds.Top + 4);
            };

            _lvStreams.DrawItem += (s, e) => { };

            _lvStreams.DrawSubItem += (s, e) =>
            {
                bool isSelected = e.Item.Selected;
                using var bgBrush = new SolidBrush(isSelected
                    ? Color.FromArgb(46, 80, 120) : (e.ItemIndex % 2 == 0 ? Color.FromArgb(32, 32, 44) : Color.FromArgb(28, 28, 38)));
                e.Graphics.FillRectangle(bgBrush, e.Bounds);

                Color textColor = isSelected ? Color.White : Color.FromArgb(215, 215, 225);
                if (!isSelected && e.ColumnIndex == 1) textColor = Color.FromArgb(100, 180, 255);
                if (!isSelected && e.ColumnIndex == 2) textColor = Color.FromArgb(241, 196, 15);
                if (!isSelected && e.ColumnIndex == 3) textColor = Color.FromArgb(46, 204, 113);

                using var textBrush = new SolidBrush(textColor);
                using var font = new Font("Segoe UI", 8.5f, isSelected ? FontStyle.Bold : FontStyle.Regular);
                var text = e.SubItem?.Text ?? "";
                e.Graphics.DrawString(text, font, textBrush, e.Bounds.Left + 4, e.Bounds.Top + 3);
            };

            _lvStreams.SelectedIndexChanged += LvStreams_SelectedIndexChanged;
            _lvStreams.DoubleClick += (s, e) => BtnConnect_Click(s, e);
            _leftPanel.Controls.Add(_lvStreams);
            _lvStreams.BringToFront();

            // ========== RIGHT PANEL ==========
            var rightContainer = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(6, 6, 8, 6),
                BackColor = Color.FromArgb(24, 24, 32),
            };

            _rightSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterDistance = 490,
                SplitterWidth = 6,
                BackColor = Color.FromArgb(45, 45, 60),
                Panel1MinSize = 200,
                Panel2MinSize = 100,
            };
            _rightSplit.Panel1.BackColor = Color.FromArgb(24, 24, 32);
            _rightSplit.Panel2.BackColor = Color.FromArgb(24, 24, 32);

            // === Tab View for Chart / Data ===
            _tabView = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            };
            _tabView.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabView.DrawItem += TabView_DrawItem;
            _tabView.SizeMode = TabSizeMode.Fixed;
            _tabView.ItemSize = new Size(160, 32);

            var tabChart = new TabPage("📊 Canlı Grafik") { BackColor = Color.FromArgb(20, 20, 28) };
            var tabData = new TabPage("📝 Veri Akışı / Log") { BackColor = Color.FromArgb(20, 20, 28) };

            _chart = new StreamChartControl
            {
                Dock = DockStyle.Fill,
                MaxSamples = 500,
                DisplayMode = ChartDisplayMode.Stacked,
            };
            tabChart.Controls.Add(_chart);

            _rtbData = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(16, 16, 22),
                ForeColor = Color.FromArgb(220, 220, 235),
                Font = new Font("Cascadia Code", 9.5f),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
            };
            tabData.Controls.Add(_rtbData);

            _tabView.TabPages.Add(tabChart);
            _tabView.TabPages.Add(tabData);
            _rightSplit.Panel1.Controls.Add(_tabView);

            // === Stream Info Panel (Bottom) ===
            var infoContainer = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(26, 26, 36),
            };

            var infoHeader = new Panel
            {
                Dock = DockStyle.Top,
                Height = 30,
                BackColor = Color.FromArgb(32, 32, 44),
                Padding = new Padding(8, 5, 8, 5),
            };
            var lblInfo = new Label
            {
                Text = "ℹ️ Seçili Stream Detayları ve Metadata",
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                ForeColor = Color.FromArgb(100, 180, 255),
                AutoSize = true,
                Location = new Point(8, 5),
            };
            infoHeader.Controls.Add(lblInfo);

            _rtbInfo = new RichTextBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 28),
                ForeColor = Color.FromArgb(210, 210, 225),
                Font = new Font("Cascadia Code", 9f),
                ReadOnly = true,
                BorderStyle = BorderStyle.None,
                WordWrap = true,
            };
            infoContainer.Controls.Add(_rtbInfo);
            infoContainer.Controls.Add(infoHeader);
            _rightSplit.Panel2.Controls.Add(infoContainer);

            rightContainer.Controls.Add(_rightSplit);

            // ========== STATUS BAR ==========
            _statusBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 30,
                BackColor = Color.FromArgb(30, 30, 40),
                Padding = new Padding(12, 5, 12, 5),
            };

            _lblStatus = new Label
            {
                Text = "● Bağlantı yok",
                ForeColor = Color.FromArgb(170, 170, 185),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(12, 6),
            };
            _statusBar.Controls.Add(_lblStatus);

            _lblSampleRate = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(46, 204, 113),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(280, 6),
            };
            _statusBar.Controls.Add(_lblSampleRate);

            _lblSampleCount = new Label
            {
                Text = "",
                ForeColor = Color.FromArgb(160, 175, 190),
                Font = new Font("Segoe UI", 9f),
                AutoSize = true,
                Location = new Point(480, 6),
            };
            _statusBar.Controls.Add(_lblSampleCount);

            // WinForms docking order: Center/Fill first, then edges
            Controls.Add(rightContainer);
            Controls.Add(_leftPanel);
            Controls.Add(_statusBar);
            Controls.Add(_topBar);

            // Position right-aligned test button on load & resize
            Load += (s, e) => RepositionTopButtons();
            Resize += (s, e) => RepositionTopButtons();
        }

        private void RepositionTopButtons()
        {
            if (_btnTestStream != null && _topBar != null)
            {
                _btnTestStream.Left = _topBar.ClientSize.Width - _btnTestStream.Width - 14;
            }
        }

        #endregion

        #region UI Helpers

        private Button CreateButton(string text, int x, int y, Color bgColor, int width)
        {
            var btn = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgColor,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void TabView_DrawItem(object sender, DrawItemEventArgs e)
        {
            var tab = _tabView.TabPages[e.Index];
            bool isSelected = (_tabView.SelectedIndex == e.Index);

            using var bgBrush = new SolidBrush(isSelected
                ? Color.FromArgb(36, 36, 50) : Color.FromArgb(26, 26, 34));
            e.Graphics.FillRectangle(bgBrush, e.Bounds);

            using var textBrush = new SolidBrush(isSelected
                ? Color.FromArgb(80, 210, 130) : Color.FromArgb(160, 160, 175));
            using var font = new Font("Segoe UI", 9.5f, isSelected ? FontStyle.Bold : FontStyle.Regular);
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            e.Graphics.DrawString(tab.Text, font, textBrush, e.Bounds, sf);
        }

        #endregion

        #region Continuous Stream Discovery

        private void StartDiscovery()
        {
            try
            {
                _resolver = new ContinuousResolver(forget_after: 5.0);
            }
            catch (Exception ex)
            {
                _lblDiscovery.Text = $"Resolver hatası: {ex.Message}";
                _lblDiscovery.ForeColor = Color.FromArgb(231, 76, 60);
            }

            // Periodic discovery update (zero wait since resolver runs in background)
            _discoveryTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            _discoveryTimer.Tick += (s, e) => RefreshStreamsList();
            _discoveryTimer.Start();

            // Stats calculation timer (1 Hz)
            _statsTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            _statsTimer.Tick += StatsTimer_Tick;
            _statsTimer.Start();

            // Chart refresh timer (~30 FPS)
            _chartRefreshTimer = new System.Windows.Forms.Timer { Interval = 33 };
            _chartRefreshTimer.Tick += (s, e) =>
            {
                if (_reader != null && _reader.IsRunning && !_chart.IsPaused)
                    _chart.Invalidate();
            };
            _chartRefreshTimer.Start();

            RefreshStreamsList();
        }

        private void RefreshStreamsList()
        {
            if (_resolver == null) return;

            try
            {
                var results = _resolver.results();

                // Check if list changed to avoid flicker
                string selectedUid = null;
                if (_lvStreams.SelectedItems.Count > 0)
                    selectedUid = _lvStreams.SelectedItems[0].Tag as string;

                var newUids = results.Select(r => r.uid()).ToList();
                var oldUids = _discoveredStreams.Select(s => s.uid()).ToList();

                bool changed = newUids.Count != oldUids.Count || !newUids.SequenceEqual(oldUids);

                if (changed || _lvStreams.Items.Count == 0)
                {
                    _discoveredStreams.Clear();
                    _discoveredStreams.AddRange(results);

                    _lvStreams.BeginUpdate();
                    _lvStreams.Items.Clear();

                    foreach (var info in results)
                    {
                        string uid = info.uid();
                        string name = info.name();
                        string type = info.type();
                        int ch = info.channel_count();
                        double rate = info.nominal_srate();
                        string rateStr = rate > 0 ? rate.ToString("F0") : "IRR";

                        var item = new ListViewItem(name);
                        item.SubItems.Add(type);
                        item.SubItems.Add(ch.ToString());
                        item.SubItems.Add(rateStr);
                        item.Tag = uid;
                        _lvStreams.Items.Add(item);

                        if (uid == selectedUid)
                            item.Selected = true;
                    }
                    _lvStreams.EndUpdate();
                }

                _lblDiscovery.Text = $"{results.Length} stream aktif";
                _lblDiscovery.ForeColor = results.Length > 0
                    ? Color.FromArgb(80, 210, 130)
                    : Color.FromArgb(150, 150, 170);
            }
            catch (Exception ex)
            {
                _lblDiscovery.Text = $"Hata: {ex.Message}";
                _lblDiscovery.ForeColor = Color.FromArgb(231, 76, 60);
            }
        }

        private void LvStreams_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_lvStreams.SelectedItems.Count == 0) return;

            int idx = _lvStreams.SelectedIndices[0];
            if (idx >= 0 && idx < _discoveredStreams.Count)
            {
                ShowStreamInfo(_discoveredStreams[idx]);
            }
        }

        private void ShowStreamInfo(StreamInfo info)
        {
            _rtbInfo.Clear();

            string name = info.name();
            string type = info.type();
            int ch = info.channel_count();
            double rate = info.nominal_srate();
            string uid = info.uid();
            string sourceId = info.source_id();
            string hostname = info.hostname();
            string fmt = info.channel_format().ToString();

            AppendInfoLine("Stream Adı", name, Color.FromArgb(80, 210, 130));
            AppendInfoLine("Tip (Type)", type, Color.FromArgb(100, 180, 255));
            AppendInfoLine("Kanal Sayısı", ch.ToString(), Color.FromArgb(241, 196, 15));
            AppendInfoLine("Örnekleme Hızı", rate > 0 ? $"{rate:F1} Hz" : "Düzensiz (Irregular / Event)", Color.FromArgb(230, 126, 34));
            AppendInfoLine("Veri Formatı", fmt, Color.FromArgb(175, 122, 197));
            AppendInfoLine("Kaynak ID", string.IsNullOrEmpty(sourceId) ? "(Yok)" : sourceId, Color.FromArgb(160, 175, 190));
            AppendInfoLine("Hostname", hostname, Color.FromArgb(160, 175, 190));
            AppendInfoLine("UID", uid, Color.FromArgb(120, 120, 140));

            // Extract channel metadata from XML if available
            try
            {
                var chNode = info.desc().child("channels").child("channel");
                int chIndex = 1;
                while (!chNode.empty() && chIndex <= 32)
                {
                    string label = chNode.child_value("label");
                    string unit = chNode.child_value("unit");
                    string chType = chNode.child_value("type");

                    string desc = $"Kanal {chIndex}";
                    string val = label;
                    if (!string.IsNullOrEmpty(unit)) val += $" [{unit}]";
                    if (!string.IsNullOrEmpty(chType)) val += $" ({chType})";

                    if (!string.IsNullOrEmpty(label))
                        AppendInfoLine(desc, val, Color.FromArgb(46, 204, 113));

                    chNode = chNode.next_sibling("channel");
                    chIndex++;
                }
            }
            catch { }
        }

        private void AppendInfoLine(string label, string value, Color valueColor)
        {
            int start = _rtbInfo.TextLength;
            string labelText = $"  {label}: ";
            _rtbInfo.AppendText(labelText);
            _rtbInfo.Select(start, labelText.Length);
            _rtbInfo.SelectionColor = Color.FromArgb(140, 140, 160);

            start = _rtbInfo.TextLength;
            _rtbInfo.AppendText(value + "\n");
            _rtbInfo.Select(start, value.Length);
            _rtbInfo.SelectionColor = valueColor;
            _rtbInfo.SelectionFont = new Font("Cascadia Code", 9.5f, FontStyle.Bold);
        }

        #endregion

        #region Connect / Disconnect

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            if (_lvStreams.SelectedItems.Count == 0)
            {
                MessageBox.Show("Lütfen bağlanmak istediğiniz stream'i sol panelden seçin.",
                    "Stream Seçilmedi", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int idx = _lvStreams.SelectedIndices[0];
            if (idx < 0 || idx >= _discoveredStreams.Count) return;

            ConnectToStream(_discoveredStreams[idx]);
        }

        private void ConnectToStream(StreamInfo info)
        {
            DisconnectStream();

            _activeStreamInfo = info;
            string name = info.name();
            string type = info.type();
            var fmt = info.channel_format();
            _isStringStream = (fmt == channel_format_t.cf_string);

            _reader = new StreamReaderWorker();

            // Extract channel labels from XML if available
            int chCount = info.channel_count();
            string[] channelLabels = new string[chCount];
            try
            {
                var chNode = info.desc().child("channels").child("channel");
                int i = 0;
                while (!chNode.empty() && i < chCount)
                {
                    string lbl = chNode.child_value("label");
                    channelLabels[i] = !string.IsNullOrEmpty(lbl) ? lbl : $"Ch {i + 1}";
                    chNode = chNode.next_sibling("channel");
                    i++;
                }
                while (i < chCount)
                {
                    channelLabels[i] = $"Ch {i + 1}";
                    i++;
                }
            }
            catch
            {
                for (int i = 0; i < chCount; i++) channelLabels[i] = $"Ch {i + 1}";
            }

            if (!_isStringStream)
            {
                _chart.SetupChannels(chCount, channelLabels);
                _reader.DirectChartSink = values => _chart.AddSample(values);
                _reader.NumericSampleReceived += OnNumericSample;
                _tabView.SelectedIndex = 0; // Show chart tab
            }
            else
            {
                _reader.StringSampleReceived += OnStringSample;
                _tabView.SelectedIndex = 1; // Show data log tab
            }

            _reader.ErrorOccurred += OnError;
            _reader.Disconnected += OnDisconnected;

            ClearData();
            _reader.Start(info);
            _lastSampleCount = 0;
            _lastStatTime = DateTime.UtcNow;

            _btnConnect.Enabled = false;
            _btnDisconnect.Enabled = true;
            _lblStatus.Text = $"● Bağlı: {name} ({type}, {chCount} kanal, {fmt})";
            _lblStatus.ForeColor = Color.FromArgb(46, 204, 113);

            LogData($"[{DateTime.Now:HH:mm:ss}] Stream'e başarıyla bağlandı: {name} ({type})",
                Color.FromArgb(80, 210, 130));
        }

        private void BtnDisconnect_Click(object sender, EventArgs e)
        {
            DisconnectStream();
        }

        private void DisconnectStream()
        {
            if (_reader != null)
            {
                _reader.Stop();
                _reader.NumericSampleReceived -= OnNumericSample;
                _reader.StringSampleReceived -= OnStringSample;
                _reader.ErrorOccurred -= OnError;
                _reader.Disconnected -= OnDisconnected;
                _reader.DirectChartSink = null;
                _reader.Dispose();
                _reader = null;
            }

            _btnConnect.Enabled = true;
            _btnDisconnect.Enabled = false;
            _lblStatus.Text = "● Bağlantı yok";
            _lblStatus.ForeColor = Color.FromArgb(170, 170, 185);
            _lblSampleRate.Text = "";
            _lblSampleCount.Text = "";
        }

        #endregion

        #region Chart & Toolbar Controls

        private void BtnPause_Click(object sender, EventArgs e)
        {
            _chart.IsPaused = !_chart.IsPaused;
            if (_chart.IsPaused)
            {
                _btnPause.Text = "▶ Devam";
                _btnPause.BackColor = Color.FromArgb(39, 174, 96);
            }
            else
            {
                _btnPause.Text = "⏸ Dondur";
                _btnPause.BackColor = Color.FromArgb(211, 84, 0);
            }
        }

        private void BtnModeToggle_Click(object sender, EventArgs e)
        {
            if (_chart.DisplayMode == ChartDisplayMode.Stacked)
            {
                _chart.DisplayMode = ChartDisplayMode.Overlaid;
                _btnModeToggle.Text = "🔀 Görünüm: Üst Üste";
            }
            else
            {
                _chart.DisplayMode = ChartDisplayMode.Stacked;
                _btnModeToggle.Text = "☰ Görünüm: Ayrık";
            }
        }

        private void BtnTestStream_Click(object sender, EventArgs e)
        {
            if (_testGenerator == null || !_testGenerator.IsRunning)
            {
                _testGenerator = new TestStreamGenerator();
                _testGenerator.Start();
                _btnTestStream.Text = "🛑 Test Stream Durdur";
                _btnTestStream.BackColor = Color.FromArgb(192, 57, 43);
                LogData($"[{DateTime.Now:HH:mm:ss}] Test Simülasyon Stream'i başlatıldı (Test-SineWave-EEG, 4 Ch @ 100 Hz).",
                    Color.FromArgb(175, 122, 197));
                RefreshStreamsList();
            }
            else
            {
                _testGenerator.Stop();
                _testGenerator.Dispose();
                _testGenerator = null;
                _btnTestStream.Text = "🧪 Test Stream Başlat";
                _btnTestStream.BackColor = Color.FromArgb(142, 68, 173);
                LogData($"[{DateTime.Now:HH:mm:ss}] Test Simülasyon Stream'i durduruldu.",
                    Color.FromArgb(230, 126, 34));
                RefreshStreamsList();
            }
        }

        #endregion

        #region Data Handling & Logging

        private void OnNumericSample(double[] values, double timestamp)
        {
            // Only format and display text when log tab is visible
            if (_tabView.SelectedIndex == 1)
            {
                string ts = DateTime.Now.ToString("HH:mm:ss.fff");
                string valStr = string.Join(", ", values.Select(v => v.ToString("F3")));
                LogData($"[{ts}] {valStr}", Color.FromArgb(190, 215, 240));
            }
        }

        private void OnStringSample(string[] values, double timestamp)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss.fff");
            string valStr = string.Join(" | ", values);
            LogData($"[{ts}] 🏷 MARKER: {valStr}", Color.FromArgb(46, 204, 113));
        }

        private void OnError(string message)
        {
            LogData($"[HATA] {message}", Color.FromArgb(231, 76, 60));
        }

        private void OnDisconnected()
        {
            LogData($"[{DateTime.Now:HH:mm:ss}] Stream bağlantısı sonlandı.", Color.FromArgb(241, 196, 15));
            _btnConnect.Enabled = true;
            _btnDisconnect.Enabled = false;
            _lblStatus.Text = "● Bağlantı sonlandı";
            _lblStatus.ForeColor = Color.FromArgb(241, 196, 15);
        }

        private void LogData(string message, Color color)
        {
            if (_rtbData.InvokeRequired)
            {
                _rtbData.BeginInvoke(new Action(() => LogData(message, color)));
                return;
            }

            int start = _rtbData.TextLength;
            string line = message + "\n";
            _rtbData.AppendText(line);

            try
            {
                _rtbData.Select(start, line.Length);
                _rtbData.SelectionColor = color;
                _rtbData.Select(_rtbData.TextLength, 0);

                if (_chkAutoScroll.Checked)
                    _rtbData.ScrollToCaret();
            }
            catch { }

            // Limit buffer lines
            if (_rtbData.Lines.Length > 2500)
            {
                try
                {
                    int removeEnd = _rtbData.GetFirstCharIndexFromLine(500);
                    if (removeEnd > 0)
                    {
                        _rtbData.ReadOnly = false;
                        _rtbData.Select(0, removeEnd);
                        _rtbData.SelectedText = "";
                        _rtbData.ReadOnly = true;
                        _rtbData.Select(_rtbData.TextLength, 0);
                    }
                }
                catch { }
            }
        }

        private void ClearData()
        {
            _rtbData.Clear();
            _chart.Clear();
        }

        #endregion

        #region Stats Calculation

        private void StatsTimer_Tick(object sender, EventArgs e)
        {
            if (_reader == null || !_reader.IsRunning) return;

            long current = _reader.SamplesReceived;
            var now = DateTime.UtcNow;
            double elapsed = (now - _lastStatTime).TotalSeconds;

            if (elapsed > 0.5)
            {
                double rate = (current - _lastSampleCount) / elapsed;
                _lblSampleRate.Text = $"⚡ {rate:F1} sample/s";
                _lastSampleCount = current;
                _lastStatTime = now;
            }

            _lblSampleCount.Text = $"Toplam: {current:N0} sample";
        }

        #endregion

        #region Form Cleanup

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _discoveryTimer?.Stop();
            _statsTimer?.Stop();
            _chartRefreshTimer?.Stop();

            DisconnectStream();

            if (_testGenerator != null)
            {
                _testGenerator.Stop();
                _testGenerator.Dispose();
                _testGenerator = null;
            }

            try { _resolver?.Dispose(); } catch { }
            _resolver = null;

            base.OnFormClosing(e);
        }

        #endregion
    }
}
