using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using System.Linq;
using MadWizard.WinUSBNet;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.TetherScript;
using Guncon3Console.Feeders;
using Guncon3Console.WindowsInput;
using Guncon3Console.Calibration;

namespace Guncon3Console
{
    internal static class Program
    {
        private static RectCalib _rect;
        private static RectCalib _rectP1;
        private static RectCalib _rectP2;
        private static volatile bool _running = true;

        private static readonly TetherScript.AbsMouseFeeder _absMouse = new TetherScript.AbsMouseFeeder();
        private static readonly RelMouseFeeder _relMouse = new RelMouseFeeder();
        private static readonly WindowsInput.AbsMouseFeeder _winAbsMouse = new WindowsInput.AbsMouseFeeder();
        private static readonly KeyboardFeeder _keyboard = new KeyboardFeeder();
        private static readonly GamepadFeeder _gamepad = new GamepadFeeder();

        private static GunconDevice _gun1ForCal;
        private static GunconDevice _gun2ForCal;

        private const string CalibDefault = RectCalib.DefaultFileName;
        private const string CalibP1 = RectCalib.Player1FileName;
        private const string CalibP2 = RectCalib.Player2FileName;

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "GUNCON3";
            PrintHeader();

            // args:
            //  - dual      => single process reads 2 guns: P1 -> MouseAbs, P2 -> WindowsInput AbsMouse
            //  - test      => open a test window showing gun input state (while still feeding TetherScript)
            //  - relmouse  => single-gun mode, feed TetherScript Virtual Mouse Rel
            //  - wininputabs => single-gun mode, feed WindowsInput absolute mouse instead of TetherScript AbsMouse
            bool dual = args.Any(a => a.Equals("dual", StringComparison.OrdinalIgnoreCase));
            bool testMode = args.Any(a => a.Equals("test", StringComparison.OrdinalIgnoreCase));
            bool useRelMouse = (args.Any(a => a.Equals("relmouse", StringComparison.OrdinalIgnoreCase)));
            bool useWindowsInputAbs = (!useRelMouse && args.Any(a => a.Equals("wininputabs", StringComparison.OrdinalIgnoreCase)));

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
                LaunchCalibrationWindowModal(path, label, _gun1ForCal);
                return;
            }

            // === "dump-hid": dump present HID devices and exit ===
            if (args.Length > 0 && args[0].Equals("dump-hid", StringComparison.OrdinalIgnoreCase))
            {
                new HIDController().DumpTetherscriptCandidates();
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

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            GunconDevice gun1 = null;
            GunconDevice gun2 = null;

            try
            {
                Console.WriteLine("Guncon3 connecting...");

                if (dual)
                {
                    var infos = USBDevice.GetDevices(Constants.GunDeviceInterfaceGuid)
                                       .Where(x => x.VID == Constants.VendorId && x.PID == Constants.ProductId)
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
                    var info = USBDevice.GetDevices(Constants.GunDeviceInterfaceGuid)
                                      .FirstOrDefault(x => x.VID == Constants.VendorId && x.PID == Constants.ProductId);
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
                _rect = RectCalib.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibDefault));

                if (_rect == null || !_rect.IsValid())
                {
                    Console.WriteLine("Single mode calibration not found/invalid. Starting calibration...");
                    Recalibrate();

                    if (_rect == null || !_rect.IsValid())
                    {
                        FailAndExit("Cannot run without calibration.");
                        return;
                    }
                }
                Console.WriteLine("Calibration loaded from " + CalibDefault);
            }

            TryConnectFeeders(useRelMouse, dual, useWindowsInputAbs);
            LoadMapping("mapping.txt", dual, useRelMouse, useWindowsInputAbs);
            Console.WriteLine("Mapping OK.");
            Console.WriteLine("Ready to use!   (F12 = recalibrate,  T = test screen,  R = reload mapping.txt,  ESC = exit)");

            var p1 = new GunState();
            var p2 = dual ? new GunState() : null;

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

                    ApplyRectCalib(_rect, p1, g1x, g1y);
                }

                try
                {
                    if (dual)
                    {
                        _absMouse.Feed(p1);
                        if (useRelMouse)
                            _relMouse.Feed(p2);
                        else
                            _winAbsMouse.Feed(p2);

                        _keyboard.Feed(p1);
                        _keyboard.Feed(p2);
                    }
                    else
                    {
                        if (useRelMouse)
                            _relMouse.Feed(p1);
                        else if (useWindowsInputAbs)
                            _winAbsMouse.Feed(p1);
                        else
                            _absMouse.Feed(p1);

                        _keyboard.Feed(p1);
                    }
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
                        LoadMapping("mapping.txt", dual, useRelMouse, useWindowsInputAbs);
                        Console.WriteLine("[Mapping] OK.");
                    }
                }

                Thread.Sleep(1);
            }
            try { if (useRelMouse) _relMouse.Disconnect(); } catch { }
            try { if (dual || useWindowsInputAbs) _winAbsMouse.Disconnect(); } catch { }
            try { _absMouse.Disconnect(); } catch { }
            try { if (dual) _gamepad.Disconnect(); } catch { }
            try { _keyboard.Disconnect(); } catch { }
            try { gun1?.Dispose(); } catch { }
            try { gun2?.Dispose(); } catch { }
        }

        private static void ApplyRectCalib(RectCalib rect, GunState state, short rawX, short rawY)
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
            LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibDefault), "Calibrating", _gun1ForCal);

            _rect = RectCalib.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibDefault));

            Console.WriteLine("[Calibration] Reloaded: " + CalibDefault);
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
                    Console.WriteLine("TetherScript Absolute Mouse for Player 1 connecting...");
                    _absMouse.Connect();
                    Console.WriteLine("TetherScript Absolute Mouse for Player 1 connected.");

                    if (useRelMouse)
                    {
                        Console.WriteLine("TetherScript Relative Mouse for Player 2 connecting...");
                        _relMouse.Connect();
                        Console.WriteLine("TetherScript Relative Mouse for Player 2 connected.");
                    }
                    else
                    {
                        Console.WriteLine("WindowsInput Absolute Mouse for Player 2 connecting...");
                        _winAbsMouse.Connect();
                        Console.WriteLine("WindowsInput Absolute Mouse for Player 2 connected.");
                    }
                }
                else
                {
                    if (useRelMouse)
                    {
                        Console.WriteLine("TetherScript Relative Mouse connecting...");
                        _relMouse.Connect();
                        Console.WriteLine("TetherScript Relative Mouse connected.");
                    }
                    else if (useWindowsInputAbs)
                    {
                        Console.WriteLine("WindowsInput Absolute Mouse connecting...");
                        _winAbsMouse.Connect();
                        Console.WriteLine("WindowsInput Absolute Mouse connected.");
                    }
                    else
                    {
                        Console.WriteLine("TetherScript Absolute Mouse for connecting...");
                        _absMouse.Connect();
                        Console.WriteLine("TetherScript Absolute Mouse for connected.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[MouseFeeder] Connect fail:\n" + ex);
            }

            try
            {
                Console.WriteLine("TetherScript Keyboard connecting...");
                _keyboard.Connect();
                Console.WriteLine("TetherScript Keyboard connected.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[KeyboardFeeder] Connect fail:\n" + ex);
            }
        }

        private static void LoadMapping(string path, bool dual, bool useRelMouse, bool useWindowsInputAbs)
        {
            _absMouse.ClearMapping();
            _keyboard.ClearMapping();
            _relMouse.ClearMapping();
            _gamepad.ClearMapping();
            _winAbsMouse.ClearMapping();

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

                if (device == "MOUSE" || device == "MOUSE2")
                {
                    MapMouseInput(device, cmd, gunBtn, dual, useRelMouse, useWindowsInputAbs);
                }
                else if (device == "KEYBOARD")
                {
                    if (byte.TryParse(cmd, out var keyCode))
                        _keyboard.AddMapping(gunBtn, keyCode);
                }
                else if (device == "GAMEPAD")
                {
                    if (int.TryParse(cmd, out var btnBit))
                        _gamepad.AddMapping(gunBtn, btnBit);
                }
            }

            Console.WriteLine($"[Mapping] Mouse: {_absMouse.MappingCount()} entries, Mouse2: {_relMouse.MappingCount()} entries, Gamepad2: {_gamepad.MappingCount()} entries, Keyboard: {_keyboard.MappingCount()} entries.");
        }

        private static void MapMouseInput(string device, string command, GunButton gunButton, bool dual, bool useRelMouse, bool useWindowsInputAbs)
        {
            bool isMouse1 = device.Equals("MOUSE", StringComparison.OrdinalIgnoreCase);
            bool isMouse2 = device.Equals("MOUSE2", StringComparison.OrdinalIgnoreCase);
            if (!isMouse1 && !isMouse2) return;
            if (isMouse2 && !dual) return; // MOUSE2 only valid in dual mode

            bool isLeft = command.Equals("Left", StringComparison.OrdinalIgnoreCase);
            bool isRight = command.Equals("Right", StringComparison.OrdinalIgnoreCase);
            bool isMiddle = command.Equals("Middle", StringComparison.OrdinalIgnoreCase);

            // TODO make feeders use a shared interface because this logic is ugly...
            if ((isMouse1 && dual) || (isMouse1 && !dual && !useRelMouse && !useWindowsInputAbs))
            {
                if (isLeft)
                    _absMouse.AddMapping(gunButton, MouseButton.Left);
                else if (isRight)
                    _absMouse.AddMapping(gunButton, MouseButton.Right);
                else if (isMiddle)
                    _absMouse.AddMapping(gunButton, MouseButton.Middle);
            }
            else if ((isMouse2 && dual && useRelMouse) || (isMouse1 && !dual && useRelMouse))
            {
                if (isLeft)
                    _relMouse.AddMapping(gunButton, MouseButton.Left);
                else if (isRight)
                    _relMouse.AddMapping(gunButton, MouseButton.Right);
                else if (isMiddle)
                    _relMouse.AddMapping(gunButton, MouseButton.Middle);
            }
            else if ((isMouse2 && dual && !useRelMouse) || (isMouse1 && !dual && useWindowsInputAbs))
            {
                if (isLeft)
                    _winAbsMouse.AddMapping(gunButton, global::WindowsInput.MouseButton.LeftButton);
                else if (isRight)
                    _winAbsMouse.AddMapping(gunButton, global::WindowsInput.MouseButton.RightButton);
                else if (isMiddle)
                    _winAbsMouse.AddMapping(gunButton, global::WindowsInput.MouseButton.MiddleButton);
            }
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
