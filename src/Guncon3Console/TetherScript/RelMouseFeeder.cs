using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;

namespace Guncon3Console.TetherScript
{
    internal sealed class RelMouseFeeder : IMouseFeeder, ITetherScriptFeeder
    {
        private readonly HIDController _hid = new HIDController();
        public HIDController Hid => _hid;

        private readonly Dictionary<GunButton, MouseButton> _mapping = new Dictionary<GunButton, MouseButton>();

        private byte _btns;

        public bool Force4by3 { get; set; } = false;

        // Tunables (servo)
        // Cursor error (in pixels) is multiplied by Kp to produce a relative delta.
        // Note: the output is clamped to MaxStepPx to stay within 8-bit deltas.
        public int Kp { get; set; } = 1;
        public int DeadzonePx { get; set; } = 1;
        public int MaxStepPx { get; set; } = 40;
        public int MinStepPx { get; set; } = 1; // minimum step applied when outside deadzone (helps overcome stickiness)
        public int SlewLimitPx { get; set; } = 25; // max change per frame of dx/dy (reduces flip-flop / jitter)
        public double TargetSmoothing { get; set; } = 0.25; // 0..1 EMA for target point (higher = less lag)
        public int DeadzoneUnlockPx { get; set; } = 4; // hysteresis: once settled, must exceed this to move again
        public int UpdateIntervalMs { get; set; } = 0; // optional pacing; 0 = no sleep
        public bool InvertX { get; set; } = false;
        public bool InvertY { get; set; } = false;

        private int _lastDx;
        private int _lastDy;

        private bool _settled;
        private double _fltTargetX;
        private double _fltTargetY;
        private bool _fltInit;

        private int _m1x, _m2x, _m3x;
        private int _m1y, _m2y, _m3y;
        private int _mCount;
        public RelMouseFeeder() { }

        public string Name => "TetherScript RelMouse";

        public ushort VendorId => (ushort)DriversConst.TTC_VENDORID;
        public ushort ProductId => (ushort)DriversConst.TTC_PRODUCTID_MOUSEREL;

        public bool IsConnected => _hid.Connected;

        public void Log(string message) => Console.WriteLine("[" + Name + "]: " + message);

        public void ClearMapping() => _mapping.Clear();

        public int MappingCount() => _mapping.Count;

        public void AddMapping(GunButton gunButton, dynamic mapping)
        {
            if (mapping is MouseButton btn)
                _mapping[gunButton] = btn;
        }

        public dynamic GetMapping(GunButton gunButton)
        {
            if (_mapping.TryGetValue(gunButton, out var v))
                return v;
            return null;
        }

        public void Connect()
        {
            _hid.OnLog += OnHidLog;
            _hid.VendorID = VendorId;
            _hid.ProductID = ProductId;
            _hid.Connect();

            if (!_hid.Connected)
                throw new Exception("Could not connect to TetherScript's RelMouse");

            _btns = 0;
        }

        public void Disconnect()
        {
            _hid.Disconnect();
            _hid.OnLog -= OnHidLog;
        }

        public void OnHidLog(object sender, LogArgs e) => Log(e.Msg);

        public void Feed(IGunState state)
        {
            // Buttons
            _btns = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) _btns = (byte)(_btns | 1);
                if (map.Value == MouseButton.Right) _btns = (byte)(_btns | (1 << 1));
                if (map.Value == MouseButton.Middle) _btns = (byte)(_btns | (1 << 2));
            }

            // If outside screen, send zero delta but still update buttons.
            if (!state.IsInsideScreen)
            {
                Send_Data_To_MouseRel(_btns, 0, 0);
                _lastDx = 0;
                _lastDy = 0;
                _settled = false;
                _fltInit = false;
                _mCount = 0;
                return;
            }

            var absX = state.ABS_X;
            if (Force4by3)
                absX = (short)Helper.ConvertRange(4096, 28671, 0, 32767, absX);

            var target = AbsToScreenTarget(absX, state.ABS_Y);
            var targetSm = SmoothTarget(target);
            targetSm = Median3(targetSm);
            var cur = GetCursorPosSafe();

            int ex = targetSm.X - cur.X;
            int ey = targetSm.Y - cur.Y;

            int dz = DeadzonePx;
            if (dz < 0) dz = 0;

            int unlock = DeadzoneUnlockPx;
            if (unlock < dz) unlock = dz;

            // Axis deadzone
            if (Math.Abs(ex) <= dz) ex = 0;
            if (Math.Abs(ey) <= dz) ey = 0;

            // Optional radial deadzone to reduce diagonal jitter near target.
            if (dz > 0 && ex != 0 && ey != 0)
            {
                long rr = (long)ex * ex + (long)ey * ey;
                if (rr <= (long)dz * dz)
                {
                    ex = 0;
                    ey = 0;
                }
            }

            // Hysteresis: once we settle, stay settled until we exceed the larger threshold.
            if (_settled)
            {
                if (Math.Abs(ex) <= unlock && Math.Abs(ey) <= unlock)
                {
                    Send_Data_To_MouseRel(_btns, 0, 0);
                    _lastDx = 0;
                    _lastDy = 0;
                    return;
                }
                _settled = false;
            }

            if (ex == 0 && ey == 0)
            {
                _settled = true;
                Send_Data_To_MouseRel(_btns, 0, 0);
                _lastDx = 0;
                _lastDy = 0;
                return;
            }

            int kp = Kp;
            if (kp < 1) kp = 1;

            int maxStep = MaxStepPx;
            if (maxStep < 1) maxStep = 1;
            if (maxStep > 127) maxStep = 127;

            int dx = Clamp(ex * kp, -maxStep, maxStep);
            int dy = Clamp(ey * kp, -maxStep, maxStep);

            int minStep = MinStepPx;
            if (minStep < 0) minStep = 0;
            if (minStep > 127) minStep = 127;

            // Ensure we actually move when outside deadzone.
            if (ex != 0 && dx == 0) dx = (ex > 0) ? minStep : -minStep;
            if (ey != 0 && dy == 0) dy = (ey > 0) ? minStep : -minStep;

            // Slew-rate limit to avoid rapid sign flipping between two adjacent pixels.
            int slew = SlewLimitPx;
            if (slew < 1) slew = 1;
            if (slew > 127) slew = 127;
            dx = Clamp(dx, _lastDx - slew, _lastDx + slew);
            dy = Clamp(dy, _lastDy - slew, _lastDy + slew);

            if (InvertX) dx = -dx;
            if (InvertY) dy = -dy;

            Send_Data_To_MouseRel(_btns, (short)dx, (short)dy);

            _lastDx = dx;
            _lastDy = dy;

            int sleep = UpdateIntervalMs;
            if (sleep > 0)
                Thread.Sleep(sleep);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static POINT AbsToScreenTarget(short absX, short absY)
        {
            // ABS is expected 0..32767 (see Program.ApplyRectCalib). Map to primary screen pixels.
            var b = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

            double nx = absX / 32767.0;
            double ny = absY / 32767.0;
            if (nx < 0) nx = 0; else if (nx > 1) nx = 1;
            if (ny < 0) ny = 0; else if (ny > 1) ny = 1;

            int x = b.Left + (int)Math.Round(nx * (b.Width - 1));
            int y = b.Top + (int)Math.Round(ny * (b.Height - 1));
            return new POINT { X = x, Y = y };
        }

        private POINT SmoothTarget(POINT raw)
        {
            double a = TargetSmoothing;
            if (a < 0) a = 0;
            if (a > 1) a = 1;
            if (a <= 0)
                return raw;

            if (!_fltInit)
            {
                _fltInit = true;
                _fltTargetX = raw.X;
                _fltTargetY = raw.Y;
                return raw;
            }

            // EMA: higher alpha follows raw more closely (less lag)
            _fltTargetX = _fltTargetX + a * (raw.X - _fltTargetX);
            _fltTargetY = _fltTargetY + a * (raw.Y - _fltTargetY);

            return new POINT { X = (int)Math.Round(_fltTargetX), Y = (int)Math.Round(_fltTargetY) };
        }

        private POINT Median3(POINT p)
        {
            // Median filter helps reject single-frame outliers that can cause large diagonal jumps.
            // It adds ~0-1 frame of latency in the worst case (usually not noticeable).
            _m3x = _m2x; _m2x = _m1x; _m1x = p.X;
            _m3y = _m2y; _m2y = _m1y; _m1y = p.Y;
            if (_mCount < 3) _mCount++;
            if (_mCount < 3) return p;
            return new POINT { X = Median(_m1x, _m2x, _m3x), Y = Median(_m1y, _m2y, _m3y) };
        }

        private static int Median(int a, int b, int c)
        {
            if (a > b) { int t = a; a = b; b = t; }
            if (b > c) { int t = b; b = c; c = t; }
            if (a > b) { int t = a; a = b; b = t; }
            return b;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        private static POINT GetCursorPosSafe()
        {
            try
            {
                POINT p;
                if (GetCursorPos(out p))
                    return p;
            }
            catch { }
            return new POINT();
        }

        private void Send_Data_To_MouseRel(byte buttons, short dx, short dy)
        {
            // Empirically validated via RelMouseProbe:
            // payload = [ReportID=1, CommandCode=2, Buttons, Dx8, Dy8]

            sbyte dx8 = ToSByte(dx);
            sbyte dy8 = ToSByte(dy);

            var buf = new byte[5];
            buf[0] = 1;
            buf[1] = 2;
            buf[2] = buttons;
            buf[3] = unchecked((byte)dx8);
            buf[4] = unchecked((byte)dy8);
            _hid.SendData(buf, (uint)buf.Length);
        }

        private static sbyte ToSByte(short v)
        {
            if (v < sbyte.MinValue) return sbyte.MinValue;
            if (v > sbyte.MaxValue) return sbyte.MaxValue;
            return (sbyte)v;
        }
    }
}