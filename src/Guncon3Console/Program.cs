using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GunconUSB;                         // gun reader (GunconUSB project)
using Guncon3Console.TetherScript;      // TetherScript feeders (mouse/keyboard)

namespace Guncon3Console
{
    internal static class Program
    {
        private static RectCalib _rect;
        private static volatile bool _running = true;

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "GUNCON3";
            PrintHeader();

            // Simple arg parsing: `relmouse` => use TetherScript Virtual Mouse Rel
            bool useRelMouse = (args.Length > 0 && args[0].Equals("relmouse", StringComparison.OrdinalIgnoreCase));

            // === "keys": show keycode table and exit ===
            if (args.Length > 0 && args[0].Equals("keys", StringComparison.OrdinalIgnoreCase))
            {
                PrintKeyCodes();
                return;
            }

            // === "dump-hid": dump present HID devices and exit ===
            if (args.Length > 0 && args[0].Equals("dump-hid", StringComparison.OrdinalIgnoreCase))
            {
                new HIDController().DumpTetherscriptCandidates();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            try
            {
                Console.WriteLine("Guncon3 connecting...");
                GunconReader.Connect();
                Console.WriteLine("Guncon3 connected.");
            }
            catch (Exception ex)
            {
                FailAndExit("Could not connect to the Guncon3.", ex);
                return;
            }

            if (!LoadRectCalib())
            {
                Console.WriteLine("calibration_rect.txt not found. Opening calibration (modal)...");
                LaunchCalibrationWindowModal();
                if (!LoadRectCalib())
                {
                    FailAndExit("Cannot run without calibration (calibration could not be obtained).");
                    return;
                }
            }
            else
            {
                Console.WriteLine("Calibration loaded from calibration_rect.txt");
            }

            TryConnectFeeders(useRelMouse);
            LoadMapping("mapping.txt");

            // If using RelMouse, mirror mouse mappings into RelMouseFeeder.
            if (useRelMouse)
            {
                RelMouseFeeder.Mapping.Clear();
                foreach (var kv in AbsMouseFeeder.Mapping)
                    RelMouseFeeder.Mapping[kv.Key] = kv.Value;
            }

            Console.WriteLine("Mapping OK.");
            Console.WriteLine("Ready to use!   (F12 = recalibrate,  R = reload mapping.txt,  ESC = exit)");

            while (_running)
            {
                GunconReader.Read();

                if (_rect != null && _rect.IsValid())
                {
                    double rawX = GunState.RAW_X;
                    double rawY = GunState.RAW_Y;
                    var (px, py) = _rect.Map(rawX, rawY);

                    double nx = (_rect.ScreenW > 1) ? (px / (_rect.ScreenW - 1)) : 0.0;
                    double ny = (_rect.ScreenH > 1) ? (py / (_rect.ScreenH - 1)) : 0.0;
                    if (nx < 0) nx = 0; if (nx > 1) nx = 1;
                    if (ny < 0) ny = 0; if (ny > 1) ny = 1;

                    short ax = (short)Math.Round(nx * 32767.0);
                    short ay = (short)Math.Round(ny * 32767.0);

                    GunState.ABS_X = ax;
                    GunState.ABS_Y = ay;
                }

                try
                {
                    if (useRelMouse)
                        RelMouseFeeder.Feed();
                    else
                        AbsMouseFeeder.Feed();

                    KeyboardFeeder.Feed();
                }
                catch { }

                if (Console.KeyAvailable)
                {
                    var k = Console.ReadKey(true);
                    if (k.Key == ConsoleKey.Escape)
                        _running = false;
                    else if (k.Key == ConsoleKey.F12)
                        Recalibrate();
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
                        Console.WriteLine("[Mapping] OK.");
                    }
                }

                Thread.Sleep(1);
            }

            try { if (useRelMouse) RelMouseFeeder.Disconnect(); else AbsMouseFeeder.Disconnect(); } catch { }
            try { KeyboardFeeder.Disconnect(); } catch { }
            try { GunconReader.Disconnect(); } catch { }
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
                using (var w = new CalibrationWindow())
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
            LaunchCalibrationWindowModal();
            if (LoadRectCalib())
                Console.WriteLine("Calibration loaded from calibration_rect.txt");
            else
                Console.WriteLine("WARNING: calibration_rect.txt was not created");
        }

        private static void TryConnectFeeders(bool useRelMouse)
        {
            try
            {
                Console.WriteLine(useRelMouse ? "MouseRel Connecting..." : "Mouse Connecting...");
                if (useRelMouse)
                    RelMouseFeeder.Connect();
                else
                    AbsMouseFeeder.Connect();
                Console.WriteLine(useRelMouse ? "MouseRel Connected. (TetherScript)" : "Mouse Connected. (TetherScript)");
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
                else if (device == "KEYBOARD")
                {
                    if (byte.TryParse(cmd, out var keyCode))
                        KeyboardFeeder.Mapping[gunBtn] = keyCode;
                }
            }

            Console.WriteLine($"[Mapping] Mouse: {AbsMouseFeeder.Mapping.Count} entries, Keyboard: {KeyboardFeeder.Mapping.Count} entries.");
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
