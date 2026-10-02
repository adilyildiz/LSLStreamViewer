using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LSLStreamViewer
{
    public enum ChartDisplayMode
    {
        Overlaid,   // All channels on the same coordinate axis
        Stacked     // Each channel has its own horizontal lane (EEG style)
    }

    /// <summary>
    /// High-performance real-time line chart control for displaying multi-channel LSL stream data.
    /// Features:
    /// - Overlaid and Stacked (EEG ribbon) display modes
    /// - Dynamic auto-scaling based on currently visible window
    /// - Pause / Freeze view for signal inspection
    /// - Multi-channel color coding and legend
    /// - Thread-safe sample ingestion
    /// </summary>
    public class StreamChartControl : Control
    {
        private readonly object _lock = new object();
        private readonly List<Color> _channelColors = new List<Color>();
        private readonly List<Queue<double>> _channelData = new List<Queue<double>>();
        private int _maxSamples = 500;
        private int _channelCount;
        private string[] _channelLabels = Array.Empty<string>();

        private ChartDisplayMode _displayMode = ChartDisplayMode.Stacked;
        private bool _autoScale = true;
        private bool _showGrid = true;
        private bool _showLegend = true;
        private bool _isPaused = false;

        private static readonly Color[] DefaultColors = new[]
        {
            Color.FromArgb(46, 204, 113),   // Emerald Green
            Color.FromArgb(52, 152, 219),   // Electric Blue
            Color.FromArgb(231, 76, 60),    // Coral Red
            Color.FromArgb(241, 196, 15),   // Sunburst Yellow
            Color.FromArgb(175, 122, 197),  // Soft Violet
            Color.FromArgb(243, 156, 18),   // Orange Amber
            Color.FromArgb(26, 188, 156),   // Turquoise
            Color.FromArgb(255, 107, 129),  // Neon Rose
            Color.FromArgb(70, 200, 248),   // Sky Cyan
            Color.FromArgb(180, 210, 80),   // Lime
            Color.FromArgb(255, 159, 243),  // Pastel Pink
            Color.FromArgb(84, 160, 255),   // Azure
            Color.FromArgb(254, 202, 87),   // Marigold
            Color.FromArgb(29, 209, 161),   // Mint
            Color.FromArgb(200, 214, 229),  // Pearl
            Color.FromArgb(255, 107, 107),  // Light Salmon
        };

        public int MaxSamples
        {
            get => _maxSamples;
            set
            {
                _maxSamples = Math.Max(50, Math.Min(10000, value));
                lock (_lock)
                {
                    TrimData();
                }
                Invalidate();
            }
        }

        public ChartDisplayMode DisplayMode
        {
            get => _displayMode;
            set
            {
                _displayMode = value;
                Invalidate();
            }
        }

        public bool AutoScale
        {
            get => _autoScale;
            set { _autoScale = value; Invalidate(); }
        }

        public bool ShowGrid
        {
            get => _showGrid;
            set { _showGrid = value; Invalidate(); }
        }

        public bool ShowLegend
        {
            get => _showLegend;
            set { _showLegend = value; Invalidate(); }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set { _isPaused = value; Invalidate(); }
        }

        public StreamChartControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            BackColor = Color.FromArgb(18, 18, 24);
            ForeColor = Color.FromArgb(200, 200, 210);
            Font = new Font("Segoe UI", 8.5f);
        }

        public void SetupChannels(int channelCount, string[] labels = null)
        {
            lock (_lock)
            {
                _channelCount = channelCount;
                _channelLabels = labels != null && labels.Length == channelCount
                    ? labels
                    : Enumerable.Range(1, channelCount).Select(i => $"Ch {i}").ToArray();

                _channelData.Clear();
                _channelColors.Clear();

                for (int i = 0; i < channelCount; i++)
                {
                    _channelData.Add(new Queue<double>());
                    _channelColors.Add(DefaultColors[i % DefaultColors.Length]);
                }
            }
            Invalidate();
        }

        public void AddSample(double[] values)
        {
            if (values == null) return;
            lock (_lock)
            {
                int count = Math.Min(values.Length, _channelData.Count);
                for (int i = 0; i < count; i++)
                {
                    _channelData[i].Enqueue(values[i]);
                }
                TrimData();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                foreach (var q in _channelData)
                    q.Clear();
            }
            Invalidate();
        }

        private void TrimData()
        {
            foreach (var q in _channelData)
            {
                while (q.Count > _maxSamples)
                    q.Dequeue();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int margin = 55;
            int rightMargin = _showLegend ? 130 : 16;
            int topMargin = 16;
            int bottomMargin = 26;

            var plotRect = new Rectangle(margin, topMargin,
                Math.Max(10, Width - margin - rightMargin),
                Math.Max(10, Height - topMargin - bottomMargin));

            // Background
            using (var bgBrush = new SolidBrush(Color.FromArgb(20, 20, 28)))
                g.FillRectangle(bgBrush, plotRect);

            lock (_lock)
            {
                if (_channelData.Count == 0 || _channelData.All(q => q.Count == 0))
                {
                    DrawEmptyMessage(g, plotRect);
                    DrawBorder(g, plotRect);
                    return;
                }

                if (_displayMode == ChartDisplayMode.Stacked && _channelData.Count > 1)
                {
                    DrawStacked(g, plotRect);
                }
                else
                {
                    DrawOverlaid(g, plotRect);
                }

                // Legend
                if (_showLegend && _channelData.Count > 0)
                {
                    DrawLegend(g, plotRect);
                }

                // Pause indicator
                if (_isPaused)
                {
                    using var pauseBg = new SolidBrush(Color.FromArgb(180, 231, 76, 60));
                    using var pauseFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                    string pauseText = "⏸ DONDURULDU (PAUSED)";
                    var sz = g.MeasureString(pauseText, pauseFont);
                    var pauseRect = new RectangleF(plotRect.Right - sz.Width - 14, plotRect.Top + 6, sz.Width + 10, sz.Height + 4);
                    g.FillRectangle(pauseBg, pauseRect);
                    using var txtBrush = new SolidBrush(Color.White);
                    g.DrawString(pauseText, pauseFont, txtBrush, pauseRect.Left + 5, pauseRect.Top + 2);
                }

                DrawBorder(g, plotRect);
            }
        }

        private void DrawOverlaid(Graphics g, Rectangle plotRect)
        {
            // Find global min / max across all channels in current window
            double yMin = double.MaxValue;
            double yMax = double.MinValue;

            for (int ch = 0; ch < _channelData.Count; ch++)
            {
                foreach (var v in _channelData[ch])
                {
                    if (v < yMin) yMin = v;
                    if (v > yMax) yMax = v;
                }
            }

            if (yMin >= yMax || double.IsInfinity(yMin) || double.IsInfinity(yMax))
            {
                yMin = -1;
                yMax = 1;
            }

            double yRange = yMax - yMin;
            if (yRange < 1e-10) { yRange = 2; yMin -= 1; yMax += 1; }
            yMin -= yRange * 0.05;
            yMax += yRange * 0.05;
            yRange = yMax - yMin;

            // Grid
            if (_showGrid)
            {
                using var gridPen = new Pen(Color.FromArgb(32, 32, 44), 1f) { DashStyle = DashStyle.Dot };
                int hLines = Math.Max(2, plotRect.Height / 50);
                using var labelBrush = new SolidBrush(Color.FromArgb(120, 120, 140));
                using var labelFont = new Font("Segoe UI", 7.5f);

                for (int i = 0; i <= hLines; i++)
                {
                    float y = plotRect.Top + (float)i / hLines * plotRect.Height;
                    g.DrawLine(gridPen, plotRect.Left, y, plotRect.Right, y);
                    double val = yMax - (double)i / hLines * yRange;
                    string label = FormatValue(val);
                    var sz = g.MeasureString(label, labelFont);
                    g.DrawString(label, labelFont, labelBrush, plotRect.Left - sz.Width - 4, y - sz.Height / 2);
                }

                int vLines = Math.Max(2, plotRect.Width / 80);
                for (int i = 0; i <= vLines; i++)
                {
                    float x = plotRect.Left + (float)i / vLines * plotRect.Width;
                    g.DrawLine(gridPen, x, plotRect.Top, x, plotRect.Bottom);
                }
            }

            // Zero line
            if (yMin < 0 && yMax > 0)
            {
                float zeroY = plotRect.Bottom - (float)((0 - yMin) / yRange) * plotRect.Height;
                using var zeroPen = new Pen(Color.FromArgb(60, 255, 255, 255), 1f) { DashStyle = DashStyle.Dash };
                g.DrawLine(zeroPen, plotRect.Left, zeroY, plotRect.Right, zeroY);
            }

            // Channels
            for (int ch = 0; ch < _channelData.Count; ch++)
            {
                var data = _channelData[ch].ToArray();
                if (data.Length < 2) continue;

                var points = new PointF[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    float x = plotRect.Left + (float)i / (_maxSamples - 1) * plotRect.Width;
                    float y = plotRect.Bottom - (float)((data[i] - yMin) / yRange) * plotRect.Height;
                    y = Math.Max(plotRect.Top, Math.Min(plotRect.Bottom, y));
                    points[i] = new PointF(x, y);
                }

                using var pen = new Pen(_channelColors[ch], 1.6f) { LineJoin = LineJoin.Round };
                try { g.DrawLines(pen, points); } catch { }
            }
        }

        private void DrawStacked(Graphics g, Rectangle plotRect)
        {
            int n = _channelData.Count;
            float laneHeight = (float)plotRect.Height / n;

            using var dividerPen = new Pen(Color.FromArgb(38, 38, 52), 1f);
            using var baselinePen = new Pen(Color.FromArgb(28, 45, 60), 1f) { DashStyle = DashStyle.Dash };
            using var tagFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);

            for (int ch = 0; ch < n; ch++)
            {
                float laneTop = plotRect.Top + ch * laneHeight;
                float laneBottom = laneTop + laneHeight;
                float laneMid = laneTop + laneHeight / 2f;

                // Lane divider
                if (ch > 0)
                    g.DrawLine(dividerPen, plotRect.Left, laneTop, plotRect.Right, laneTop);

                // Baseline
                g.DrawLine(baselinePen, plotRect.Left, laneMid, plotRect.Right, laneMid);

                // Channel tag on left
                string tag = (ch < _channelLabels?.Length) ? _channelLabels[ch] : $"Ch {ch + 1}";
                using (var brush = new SolidBrush(_channelColors[ch]))
                {
                    var sz = g.MeasureString(tag, tagFont);
                    g.DrawString(tag, tagFont, brush, plotRect.Left - sz.Width - 4, laneMid - sz.Height / 2);
                }

                // Data for channel
                var data = _channelData[ch].ToArray();
                if (data.Length < 2) continue;

                double chMin = data.Min();
                double chMax = data.Max();
                double chRange = chMax - chMin;
                if (chRange < 1e-10) chRange = 2;
                chMin -= chRange * 0.1;
                chMax += chRange * 0.1;
                chRange = chMax - chMin;

                var points = new PointF[data.Length];
                for (int i = 0; i < data.Length; i++)
                {
                    float x = plotRect.Left + (float)i / (_maxSamples - 1) * plotRect.Width;
                    float y = laneBottom - (float)((data[i] - chMin) / chRange) * laneHeight;
                    y = Math.Max(laneTop + 1, Math.Min(laneBottom - 1, y));
                    points[i] = new PointF(x, y);
                }

                using var pen = new Pen(_channelColors[ch], 1.4f) { LineJoin = LineJoin.Round };
                try { g.DrawLines(pen, points); } catch { }
            }
        }

        private void DrawLegend(Graphics g, Rectangle plotRect)
        {
            int legendX = plotRect.Right + 8;
            int legendY = plotRect.Top + 4;
            using var legendFont = new Font("Segoe UI", 8f);

            int maxVisible = Math.Min(_channelData.Count, (plotRect.Height - 8) / 18);
            for (int ch = 0; ch < maxVisible; ch++)
            {
                string label = (ch < _channelLabels?.Length) ? _channelLabels[ch] : $"Ch {ch + 1}";
                using var brush = new SolidBrush(_channelColors[ch]);
                g.FillRectangle(brush, legendX, legendY + ch * 18, 10, 10);
                using var textBrush = new SolidBrush(Color.FromArgb(190, 190, 205));
                g.DrawString(label, legendFont, textBrush, legendX + 14, legendY + ch * 18 - 2);
            }

            if (_channelData.Count > maxVisible)
            {
                using var moreBrush = new SolidBrush(Color.FromArgb(130, 130, 150));
                g.DrawString($"... +{_channelData.Count - maxVisible}", legendFont, moreBrush, legendX, legendY + maxVisible * 18);
            }
        }

        private void DrawBorder(Graphics g, Rectangle plotRect)
        {
            using var borderPen = new Pen(Color.FromArgb(50, 50, 65), 1f);
            g.DrawRectangle(borderPen, plotRect);
        }

        private void DrawEmptyMessage(Graphics g, Rectangle plotRect)
        {
            using var msgBrush = new SolidBrush(Color.FromArgb(110, 110, 130));
            using var msgFont = new Font("Segoe UI", 11f, FontStyle.Italic);
            string msg = "Stream bağlı değil veya veri bekleniyor...";
            var sz = g.MeasureString(msg, msgFont);
            g.DrawString(msg, msgFont, msgBrush,
                plotRect.Left + (plotRect.Width - sz.Width) / 2,
                plotRect.Top + (plotRect.Height - sz.Height) / 2);
        }

        private static string FormatValue(double val)
        {
            if (Math.Abs(val) >= 1000) return val.ToString("F0");
            if (Math.Abs(val) >= 1) return val.ToString("F2");
            if (Math.Abs(val) >= 0.01) return val.ToString("F4");
            return val.ToString("E1");
        }
    }
}
