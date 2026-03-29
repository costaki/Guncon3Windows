using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GunconUSB;

namespace Guncon3Console.Calibration
{
    public sealed class TestWindow : Form
    {
        private readonly Timer _poll;
        private readonly GunconDevice _gun1;
        private readonly GunconDevice _gun2;
        private Dictionary<GunButton, bool> _btn1 = new Dictionary<GunButton, bool>();
        private Dictionary<GunButton, bool> _btn2 = new Dictionary<GunButton, bool>();
        private short _x1, _y1, _x2, _y2;
        private byte _rx1, _ry1, _rx2, _ry2;
        private byte _lx1, _ly1, _lx2, _ly2;
        private bool _ind2_1, _ind2_2;
        private bool _reading;
        private byte[] _dec1;
        private byte[] _dec2;
        private byte[] _lastDec1;
        private byte[] _lastDec2;

        public TestWindow(GunconDevice gun1, GunconDevice gun2)
        {
            _gun1 = gun1;
            _gun2 = gun2;

            Text = "Guncon3 Test";
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(980, 700);
            MinimumSize = new Size(980, 700);
            BackColor = Color.Black;
            ForeColor = Color.White;
            DoubleBuffered = true;
            KeyPreview = true;

            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            _poll = new Timer { Interval = 33 };
            _poll.Tick += (_, __) =>
            {
                if (_reading) return;
                _reading = true;

                System.Threading.ThreadPool.QueueUserWorkItem(__state =>
                {
                    if (_gun1 != null)
                    {
                        try { _gun1.Read(out _btn1, out _x1, out _y1, out _ind2_1); } catch { }
                        try { _gun1.TryReadDecoded(out _dec1); } catch { }
                        if (_dec1 != null && _dec1.Length > 3)
                        {
                            _ly1 = _dec1[2];
                            _lx1 = _dec1[3];

                            _ry1 = _dec1[0];
                            _rx1 = _dec1[1];
                        }
                    }
                    if (_gun2 != null)
                    {
                        try { _gun2.Read(out _btn2, out _x2, out _y2, out _ind2_2); } catch { }
                        try { _gun2.TryReadDecoded(out _dec2); } catch { }
                        if (_dec2 != null && _dec2.Length > 3)
                        {
                            _ly2 = _dec2[2];
                            _lx2 = _dec2[3];

                            _ry2 = _dec2[0];
                            _rx2 = _dec2[1];
                        }
                    }

                    try
                    {
                        BeginInvoke((Action)(() =>
                        {
                            _reading = false;
                            Invalidate();
                        }));
                    }
                    catch
                    {
                        _reading = false;
                    }
                });
            };
            _poll.Start();

            FormClosed += (_, __) =>
            {
                try { _poll.Stop(); } catch { }
                try { _poll.Dispose(); } catch { }
                // Do not dispose of devices
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using (var fTitle = new Font(FontFamily.GenericSansSerif, 18f, FontStyle.Bold))
            using (var f = new Font(FontFamily.GenericSansSerif, 12f, FontStyle.Regular))
            using (var b = new SolidBrush(ForeColor))
            {
                g.DrawString("TEST MODE (ESC = exit)", fTitle, b, new PointF(20, 20));

                var pad = 20;
                var top = 70;
                var gap = 40;
                var w = (ClientSize.Width - pad * 2 - gap) / 2;
                var h = ClientSize.Height - top - pad;
                DrawGunPanel(g, "GUN 1", new Rectangle(pad, top, w, h), _btn1, _x1, _y1, _lx1, _ly1, _rx1, _ry1, _ind2_1, _dec1, ref _lastDec1, f);
                DrawGunPanel(g, "GUN 2", new Rectangle(pad + w + gap, top, w, h), _btn2, _x2, _y2, _lx2, _ly2, _rx2, _ry2, _ind2_2, _dec2, ref _lastDec2, f);
            }
        }

        private static void DrawGunPanel(Graphics g, string title, Rectangle r, Dictionary<GunButton, bool> btn, short x, short y, byte lx, byte ly, byte rx, byte ry, bool ind2, byte[] dec, ref byte[] lastDec, Font f)
        {
            using (var pen = new Pen(Color.DimGray, 2f))
            using (var bTitle = new SolidBrush(Color.Yellow))
            using (var bText = new SolidBrush(Color.White))
            using (var bOn = new SolidBrush(Color.Lime))
            using (var bWarn = new SolidBrush(Color.Orange))
            {
                g.DrawRectangle(pen, r);
                g.DrawString(title, f, bTitle, new PointF(r.X + 10, r.Y + 10));

                g.DrawString($"ABS: X={x} Y={y}", f, bText, new PointF(r.X + 10, r.Y + 38));
                g.DrawString($"LStick: X={lx} Y={ly}", f, bText, new PointF(r.X + 10, r.Y + 60));
                g.DrawString($"RStick: X={rx} Y={ry}", f, bText, new PointF(r.X + 10, r.Y + 82));
                g.DrawString($"InsideScreen: {!ind2}", f, bText, new PointF(r.X + 10, r.Y + 104));

                var values = Enum.GetValues(typeof(GunButton)).Cast<GunButton>().ToList();

                float y0 = r.Y + 136;
                float lineH = 18;
                for (int i = 0; i < values.Count; i++)
                {
                    var b = values[i];
                    bool on = btn.TryGetValue(b, out var v) && v;
                    var brush = on ? bOn : bText;
                    g.DrawString($"{b}: {(on ? "ON" : "off")}", f, brush, new PointF(r.X + 10, y0 + i * lineH));
                }

                float yRaw = y0 + values.Count * lineH + 8;
                if (dec == null || dec.Length == 0)
                {
                    g.DrawString("RAW: (no decoded data)", f, bWarn, new PointF(r.X + 10, yRaw));
                    return;
                }

                if (lastDec == null || lastDec.Length != dec.Length)
                    lastDec = (byte[])dec.Clone();

                // Raw dump of decoded[0..12]
                // Split across multiple lines to fit the panel.
                string raw0 = string.Join(" ", Enumerable.Range(0, Math.Min(7, dec.Length)).Select(i => dec[i].ToString("X2")));
                string raw1 = (dec.Length > 7)
                    ? string.Join(" ", Enumerable.Range(7, Math.Min(6, dec.Length - 7)).Select(i => dec[i].ToString("X2")))
                    : "";

                g.DrawString("RAW[0..6]:  " + raw0, f, bText, new PointF(r.X + 10, yRaw));
                yRaw += lineH;
                if (!string.IsNullOrEmpty(raw1))
                {
                    g.DrawString("RAW[7..12]: " + raw1, f, bText, new PointF(r.X + 10, yRaw));
                    yRaw += lineH;
                }

                // Report any changed bits in any decoded byte
                var changed = new List<string>();
                int n = Math.Min(dec.Length, lastDec.Length);
                for (int i = 0; i < n; i++)
                {
                    byte c = (byte)(dec[i] ^ lastDec[i]);
                    if (c != 0)
                        changed.Add(i.ToString() + ":" + c.ToString("X2"));
                    lastDec[i] = dec[i];
                }

                if (changed.Count > 0 && yRaw < (r.Bottom - lineH))
                {
                    string chLine = string.Join(" ", changed);
                    if (chLine.Length > 48)
                        chLine = chLine.Substring(0, 48) + "…";
                    g.DrawString("CHANGED: " + chLine, f, bWarn, new PointF(r.X + 10, yRaw));
                    yRaw += lineH;
                }

                // Unknown bits in the bytes we currently treat as button/indicator bytes
                // decoded[10] bits 7,6 (AClick/BClick)
                // decoded[11] bits 7,5,4,3,2,1 (C1,Trigger,IND1,IND2,B1,B2)
                // decoded[12] bits 3,2,1 (C2,A1,A2)
                if (dec.Length >= 13 && yRaw < (r.Bottom - lineH))
                {
                    byte unk10 = (byte)(dec[10] & ~(0x80 | 0x40));
                    byte unk11 = (byte)(dec[11] & ~(0x80 | 0x20 | 0x10 | 0x08 | 0x04 | 0x02));
                    byte unk12 = (byte)(dec[12] & ~(0x08 | 0x04 | 0x02));
                    if ((unk10 | unk11 | unk12) != 0)
                        g.DrawString($"UNKNOWN btn bits: 10:{unk10:X2} 11:{unk11:X2} 12:{unk12:X2}", f, bWarn, new PointF(r.X + 10, yRaw));
                }
            }
        }
    }
}
