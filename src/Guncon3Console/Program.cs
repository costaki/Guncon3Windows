using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GunconUSB;                         // gun reader (GunconUSB project)
using Guncon3Console.TetherScript;      // TetherScript feeders (mouse/keyboard)
using MadWizard.WinUSBNet;
using System.Linq;

namespace Guncon3Console
{
    internal static class Program
    {
        private static RectCalib _rect;
        private static RectCalib _rectP1;
        private static RectCalib _rectP2;
        private static volatile bool _running = true;

        private static GunconDevice _gun1ForCal;
        private static GunconDevice _gun2ForCal;

        private const string CalibDefault = "calibration_rect.txt";
        private const string CalibP1 = "calibration_rect_p1.txt";
        private const string CalibP2 = "calibration_rect_p2.txt";

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "GUNCON3";
            PrintHeader();

            // args:
            //  - relmouse  => single-gun mode, feed TetherScript Virtual Mouse Rel
            //  - dual      => single process reads 2 guns: P1 -> MouseAbs, P2 -> MouseRel
            //  - wininputabs => single-gun mode, feed WindowsInput absolute mouse instead of TetherScript AbsMouse
            bool dual = args.Any(a => a.Equals("dual", StringComparison.OrdinalIgnoreCase));
            bool testMode = args.Any(a => a.Equals("test", StringComparison.OrdinalIgnoreCase));
            bool useRelMouse = (!dual && args.Any(a => a.Equals("relmouse", StringComparison.OrdinalIgnoreCase)));
            bool useWindowsInputAbs = (!dual && !useRelMouse && args.Any(a => a.Equals("wininputabs", StringComparison.OrdinalIgnoreCase)));

            // === "keys": show keycode table and exit ===
            if (args.Length > 0 && args[0].Equals("keys", StringComparison.OrdinalIgnoreCase))
            {
                PrintKeyCodes();
                return;
            }

            // === "calib": run calibration window and exit ===
            // calib [p1|p2] (dual) or calib (single)
            if (args.Length > 0 && args[0].Equals("calib", StringComparison.OrdinalIgnoreCase))
            {
                string which = (args.Length > 1) ? args[1].ToLowerInvariant() : "";
                string path = CalibDefault;
                if (which == "p1") path = CalibP1;
                else if (which == "p2") path = CalibP2;

                string label = (which == "p1") ? "Calibrating: Player 1 / Gun 1" : (which == "p2") ? "Calibrating: Player 2 / Gun 2" : "Calibrating";
                LaunchCalibrationWindowModal(path, label);
                return;
            }

            // === "dump-hid": dump present HID devices and exit ===
            if (args.Length > 0 && args[0].Equals("dump-hid", StringComparison.OrdinalIgnoreCase))
            {
                new HIDController().DumpTetherscriptCandidates();
                return;
            }

            // === "probe-relmouse": brute-force probe the RelMouse report format ===
            if (args.Length > 0 && args[0].Equals("probe-relmouse", StringComparison.OrdinalIgnoreCase))
            {
                Environment.ExitCode = RelMouseProbe.Run(args.Skip(1).ToArray());
                return;
            }

            // === "relmouse-manual": open a simple manual sender form for RelMouse reports ===
            if (args.Length > 0 && args[0].Equals("relmouse-manual", StringComparison.OrdinalIgnoreCase))
            {
                var hid = new HIDController();
                hid.VendorID = (ushort)DriversConst.TTC_VENDORID;
                hid.ProductID = (ushort)DriversConst.TTC_PRODUCTID_MOUSEREL;
                hid.Connect();

                if (!hid.Connected)
                {
                    FailAndExit("Could not connect to the TetherScript RelMouse.");
                    return;
                }

                try
                {
                    using (var w = new RelMouseManualForm(hid))
                        Application.Run(w);
                }
                finally
                {
                    try { hid.Disconnect(); } catch { }
                }
                return;
            }

            // (testMode parsed above)

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            GunconDevice gun1 = null;
            GunconDevice gun2 = null;

            try
            {
                Console.WriteLine("Guncon3 connecting...");

                if (dual)
                {
                    var guid = new Guid("{A5DCBF10-6530-11D2-901F-00C04FB951ED}");
                    const int vid = 2970;
                    const int pid = 2048;

                    var infos = USBDevice.GetDevices(guid)
                                       .Where(x => x.VID == vid && x.PID == pid)
                                       .Take(2)
                                       .ToList();

                    if (infos.Count < 2)
                        throw new Exception("Dual mode requires 2 Guncon3 devices.");

                    gun1 = new GunconDevice(infos[0]);
                    gun2 = new GunconDevice(infos[1]);

                    _gun1ForCal = gun1;
                    _gun2ForCal = gun2;
                    Console.WriteLine("Guncon3 connected (dual). #1=" + infos[0].DevicePath);
                    Console.WriteLine("Guncon3 connected (dual). #2=" + infos[1].DevicePath);
                }
                else
                {
                    var guid = new Guid("{A5DCBF10-6530-11D2-901F-00C04FB951ED}");
                    const int vid = 2970;
                    const int pid = 2048;

                    var info = USBDevice.GetDevices(guid)
                                      .FirstOrDefault(x => x.VID == vid && x.PID == pid);
                    if (info == null)
                        throw new Exception("Guncon3 device not found");

                    gun1 = new GunconDevice(info);
                    _gun1ForCal = gun1;
                    Console.WriteLine("Guncon3 connected (single). #1=" + info.DevicePath);
                }
            }
            catch (Exception ex)
            {
                FailAndExit("Could not connect to the Guncon3.", ex);
                return;
            }

            if (testMode)
            {
                Console.WriteLine("[Test] Opening test window...");
                using (var w = new TestWindow(gun1, dual ? gun2 : null))
                    Application.Run(w);
                return;
            }

            if (dual)
            {
                _rectP1 = RectCalib.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP1));
                _rectP2 = RectCalib.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP2));

                if (_rectP1 == null || !_rectP1.IsValid() || _rectP2 == null || !_rectP2.IsValid())
                {
                    Console.WriteLine("Dual mode calibration not found/invalid. Starting calibration...");
                    RecalibrateDual();

                    if (_rectP1 == null || !_rectP1.IsValid() || _rectP2 == null || !_rectP2.IsValid())
                    {
                        FailAndExit("Cannot run dual mode without both calibrations.");
                        return;
                    }
                }

                Console.WriteLine("Calibration loaded: " + CalibP1 + " and " + CalibP2);
            }
            else
            {
                if (!LoadRectCalib())
                {
                    Console.WriteLine(CalibDefault + " not found. Opening calibration (modal)...");
                    LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibDefault), "Calibrating");
                    if (!LoadRectCalib())
                    {
                        FailAndExit("Cannot run without calibration (calibration could not be obtained).");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine("Calibration loaded from " + CalibDefault);
                }
            }

            TryConnectFeeders(useRelMouse, dual, useWindowsInputAbs);
            LoadMapping("mapping.txt");

            // If using RelMouse, mirror mouse mappings into RelMouseFeeder.
            if (useRelMouse)
            {
                RelMouseFeeder.Mapping.Clear();
                foreach (var kv in AbsMouseFeeder.Mapping)
                    RelMouseFeeder.Mapping[kv.Key] = kv.Value;
            }

            // If using WindowsInput absolute mouse (single) OR dual mode (P2 uses WindowsInput abs),
            // mirror mappings from AbsMouseFeeder.
            // (Mapping file continues to use MOUSE.Left/Right/Middle, mapped into AbsMouseFeeder.Mapping.)
            if (useWindowsInputAbs || dual)
            {
                WindowsInputAbsMouseFeeder.Mapping.Clear();
                foreach (var kv in AbsMouseFeeder.Mapping)
                {
                    if (kv.Value == MouseButton.Left)
                        WindowsInputAbsMouseFeeder.Mapping[kv.Key] = WindowsInput.MouseButton.LeftButton;
                    else if (kv.Value == MouseButton.Right)
                        WindowsInputAbsMouseFeeder.Mapping[kv.Key] = WindowsInput.MouseButton.RightButton;
                    else if (kv.Value == MouseButton.Middle)
                        WindowsInputAbsMouseFeeder.Mapping[kv.Key] = WindowsInput.MouseButton.MiddleButton;
                }
            }

            Console.WriteLine("Mapping OK.");
            Console.WriteLine("Ready to use!   (F12 = recalibrate,  T = test screen,  R = reload mapping.txt,  ESC = exit)");

            var p1 = new GunPlayerState();
            var p2 = dual ? new GunPlayerState() : null;

            // Test window is launched modally via Application.Run when requested.

            while (_running)
            {
                if (dual)
                {
                    // Read both guns into per-player state
                    gun1.ReadInto(p1.BtnState, out var g1x, out var g1y, out var g1Ind2);
                    gun2.ReadInto(p2.BtnState, out var g2x, out var g2y, out var g2Ind2);

                    p1.INDICATOR2 = g1Ind2;
                    p2.INDICATOR2 = g2Ind2;

                    // Apply the same rectangular calibration to both
                    ApplyRectCalib(_rectP1, p1, g1x, g1y);
                    ApplyRectCalib(_rectP2, p2, g2x, g2y);
                }
                else
                {
                    gun1.ReadInto(p1.BtnState, out var g1x, out var g1y, out var g1Ind2);
                    p1.INDICATOR2 = g1Ind2;

                    if (_rect != null && _rect.IsValid())
                        ApplyRectCalib(_rect, p1, g1x, g1y);
                    else
                    {
                        p1.ABS_X = g1x;
                        p1.ABS_Y = g1y;
                    }
                }

                try
                {
                    if (dual)
                    {
                        AbsMouseFeeder.Feed(p1);
                        WindowsInputAbsMouseFeeder.Feed(p2);
                    }
                    else
                    {
                        if (useRelMouse)
                            RelMouseFeeder.Feed(p1);
                        else if (useWindowsInputAbs)
                            WindowsInputAbsMouseFeeder.Feed(p1);
                        else
                            AbsMouseFeeder.Feed(p1);
                    }

                    KeyboardFeeder.Feed(p1);
                }
                catch { }

                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Escape)
                        _running = false;
                    else if (k.Key == ConsoleKey.T)
                    {
                        try
                        {
                            using (var w = new TestWindow(gun1, dual ? gun2 : null))
                                Application.Run(w);
                        }
                        catch { }
                    }
                    else if (k.Key == ConsoleKey.F12)
                    {
                        if (dual)
                            RecalibrateDual();
                        else
                            Recalibrate();
                    }
                    else if (k.Key == ConsoleKey.R)
                    {
                        Console.WriteLine("[Mapping] Reloading mapping.txt…");
                        LoadMapping("mapping.txt");
                        if (useRelMouse)
                        {
                            RelMouseFeeder.Mapping.Clear();
                            foreach (var kv in AbsMouseFeeder.Mapping)
                                RelMouseFeeder.Mapping[kv.Key] = kv.Value;
                        }
                        if (useWindowsInputAbs || dual)
                        {
                            WindowsInputAbsMouseFeeder.Mapping.Clear();
                            foreach (var kv in AbsMouseFeeder.Mapping)
                            {
                                if (kv.Value == MouseButton.Left)
                                    WindowsInputAbsMouseFeeder.Mapping[kv.Key] = WindowsInput.MouseButton.LeftButton;
                                else if (kv.Value == MouseButton.Right)
                                    WindowsInputAbsMouseFeeder.Mapping[kv.Key] = WindowsInput.MouseButton.RightButton;
                                else if (kv.Value == MouseButton.Middle)
                                    WindowsInputAbsMouseFeeder.Mapping[kv.Key] = WindowsInput.MouseButton.MiddleButton;
                            }
                        }
                        Console.WriteLine("[Mapping] OK.");
                    }
                }

                Thread.Sleep(1);
            }
            try { if (useRelMouse) RelMouseFeeder.Disconnect(); } catch { }
            try { if (dual || useWindowsInputAbs) WindowsInputAbsMouseFeeder.Disconnect(); } catch { }
            try { AbsMouseFeeder.Disconnect(); } catch { }
            try { if (dual) GamepadFeeder.Disconnect(); } catch { }
            try { KeyboardFeeder.Disconnect(); } catch { }
            try { gun1?.Dispose(); } catch { }
            try { gun2?.Dispose(); } catch { }
        }

        private static void ApplyRectCalib(RectCalib rect, GunPlayerState state, short rawX, short rawY)
        {
            if (rect == null || !rect.IsValid())
            {
                state.ABS_X = rawX;
                state.ABS_Y = rawY;
                return;
            }

            var (px, py) = rect.Map(rawX, rawY);

            double nx = (rect.ScreenW > 1) ? (px / (rect.ScreenW - 1)) : 0.0;
            double ny = (rect.ScreenH > 1) ? (py / (rect.ScreenH - 1)) : 0.0;
            if (nx < 0) nx = 0; if (nx > 1) nx = 1;
            if (ny < 0) ny = 0; if (ny > 1) ny = 1;

            state.ABS_X = (short)Math.Round(nx * 32767.0);
            state.ABS_Y = (short)Math.Round(ny * 32767.0);
        }

        private static bool LoadRectCalib()
        {
            _rect = RectCalib.Load();
            return _rect != null && _rect.IsValid();
        }

        private static void LaunchCalibrationWindowModal()
        {
            try
            {
                using (var w = new CalibrationWindow(null, null, _gun1ForCal))
                    Application.Run(w);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Calibration] Error: " + ex.Message);
            }
        }

        private static void LaunchCalibrationWindowModal(string savePath)
        {
            try
            {
                using (var w = new CalibrationWindow(savePath, null, _gun1ForCal))
                    Application.Run(w);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Calibration] Error: " + ex.Message);
            }
        }

        private static void LaunchCalibrationWindowModal(string savePath, string label)
        {
            try
            {
                using (var w = new CalibrationWindow(savePath, label, _gun1ForCal))
                    Application.Run(w);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Calibration] Error: " + ex.Message);
            }
        }

        private static void LaunchCalibrationWindowModal(string savePath, string label, GunconDevice device)
        {
            try
            {
                using (var w = new CalibrationWindow(savePath, label, device))
                    Application.Run(w);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Calibration] Error: " + ex.Message);
            }
        }

        private static void Recalibrate()
        {
            Console.WriteLine("[Calibration] Opening window (F12)...");
            LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibDefault), "Calibrating");
            if (LoadRectCalib())
                Console.WriteLine("Calibration loaded from " + CalibDefault);
            else
                Console.WriteLine("WARNING: " + CalibDefault + " was not created");
        }

        private static void RecalibrateDual()
        {
            Console.WriteLine("[Calibration] Dual mode: calibrating P1...");
            LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP1), "Calibrating: Player 1 / Gun 1", _gun1ForCal);

            Console.WriteLine("[Calibration] Dual mode: calibrating P2...");
            LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP2), "Calibrating: Player 2 / Gun 2", _gun2ForCal);

            _rectP1 = RectCalib.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP1));
            _rectP2 = RectCalib.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP2));

            Console.WriteLine("[Calibration] Reloaded: " + CalibP1 + " and " + CalibP2);
        }

        private static void TryConnectFeeders(bool useRelMouse, bool dual, bool useWindowsInputAbs)
        {
            try
            {
                if (dual)
                {
                    Console.WriteLine("MouseAbs Connecting...");
                    AbsMouseFeeder.Connect();
                    Console.WriteLine("MouseAbs Connected. (TetherScript)");

                    Console.WriteLine("MouseAbs2 Connecting... (WindowsInput Abs)");
                    WindowsInputAbsMouseFeeder.Connect();
                    Console.WriteLine("MouseAbs2 Connected. (WindowsInput Abs)");

                    Console.WriteLine("Gamepad Connecting...");
                    GamepadFeeder.Connect();
                    Console.WriteLine("Gamepad Connected. (TetherScript)");
                }
                else
                {
                    if (useRelMouse)
                        Console.WriteLine("MouseRel Connecting...");
                    else if (useWindowsInputAbs)
                        Console.WriteLine("Mouse Connecting... (WindowsInput Abs)");
                    else
                        Console.WriteLine("Mouse Connecting...");

                    if (useRelMouse)
                        RelMouseFeeder.Connect();
                    else if (useWindowsInputAbs)
                        WindowsInputAbsMouseFeeder.Connect();
                    else
                        AbsMouseFeeder.Connect();

                    if (useRelMouse)
                        Console.WriteLine("MouseRel Connected. (TetherScript)");
                    else if (useWindowsInputAbs)
                        Console.WriteLine("Mouse Connected. (WindowsInput Abs)");
                    else
                        Console.WriteLine("Mouse Connected. (TetherScript)");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MouseFeeder] Connect fail:\n" + ex);
            }

            try
            {
                Console.WriteLine("Keyboard Connecting...");
                KeyboardFeeder.Connect();
                Console.WriteLine("Keyboard Connected (TetherScript).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[KeyboardFeeder] Connect fail:\n" + ex);
            }
        }

        private static void LoadMapping(string path)
        {
            AbsMouseFeeder.Mapping.Clear();
            KeyboardFeeder.Mapping.Clear();
            RelMouseFeeder.Mapping.Clear();
            GamepadFeeder.Mapping.Clear();

            if (!File.Exists(path))
            {
                Console.WriteLine("[Mapping] mapping.txt not found (an empty mapping will be used).");
                return;
            }

            byte lineNo = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                lineNo++;
                var line = raw.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var left = line.Substring(0, eq).Trim();
                var right = line.Substring(eq + 1).Trim();

                var dot = left.IndexOf('.');
                if (dot <= 0) continue;

                var device = left.Substring(0, dot).ToUpperInvariant();
                var cmd = left.Substring(dot + 1);

                if (!Enum.TryParse<GunButton>(right, ignoreCase: false, out var gunBtn))
                {
                    Console.WriteLine($"[Mapping] Line {lineNo}: unknown gun command: {right}");
                    continue;
                }

                if (device == "MOUSE")
                {
                    if (cmd.Equals("Left", StringComparison.OrdinalIgnoreCase))
                        AbsMouseFeeder.Mapping[gunBtn] = MouseButton.Left;
                    else if (cmd.Equals("Right", StringComparison.OrdinalIgnoreCase))
                        AbsMouseFeeder.Mapping[gunBtn] = MouseButton.Right;
                    else if (cmd.Equals("Middle", StringComparison.OrdinalIgnoreCase))
                        AbsMouseFeeder.Mapping[gunBtn] = MouseButton.Middle;
                }
                else if (device == "MOUSE2")
                {
                    if (cmd.Equals("Left", StringComparison.OrdinalIgnoreCase))
                        RelMouseFeeder.Mapping[gunBtn] = MouseButton.Left;
                    else if (cmd.Equals("Right", StringComparison.OrdinalIgnoreCase))
                        RelMouseFeeder.Mapping[gunBtn] = MouseButton.Right;
                    else if (cmd.Equals("Middle", StringComparison.OrdinalIgnoreCase))
                        RelMouseFeeder.Mapping[gunBtn] = MouseButton.Middle;
                }
                else if (device == "KEYBOARD")
                {
                    if (byte.TryParse(cmd, out var keyCode))
                        KeyboardFeeder.Mapping[gunBtn] = keyCode;
                }
                else if (device == "KEYBOARD2")
                {
                    // Placeholder: no separate keyboard device exposed. Intentionally ignored.
                    // Keep parsing so mapping files can be shared with future multi-keyboard support.
                }
                else if (device == "GAMEPAD2")
                {
                    if (int.TryParse(cmd, out var btnBit))
                        GamepadFeeder.Mapping[gunBtn] = btnBit;
                }
            }

            Console.WriteLine($"[Mapping] Mouse: {AbsMouseFeeder.Mapping.Count} entries, Mouse2: {RelMouseFeeder.Mapping.Count} entries, Gamepad2: {GamepadFeeder.Mapping.Count} entries, Keyboard: {KeyboardFeeder.Mapping.Count} entries.");
        }

        private static void FailAndExit(string msg, Exception ex = null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(msg);
            if (ex != null) Console.WriteLine(ex);
            Console.ResetColor();
            Console.WriteLine("Press any key to exit.");
            try { Console.ReadKey(true); } catch { }
        }

        private static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("GUNCON3 V0.41 - BY DANITURI (BASED ON SONIK PROJECT)");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("BUILD TAG: CALIB-FIRST v2 + feeders-guard (STRICT TS)");
            Console.WriteLine("EXE:  " + System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            Console.WriteLine("BASE: " + AppDomain.CurrentDomain.BaseDirectory);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("4:3 inside 16:9 mode enabled");
        }

            // === Full keycode table (4..111) ===
        private static void PrintKeyCodes()
        {
            // index = keycode
            string[] name = new string[112];

            // 4..29 letters
            name[4] = "a"; name[5] = "b"; name[6] = "c"; name[7] = "d"; name[8] = "e"; name[9] = "f";
            name[10] = "g"; name[11] = "h"; name[12] = "i"; name[13] = "j"; name[14] = "k"; name[15] = "l";
            name[16] = "m"; name[17] = "n"; name[18] = "o"; name[19] = "p"; name[20] = "q"; name[21] = "r";
            name[22] = "s"; name[23] = "t"; name[24] = "u"; name[25] = "v"; name[26] = "w"; name[27] = "x";
            name[28] = "y"; name[29] = "z";

            // 30..39 top-row digits
            name[30] = "1"; name[31] = "2"; name[32] = "3"; name[33] = "4"; name[34] = "5";
            name[35] = "6"; name[36] = "7"; name[37] = "8"; name[38] = "9"; name[39] = "0";

            // specials
            name[40] = "ENTER";
            name[41] = "ESCAPE";
            name[42] = "BACKSPACE";
            name[43] = "TAB";
            name[44] = "SPACEBAR";
            name[45] = "-";
            name[46] = "=";
            name[47] = "[";
            name[48] = "]";
            name[49] = "\\";
            name[50] = "";        // (empty, same as original)
            name[51] = ";";
            name[52] = "dummy5";  // keep original text
            name[53] = "`";
            name[54] = ",";
            name[55] = ".";
            name[56] = "/";

            // lock keys and F1..F12
            name[57] = "CAPSLOCK";
            name[58] = "F1"; name[59] = "F2"; name[60] = "F3"; name[61] = "F4"; name[62] = "F5";
            name[63] = "F6"; name[64] = "F7"; name[65] = "F8"; name[66] = "F9"; name[67] = "F10";
            name[68] = "F11"; name[69] = "F12";

            // navigation
            name[70] = "PRINTSCREEN";
            name[71] = "SCROLLLOCK";
            name[72] = "PAUSE";
            name[73] = "INSERT";
            name[74] = "HOME";
            name[75] = "PAGEUP";
            name[76] = "DELETE";
            name[77] = "END";
            name[78] = "PAGEDOWN";
            name[79] = "RIGHTARROW";
            name[80] = "LEFTARROW";
            name[81] = "DOWNARROW";
            name[82] = "UPARROW";

            // keypad
            name[83] = "NUMLOCK";
            name[84] = "K/";    // keypad /
            name[85] = "K*";    // keypad *
            name[86] = "K-";    // keypad -
            name[87] = "K+";    // keypad +
            name[88] = "KENTER";
            name[89] = "K1";
            name[90] = "K2";
            name[91] = "K3";
            name[92] = "K4";
            name[93] = "K5";
            name[94] = "K6";
            name[95] = "K7";
            name[96] = "K8";
            name[97] = "K9";
            name[98] = "K0";
            name[99] = "K.";

            // F13..F24
            name[100] = "F13"; name[101] = "F14"; name[102] = "F15"; name[103] = "F16";
            name[104] = "F17"; name[105] = "F18"; name[106] = "F19"; name[107] = "F20";
            name[108] = "F21"; name[109] = "F22"; name[110] = "F23"; name[111] = "F24";

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("KEYCODE\tKEY");
            Console.ResetColor();

            for (int i = 4; i <= 111; i++)
            {
                var n = name[i];
                if (!string.IsNullOrEmpty(n))
                    Console.WriteLine($"{i}\t{n}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit…");
            try { Console.ReadKey(true); } catch { }
        }
    }
}
