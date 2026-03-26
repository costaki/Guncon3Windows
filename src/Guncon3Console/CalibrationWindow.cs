using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;
using GunconUSB;

namespace Guncon3Console
{
    public class CalibrationWindow : Form
    {
        private readonly WinFormsTimer _poll;
        private readonly List<(double X, double Y)> _rawPoints = new List<(double X, double Y)>();
        private readonly string _savePath;
        private readonly string _label;
        private readonly GunconDevice _device;
        private readonly bool _ownsDevice;
        private readonly Dictionary<GunButton, bool> _btn = new Dictionary<GunButton, bool>();
        private short _absX;
        private short _absY;
        private PointF[] _targets = Array.Empty<PointF>();
        private int _idx = 0;
        private bool _prevTrig = false;
        private bool _prevA1 = false, _prevC2 = false;
        private bool _checking = false;
        private RectCalib _rectForCheck = null;

        public CalibrationWindow() : this(null, null, null)
        {
        }

        public CalibrationWindow(string savePath) : this(savePath, null, null)
        {
        }

        public CalibrationWindow(string savePath, string label) : this(savePath, label, null)
        {
        }

        public CalibrationWindow(string savePath, string label, GunconDevice device)
        {
            _savePath = savePath;
            _label = label;
            _device = device;
            _ownsDevice = false;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            Bounds = Screen.PrimaryScreen.Bounds;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.DimGray;
            ForeColor = Color.White;

            Shown += (_, __) => { try { Cursor.Hide(); } catch { } };
            FormClosed += (_, __) => { try { Cursor.Show(); } catch { } };

            KeyDown += CalibrationWindow_KeyDown;
            MouseDown += CalibrationWindow_MouseDown;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            UpdateStyles();

            RebuildTargets();
            Resize += (_, __) => { RebuildTargets(); Invalidate(); };

            if (_device == null)
                throw new ArgumentNullException(nameof(device), "CalibrationWindow requires a GunconDevice.");

            _poll = new WinFormsTimer { Interval = 16 };
            _poll.Tick += PollTick;
            _poll.Start();

            FormClosed += (_, __) =>
            {
                try { _poll?.Stop(); } catch { }
                try { _poll?.Dispose(); } catch { }
                if (_ownsDevice)
                {
                    try { _device?.Dispose(); } catch { }
                }
            };
        }

        private void ReadGunSafe()
        {
            try
            {
                _device.ReadInto(_btn, out _absX, out _absY, out var _);
            }
            catch { }
        }

        private bool IsDown(GunButton b)
        {
            return _btn.TryGetValue(b, out var v) && v;
        }

        private void CalibrationWindow_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && !_checking)
                CapturePointSafe();
        }

        private void CalibrationWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
                Close();
            else if (e.KeyCode == Keys.Space && !_checking)
                CapturePointSafe();
        }

        private void CapturePointSafe()
        {
            ReadGunSafe();

            try
            {
                double rawX = _absX;
                double rawY = _absY;
                CapturePoint(rawX, rawY);
            }
            catch (Exception ex) { DumpError("cal_capture_error.txt", ex); }
        }

        private void PollTick(object sender, EventArgs e)
        {
            ReadGunSafe();

            if (_checking)
            {
                bool a1 = IsDown(GunButton.A1);
                bool c2 = IsDown(GunButton.C2);

                if (!_prevA1 && a1)
                {
                    _checking = false;
                    _rectForCheck = null;
                    _rawPoints.Clear();
                    _idx = 0;
                    RebuildTargets();
                }
                else if (!_prevC2 && c2)
                {
                    Close();
                    return;
                }

                _prevA1 = a1;
                _prevC2 = c2;
            }
            else
            {
                bool t = IsDown(GunButton.Trigger);
                bool ac = IsDown(GunButton.AClick);
                bool bc = IsDown(GunButton.BClick);
                bool capture = t || ac || bc;
                if (!_prevTrig && capture)
                {
                    CapturePoint(_absX, _absY);
                }
                _prevTrig = capture;
            }

            Invalidate();
        }

        private void RebuildTargets()
        {
            int W = Math.Max(1, ClientSize.Width);
            int H = Math.Max(1, ClientSize.Height);
            _targets = new[]
            {
                new PointF(0,0),
                new PointF(W-1,0),
                new PointF(W-1,H-1),
                new PointF(0,H-1),
                new PointF(W/2f,H/2f)
            };
            _idx = 0;
        }

        private void CapturePoint(double rawX, double rawY)
        {
            if (_rawPoints.Count >= 5) return;
            _rawPoints.Add((rawX, rawY));
            _idx++;

            if (_idx < 5) { Invalidate(); return; }

            FinishAndSave();
        }

        private void FinishAndSave()
        {
            try
            {
                _poll.Stop();

                double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
                foreach (var p in _rawPoints)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }

                int W = Screen.PrimaryScreen.Bounds.Width;
                int H = Screen.PrimaryScreen.Bounds.Height;

                var rc = new RectCalib
                {
                    RawMinX = (int)Math.Round(minX),
                    RawMaxX = (int)Math.Round(maxX),
                    RawMinY = (int)Math.Round(minY),
                    RawMaxY = (int)Math.Round(maxY),
                    ScreenW = W,
                    ScreenH = H,
                    InvertY = true
                };

                rc.Save(_savePath);

                _rectForCheck = rc;
                _checking = true;
                _poll.Start();
            }
            catch (Exception ex)
            {
                DumpRaw("cal_raw_dump.txt", ex);
                MessageBox.Show("Calibration error: " + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var f = new Font(FontFamily.GenericSansSerif, 18f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.White))
            {
                if (!_checking)
                {
                    if (!string.IsNullOrWhiteSpace(_label))
                    {
                        using (var f2 = new Font(FontFamily.GenericSansSerif, 26f, FontStyle.Bold))
                        using (var b2 = new SolidBrush(Color.Yellow))
                            g.DrawString(_label, f2, b2, new PointF(20, 110));
                    }

                    g.DrawString("SHOOT THE MARK (5 POINTS). ESC = cancel / Space = capture", f, b, new PointF(20, 20));
                    g.DrawString("Progress: " + Math.Min(_idx + 1, 5) + "/5", f, b, new PointF(20, 46));

                    string rawLine = "RAW: X=0 Y=0 TRIG=off";
                    try
                    {
                        double px = _absX;
                        double py = _absY;
                        string trig = IsDown(GunButton.Trigger) ? "ON" : "off";
                        rawLine = $"RAW: X={px} Y={py} TRIG={trig}";
                    }
                    catch { }

                    g.DrawString(rawLine, f, b, new PointF(20, 72));

                    int k = Math.Min(_idx, _targets.Length - 1);
                    DrawTarget(g, k, _targets[k], ClientSize);
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(_label))
                    {
                        using (var f2 = new Font(FontFamily.GenericSansSerif, 26f, FontStyle.Bold))
                        using (var b2 = new SolidBrush(Color.Yellow))
                            g.DrawString(_label, f2, b2, new PointF(20, 70));
                    }

                    g.DrawString("CHECK CALIBRATION — A1 = recalibrate | C2 = save & exit", f, b, new PointF(20, 20));

                    double rx = _absX;
                    double ry = _absY;
                    var (px, py) = _rectForCheck.Map(rx, ry);

                    DrawCrosshair(g, (float)px, (float)py);
                }
            }
        }

        private static void DrawTarget(Graphics g, int k, PointF p, Size client)
        {
            using (var red = new SolidBrush(Color.Red))
            using (var penW = new Pen(Color.White, 3f))
            {
                float tri = Math.Min(client.Width, client.Height) * 0.05f;
                if (k <= 3)
                {
                    PointF a, b, c;
                    if (k == 0) { a = new PointF(p.X, p.Y); b = new PointF(p.X + tri, p.Y); c = new PointF(p.X, p.Y + tri); }
                    else if (k == 1) { a = new PointF(p.X, p.Y); b = new PointF(p.X - tri, p.Y); c = new PointF(p.X, p.Y + tri); }
                    else if (k == 2) { a = new PointF(p.X, p.Y); b = new PointF(p.X - tri, p.Y); c = new PointF(p.X, p.Y - tri); }
                    else { a = new PointF(p.X, p.Y); b = new PointF(p.X + tri, p.Y); c = new PointF(p.X, p.Y - tri); }
                    g.FillPolygon(red, new[] { a, b, c });
                    g.DrawPolygon(penW, new[] { a, b, c });
                }
                else
                {
                    float r = tri * 0.9f;
                    g.DrawEllipse(penW, p.X - r, p.Y - r, r * 2, r * 2);
                    g.DrawLine(penW, p.X - r * 1.7f, p.Y, p.X + r * 1.7f, p.Y);
                    g.DrawLine(penW, p.X, p.Y - r * 1.7f, p.X, p.Y + r * 1.7f);
                }
            }
        }

        private static void DrawCrosshair(Graphics g, float x, float y)
        {
            using (var pen = new Pen(Color.White, 2f))
            {
                const int arm = 20;
                const int gap = 4;
                g.DrawLine(pen, x - (arm + gap), y, x - gap, y);
                g.DrawLine(pen, x + gap, y, x + (arm + gap), y);
                g.DrawLine(pen, x, y - (arm + gap), x, y - gap);
                g.DrawLine(pen, x, y + gap, x, y + (arm + gap));
            }
        }

        private void DumpError(string file, Exception ex)
        {
            try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file), ex.ToString()); } catch { }
        }

        private void DumpRaw(string file, Exception ex)
        {
            try
            {
                string dump = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
                using (var sw = new StreamWriter(dump, false))
                {
                    sw.WriteLine("# Calibration error: " + ex.Message);
                    sw.WriteLine("# RAW:");
                    foreach (var p in _rawPoints)
                        sw.WriteLine($"{p.X},{p.Y}");
                }
            }
            catch { }
        }
    }
}
