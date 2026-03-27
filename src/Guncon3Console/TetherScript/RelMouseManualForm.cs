using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Calibration;

namespace Guncon3Console.TetherScript
{
    internal sealed class RelMouseManualForm : Form
    {
        private readonly HIDController _hid;

        private readonly ToolTip _tip = new ToolTip();

        private GunconDevice _gun;
        private Thread _gunThread;
        private volatile bool _gunRunning;
        private bool _suppressUseGunEvent;

        private int _jRawMax;
        private int _jMapMax;
        private int _jRawSum;
        private int _jMapSum;
        private int _jCount;
        private int _jLastRawX;
        private int _jLastRawY;
        private int _jLastMapX;
        private int _jLastMapY;
        private bool _jInit;
        private int _jWinStart;

        private TextBox _txtKp;
        private TextBox _txtSettle;
        private TextBox _txtDeadzone;
        private TextBox _txtMaxStep;
        private TextBox _txtMinStep;
        private TextBox _txtSlew;
        private TextBox _txtTargetSmooth;
        private TextBox _txtUnlock;
        private TextBox _txtUpdate;
        private Button _btnApply;
        private Button _btnCalibrate;

        private CheckBox _chkUseGun;
        private ComboBox _cmbCalib;
        private RectCalib _calib;
        private CheckBox _chkInvertX;
        private CheckBox _chkInvertY;
        private Button _btnTestArrows;
        private Button _btnTestTargets;
        private Button _btnCenter;
        private StatusStrip _status;
        private ToolStripStatusLabel _lblStatus;

        public RelMouseManualForm(HIDController hid)
        {
            _hid = hid;

            Text = "RelMouse Tuning Test";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 640);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;

            AutoSize = false;
            AutoScroll = false;

            Font = new Font(FontFamily.GenericSansSerif, 10f);

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 14,
                Padding = new Padding(12),
                AutoSize = true,
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            for (int i = 0; i < grid.RowCount; i++)
                grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddRow(grid, 0, "Kp", out _txtKp, "1");
            AddRow(grid, 1, "Settle ms", out _txtSettle, "800");
            AddRow(grid, 2, "Deadzone px", out _txtDeadzone, "1");
            AddRow(grid, 3, "MaxStep px", out _txtMaxStep, "40");
            AddRow(grid, 4, "MinStep px", out _txtMinStep, "1");
            AddRow(grid, 5, "SlewLimit px", out _txtSlew, "25");
            AddRow(grid, 6, "TargetSmooth", out _txtTargetSmooth, "0.25");
            AddRow(grid, 7, "Unlock px", out _txtUnlock, "4");
            AddRow(grid, 8, "Update ms", out _txtUpdate, "0");

            _tip.SetToolTip(_txtKp, "Servo gain used by 'Center + Corners'. Higher = more aggressive/faster, too high can overshoot/jitter. Typical: 1..4");
            _tip.SetToolTip(_txtSettle, "How long the servo test runs per target (ms). Longer gives it more time to converge. Typical: 500..2000");
            _tip.SetToolTip(_txtDeadzone, "Deadzone in pixels for the servo. Errors within this range are treated as 0 to reduce jitter. Typical: 0..3");
            _tip.SetToolTip(_txtMaxStep, "Maximum relative step in pixels per update. Clamped to 1..127 (RelMouse uses 8-bit deltas). Typical: 20..80");
            _tip.SetToolTip(_txtMinStep, "Minimum step in pixels when outside the deadzone. Helps prevent wobble/stickiness near the target. Typical: 0..3");
            _tip.SetToolTip(_txtSlew, "Limits how quickly dx/dy can change from one update to the next (px/update). Prevents rapid sign flipping between two adjacent pixels. Typical: 10..60");
            _tip.SetToolTip(_txtTargetSmooth,
                "Target smoothing (EMA alpha 0..1). Applied to the *aim target* before the servo runs.\n" +
                "Higher = follows the gun more closely (less smoothing / less lag). Lower = steadier but can feel 'floaty'.\n" +
                "Try: 0.20..0.40 (no-lag leaning). Set to 0 to disable.");
            _tip.SetToolTip(_txtUnlock,
                "Deadzone hysteresis (px). When the cursor has settled, it will not move again until the error exceeds this value.\n" +
                "This stops the cursor bouncing between two adjacent pixels.\n" +
                "Expected behavior: higher = more stable when holding still, but tiny movements near center may be ignored.\n" +
                "Try: Deadzone+2 (e.g. Deadzone=2 => Unlock=4)." );
            _tip.SetToolTip(_txtUpdate, "Optional extra sleep per update (ms). 0 = no added delay. Increase if you want slower/steadier movement.");

            _chkInvertX = new CheckBox { Text = "Invert X", AutoSize = true };
            _chkInvertY = new CheckBox { Text = "Invert Y", AutoSize = true };

            _tip.SetToolTip(_chkInvertX, "Flips horizontal direction if Left/Right feels reversed.");
            _tip.SetToolTip(_chkInvertY, "Flips vertical direction if Up/Down feels reversed.");
            var inv = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
            inv.Controls.Add(_chkInvertX);
            inv.Controls.Add(_chkInvertY);
            // row 9: invert
            grid.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 9);
            grid.Controls.Add(inv, 1, 9);

            // row 10: calibration selection
            _cmbCalib = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Left, Width = 260 };
            _cmbCalib.Items.AddRange(new object[] { "Default", "P1", "P2", "None" });
            _cmbCalib.SelectedIndex = 0;
            _cmbCalib.SelectedIndexChanged += (_, __) => LoadSelectedCalibration();
            _tip.SetToolTip(_cmbCalib, "Select which calibration file to use when 'Use gun input' is enabled. Default=calibration_rect.txt, P1/P2 use the dual-mode files, None sends raw gun coords (not recommended)." );

            grid.Controls.Add(new Label { Text = "Calibration", AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 10);
            grid.Controls.Add(_cmbCalib, 1, 10);

            _btnApply = new Button { Text = "Apply to RelMouseFeeder", Dock = DockStyle.Fill, Height = 32 };
            _btnApply.Click += (_, __) => ApplyToFeeder();
            _tip.SetToolTip(_btnApply, "Copies the current knobs into RelMouseFeeder so gameplay uses the same settings.");

            _btnCalibrate = new Button { Text = "Calibrate selected", Dock = DockStyle.Fill, Height = 32 };
            _btnCalibrate.Click += (_, __) => RunCalibrationForSelection();
            _tip.SetToolTip(_btnCalibrate, "Runs the calibration window and saves to the currently selected calibration file (Default/P1/P2)." );

            _chkUseGun = new CheckBox { Text = "Use gun input (C2 to stop)", AutoSize = true };
            _chkUseGun.CheckedChanged += (_, __) =>
            {
                if (_suppressUseGunEvent)
                    return;
                if (_chkUseGun.Checked) StartGunLoop();
                else StopGunLoop();
            };
            _tip.SetToolTip(_chkUseGun, "When enabled, reads the first connected Guncon3 and drives RelMouseFeeder in real-time. Press C2 on the gun to stop.");

            var gunRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, AutoSize = true };
            gunRow.Controls.Add(_btnApply);
            gunRow.Controls.Add(_btnCalibrate);
            gunRow.Controls.Add(_chkUseGun);
            grid.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 11);
            grid.Controls.Add(gunRow, 1, 11);

            _btnTestArrows = new Button { Text = "Test Up/Down/Left/Right", Dock = DockStyle.Fill, Height = 32 };
            _btnTestArrows.Click += (_, __) => TestArrows();

            _btnTestTargets = new Button { Text = "Test Center + Corners (servo)", Dock = DockStyle.Fill, Height = 32 };
            _btnTestTargets.Click += (_, __) => TestTargets();

            _btnCenter = new Button { Text = "Center cursor", Dock = DockStyle.Fill, Height = 32 };
            _btnCenter.Click += (_, __) => CenterCursor();

            _tip.SetToolTip(_btnTestArrows, "Sends one relative move in each direction and displays the observed cursor movement.");
            _tip.SetToolTip(_btnTestTargets, "Runs a closed-loop test toward center + four corners using the Kp/Settle parameters.");
            _tip.SetToolTip(_btnCenter, "Moves the OS cursor to the center of the primary screen.");

            var btns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, AutoSize = true };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            btns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            btns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            btns.Controls.Add(_btnTestArrows, 0, 0);
            btns.Controls.Add(_btnTestTargets, 0, 1);
            btns.Controls.Add(_btnCenter, 0, 2);

            grid.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 12);
            grid.Controls.Add(btns, 1, 12);

            _status = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = false };
            _lblStatus = new ToolStripStatusLabel { Text = "Ready", Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _status.Items.Add(_lblStatus);

            Controls.Add(_status);
            Controls.Add(grid);

            KeyPreview = true;
            KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape) Close();
                if (e.KeyCode == Keys.Enter) TestArrows();
            };

            FormClosed += (_, __) => StopGunLoop();

            LoadSelectedCalibration();
        }

        private void RunCalibrationForSelection()
        {
            try
            {
                StopGunLoop();
                _chkUseGun.Checked = false;

                string sel = (_cmbCalib != null && _cmbCalib.SelectedItem != null) ? _cmbCalib.SelectedItem.ToString() : "Default";
                if (string.Equals(sel, "None", StringComparison.OrdinalIgnoreCase))
                {
                    _lblStatus.Text = "Calibration is disabled (None selected).";
                    return;
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string savePath;
                string label;
                if (string.Equals(sel, "P1", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = System.IO.Path.Combine(baseDir, "calibration_rect_p1.txt");
                    label = "Calibrating: Player 1 / Gun 1";
                }
                else if (string.Equals(sel, "P2", StringComparison.OrdinalIgnoreCase))
                {
                    savePath = System.IO.Path.Combine(baseDir, "calibration_rect_p2.txt");
                    label = "Calibrating: Player 2 / Gun 2";
                }
                else
                {
                    savePath = System.IO.Path.Combine(baseDir, "calibration_rect.txt");
                    label = "Calibrating";
                }

                var guid = new Guid("{A5DCBF10-6530-11D2-901F-00C04FB951ED}");
                const int vid = 2970;
                const int pid = 2048;

                var info = MadWizard.WinUSBNet.USBDevice.GetDevices(guid)
                    .FirstOrDefault(x => x.VID == vid && x.PID == pid);
                if (info == null)
                    throw new Exception("Guncon3 device not found");

                using (var gun = new GunconDevice(info))
                {
                    using (var w = new CalibrationWindow(savePath, label, gun))
                        w.ShowDialog(this);
                }

                LoadSelectedCalibration();
                _lblStatus.Text = "Calibration done: " + System.IO.Path.GetFileName(savePath);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Calibration error: " + ex.Message;
            }
        }

        private static void AddRow(TableLayoutPanel grid, int row, string label, out TextBox txt, string def)
        {
            var l = new Label { Text = label, AutoSize = true, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill };
            txt = new TextBox { Text = def, Dock = DockStyle.Fill };
            grid.Controls.Add(l, 0, row);
            grid.Controls.Add(txt, 1, row);
        }

        private void TestArrows()
        {
            try
            {
                ApplyToFeeder();

                int step = Clamp(ParseInt(_txtMaxStep.Text), 1, 127);
                int delayMs = 250;
                int sx = _chkInvertX.Checked ? -step : step;
                int sy = _chkInvertY.Checked ? -step : step;

                CenterCursor();
                System.Threading.Thread.Sleep(150);

                var moves = new (string name, int dx, int dy)[]
                {
                    ("Right", +sx, 0),
                    ("Left", -sx, 0),
                    ("Down", 0, +sy),
                    ("Up", 0, -sy),
                };

                foreach (var m in moves)
                {
                    var before = GetCursorPosSafe();
                    SendRel(m.dx, m.dy);
                    System.Threading.Thread.Sleep(delayMs);
                    var after = GetCursorPosSafe();
                    _lblStatus.Text = $"{m.name}: sent({m.dx},{m.dy}) moved({after.X - before.X},{after.Y - before.Y})";
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(120);
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Error: " + ex.Message;
            }
        }

        private void TestTargets()
        {
            try
            {
                ApplyToFeeder();

                int kp = RelMouseFeeder.Kp;
                int settleMs = ParseInt(_txtSettle.Text);
                if (kp < 1) kp = 1;

                var b = Screen.PrimaryScreen.Bounds;
                var targets = new[]
                {
                    new POINT { X = b.Left + b.Width / 2, Y = b.Top + b.Height / 2 },
                    new POINT { X = b.Left + 10, Y = b.Top + 10 },
                    new POINT { X = b.Right - 10, Y = b.Top + 10 },
                    new POINT { X = b.Right - 10, Y = b.Bottom - 10 },
                    new POINT { X = b.Left + 10, Y = b.Bottom - 10 },
                };

                CenterCursor();
                System.Threading.Thread.Sleep(150);

                for (int i = 0; i < targets.Length; i++)
                {
                    var t = targets[i];
                    var res = ServoToTarget(t, kp, settleMs);
                    _lblStatus.Text = $"Target {i + 1}/{targets.Length} err=({res.errX},{res.errY}) steps={res.steps}";
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(200);
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Error: " + ex.Message;
            }
        }

        private (int errX, int errY, int steps) ServoToTarget(POINT target, int kp, int settleMs)
        {
            var start = Environment.TickCount;
            int steps = 0;
            while (Environment.TickCount - start < settleMs)
            {
                steps++;
                var cur = GetCursorPosSafe();
                int ex = target.X - cur.X;
                int ey = target.Y - cur.Y;
                int dz = RelMouseFeeder.DeadzonePx;
                if (dz < 0) dz = 0;
                if (Math.Abs(ex) <= dz && Math.Abs(ey) <= dz)
                    break;

                int maxStep = RelMouseFeeder.MaxStepPx;
                if (maxStep < 1) maxStep = 1;
                if (maxStep > 127) maxStep = 127;

                int dx = Clamp(ex * kp, -maxStep, maxStep);
                int dy = Clamp(ey * kp, -maxStep, maxStep);

                if (_chkInvertX.Checked) dx = -dx;
                if (_chkInvertY.Checked) dy = -dy;

                SendRel(dx, dy);
                System.Threading.Thread.Sleep(8);
            }

            var end = GetCursorPosSafe();
            return (target.X - end.X, target.Y - end.Y, steps);
        }

        private static int Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private void ApplyToFeeder()
        {
            int kp = ParseInt(_txtKp.Text);
            int dz = ParseInt(_txtDeadzone.Text);
            int maxStep = ParseInt(_txtMaxStep.Text);
            int minStep = ParseInt(_txtMinStep.Text);
            int slew = ParseInt(_txtSlew.Text);
            double smooth = ParseDouble(_txtTargetSmooth.Text);
            int unlock = ParseInt(_txtUnlock.Text);
            int update = ParseInt(_txtUpdate.Text);

            if (kp < 1) kp = 1;
            if (dz < 0) dz = 0;
            if (maxStep < 1) maxStep = 1;
            if (maxStep > 127) maxStep = 127;
            if (minStep < 0) minStep = 0;
            if (minStep > 127) minStep = 127;
            if (slew < 1) slew = 1;
            if (slew > 127) slew = 127;
            if (smooth < 0) smooth = 0;
            if (smooth > 1) smooth = 1;
            if (unlock < 0) unlock = 0;
            if (update < 0) update = 0;

            RelMouseFeeder.Kp = kp;
            RelMouseFeeder.DeadzonePx = dz;
            RelMouseFeeder.MaxStepPx = maxStep;
            RelMouseFeeder.MinStepPx = minStep;
            RelMouseFeeder.SlewLimitPx = slew;
            RelMouseFeeder.TargetSmoothing = smooth;
            RelMouseFeeder.DeadzoneUnlockPx = unlock;
            RelMouseFeeder.UpdateIntervalMs = update;
            RelMouseFeeder.InvertX = _chkInvertX.Checked;
            RelMouseFeeder.InvertY = _chkInvertY.Checked;

            _lblStatus.Text = $"Applied: Kp={kp} Deadzone={dz} Unlock={unlock} MaxStep={maxStep} MinStep={minStep} Slew={slew} Smooth={smooth.ToString("0.00", CultureInfo.InvariantCulture)} UpdateMs={update} InvX={RelMouseFeeder.InvertX} InvY={RelMouseFeeder.InvertY}";
        }

        private static double ParseDouble(string s)
        {
            s = (s ?? "").Trim();
            return double.Parse(s, CultureInfo.InvariantCulture);
        }

        private void LoadSelectedCalibration()
        {
            try
            {
                string sel = (_cmbCalib != null && _cmbCalib.SelectedItem != null) ? _cmbCalib.SelectedItem.ToString() : "Default";
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string path;
                if (string.Equals(sel, "P1", StringComparison.OrdinalIgnoreCase))
                    path = System.IO.Path.Combine(baseDir, "calibration_rect_p1.txt");
                else if (string.Equals(sel, "P2", StringComparison.OrdinalIgnoreCase))
                    path = System.IO.Path.Combine(baseDir, "calibration_rect_p2.txt");
                else if (string.Equals(sel, "None", StringComparison.OrdinalIgnoreCase))
                {
                    _calib = null;
                    return;
                }
                else
                    path = System.IO.Path.Combine(baseDir, "calibration_rect.txt");

                _calib = RectCalib.Load(path);
                if (_calib == null || !_calib.IsValid())
                    _calib = null;
            }
            catch
            {
                _calib = null;
            }
        }

        private void StartGunLoop()
        {
            try
            {
                if (_gunRunning)
                    return;

                ApplyToFeeder();
                LoadSelectedCalibration();

                var guid = new Guid("{A5DCBF10-6530-11D2-901F-00C04FB951ED}");
                const int vid = 2970;
                const int pid = 2048;

                var info = MadWizard.WinUSBNet.USBDevice.GetDevices(guid)
                    .FirstOrDefault(x => x.VID == vid && x.PID == pid);
                if (info == null)
                    throw new Exception("Guncon3 device not found");

                _gun = new GunconDevice(info);

                ResetJitterWindow();

                _gunRunning = true;
                _gunThread = new Thread(GunLoop) { IsBackground = true };
                _gunThread.Start();
                _lblStatus.Text = "Gun loop started (press C2 on gun to stop).";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Gun loop error: " + ex.Message;
                _suppressUseGunEvent = true;
                try { _chkUseGun.Checked = false; } finally { _suppressUseGunEvent = false; }
                StopGunLoop();
            }
        }

        private void StopGunLoop()
        {
            _gunRunning = false;
            try { _gunThread?.Join(250); } catch { }
            _gunThread = null;
            try { _gun?.Dispose(); } catch { }
            _gun = null;
            ResetJitterWindow();
        }

        private void ResetJitterWindow()
        {
            _jRawMax = 0;
            _jMapMax = 0;
            _jRawSum = 0;
            _jMapSum = 0;
            _jCount = 0;
            _jInit = false;
            _jWinStart = Environment.TickCount;
        }

        private void GunLoop()
        {
            var state = new GunState();
            int lastUi = Environment.TickCount;
            while (_gunRunning)
            {
                try
                {
                    _gun.ReadInto(state.BtnState, out var rawX, out var rawY, out var ind2);
                    state.INDICATOR2 = ind2;

                    int dbgMappedX = 0;
                    int dbgMappedY = 0;

                    // Match game mode: apply RectCalib -> normalized ABS (0..32767)
                    if (_calib != null && _calib.IsValid())
                    {
                        var mapped = _calib.Map(rawX, rawY);
                        dbgMappedX = (int)Math.Round(mapped.X);
                        dbgMappedY = (int)Math.Round(mapped.Y);
                        double nx = (_calib.ScreenW > 1) ? (mapped.X / (double)(_calib.ScreenW - 1)) : 0.0;
                        double ny = (_calib.ScreenH > 1) ? (mapped.Y / (double)(_calib.ScreenH - 1)) : 0.0;
                        if (nx < 0) nx = 0; if (nx > 1) nx = 1;
                        if (ny < 0) ny = 0; if (ny > 1) ny = 1;
                        state.ABS_X = (short)Math.Round(nx * 32767.0);
                        state.ABS_Y = (short)Math.Round(ny * 32767.0);
                    }
                    else
                    {
                        // Fallback (not ideal): raw coords.
                        state.ABS_X = rawX;
                        state.ABS_Y = rawY;
                    }

                    // Jitter metrics (1 second window): max/avg delta per sample in RAW and MAP space.
                    if (!_jInit)
                    {
                        _jInit = true;
                        _jLastRawX = rawX;
                        _jLastRawY = rawY;
                        _jLastMapX = dbgMappedX;
                        _jLastMapY = dbgMappedY;
                    }
                    else
                    {
                        int dRaw = Math.Max(Math.Abs(rawX - _jLastRawX), Math.Abs(rawY - _jLastRawY));
                        int dMap = Math.Max(Math.Abs(dbgMappedX - _jLastMapX), Math.Abs(dbgMappedY - _jLastMapY));

                        _jRawSum += dRaw;
                        _jMapSum += dMap;
                        _jCount++;
                        if (dRaw > _jRawMax) _jRawMax = dRaw;
                        if (dMap > _jMapMax) _jMapMax = dMap;

                        _jLastRawX = rawX;
                        _jLastRawY = rawY;
                        _jLastMapX = dbgMappedX;
                        _jLastMapY = dbgMappedY;

                        if (Environment.TickCount - _jWinStart >= 1000)
                        {
                            _jWinStart = Environment.TickCount;
                            _jRawMax = 0;
                            _jMapMax = 0;
                            _jRawSum = 0;
                            _jMapSum = 0;
                            _jCount = 0;
                        }
                    }

                    if (state.BtnState.TryGetValue(GunButton.C2, out bool stop) && stop)
                    {
                        BeginInvoke((Action)(() =>
                        {
                            _suppressUseGunEvent = true;
                            try { _chkUseGun.Checked = false; } finally { _suppressUseGunEvent = false; }
                            _lblStatus.Text = "Stopped by C2.";
                        }));
                        return;
                    }

                    // When the gun reports OUT, don't emit any RelMouse output.
                    // (Avoids button toggles or jitter when the sensor is not tracking.)
                    if (state.IsInsideScreen)
                        FeedButtonsAndServo(state);

                    // lightweight progress indicator
                    if (Environment.TickCount - lastUi > 500)
                    {
                        lastUi = Environment.TickCount;
                        var abs = $"ABS=({state.ABS_X},{state.ABS_Y})";
                        var raw = $"RAW=({rawX},{rawY})";
                        var mappedTxt = (_calib != null) ? $"MAP=({dbgMappedX},{dbgMappedY})" : "MAP=(n/a)";
                        int rawAvg = (_jCount > 0) ? (_jRawSum / _jCount) : 0;
                        int mapAvg = (_jCount > 0) ? (_jMapSum / _jCount) : 0;
                        var jit = $"JIT raw(avg={rawAvg} max={_jRawMax}) map(avg={mapAvg} max={_jMapMax})";
                        var cur = GetCursorPosSafe();
                        var b = Screen.PrimaryScreen.Bounds;
                        int tx = b.Left + (int)Math.Round((state.ABS_X / 32767.0) * (b.Width - 1));
                        int ty = b.Top + (int)Math.Round((state.ABS_Y / 32767.0) * (b.Height - 1));
                        var tgt = $"TGT=({tx},{ty})";
                        var curTxt = $"CUR=({cur.X},{cur.Y})";
                        var inside = state.IsInsideScreen ? "IN" : "OUT";
                        BeginInvoke((Action)(() => { _lblStatus.Text = $"Gun: {inside} {raw} {mappedTxt} {abs} {jit} {tgt} {curTxt}"; }));
                    }
                }
                catch { }

                Thread.Sleep(1);
            }
        }

        private void SendRel(int dx, int dy)
        {
            dx = Clamp(dx, -127, 127);
            dy = Clamp(dy, -127, 127);

            var buf = new byte[5];
            buf[0] = 1;
            buf[1] = 2;
            buf[2] = 0;
            buf[3] = unchecked((byte)(sbyte)dx);
            buf[4] = unchecked((byte)(sbyte)dy);
            if (!_hid.SendData(buf, (uint)buf.Length))
                UpdateStatusSendFail();
        }

        private void FeedButtonsAndServo(GunState state)
        {
            byte btns = 0;
            foreach (var map in RelMouseFeeder.Mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) btns = (byte)(btns | 1);
                if (map.Value == MouseButton.Right) btns = (byte)(btns | (1 << 1));
                if (map.Value == MouseButton.Middle) btns = (byte)(btns | (1 << 2));
            }

            var b = Screen.PrimaryScreen.Bounds;
            double nx = state.ABS_X / 32767.0;
            double ny = state.ABS_Y / 32767.0;
            if (nx < 0) nx = 0; else if (nx > 1) nx = 1;
            if (ny < 0) ny = 0; else if (ny > 1) ny = 1;

            int tx = b.Left + (int)Math.Round(nx * (b.Width - 1));
            int ty = b.Top + (int)Math.Round(ny * (b.Height - 1));

            var cur = GetCursorPosSafe();
            int ex = tx - cur.X;
            int ey = ty - cur.Y;

            int dz = RelMouseFeeder.DeadzonePx;
            if (Math.Abs(ex) <= dz) ex = 0;
            if (Math.Abs(ey) <= dz) ey = 0;

            int kp = RelMouseFeeder.Kp;
            if (kp < 1) kp = 1;

            int maxStep = RelMouseFeeder.MaxStepPx;
            if (maxStep < 1) maxStep = 1;
            if (maxStep > 127) maxStep = 127;

            int dx = Clamp(ex * kp, -maxStep, maxStep);
            int dy = Clamp(ey * kp, -maxStep, maxStep);
            if (RelMouseFeeder.InvertX) dx = -dx;
            if (RelMouseFeeder.InvertY) dy = -dy;

            var buf = new byte[5];
            buf[0] = 1;
            buf[1] = 2;
            buf[2] = btns;
            buf[3] = unchecked((byte)(sbyte)dx);
            buf[4] = unchecked((byte)(sbyte)dy);
            if (!_hid.SendData(buf, (uint)buf.Length))
                UpdateStatusSendFail();
        }

        private void UpdateStatusSendFail()
        {
            try
            {
                int err = Marshal.GetLastWin32Error();
                if (IsHandleCreated)
                    BeginInvoke((Action)(() => { _lblStatus.Text = $"RelMouse SendData failed (Win32={err})"; }));
            }
            catch { }
        }

        private static int ParseInt(string s)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.Parse(s.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return int.Parse(s, CultureInfo.InvariantCulture);
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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetCursorPos(int X, int Y);

        private static void CenterCursor()
        {
            try
            {
                int cx = Screen.PrimaryScreen.Bounds.Left + (Screen.PrimaryScreen.Bounds.Width / 2);
                int cy = Screen.PrimaryScreen.Bounds.Top + (Screen.PrimaryScreen.Bounds.Height / 2);
                SetCursorPos(cx, cy);
            }
            catch { }
        }
    }
}
