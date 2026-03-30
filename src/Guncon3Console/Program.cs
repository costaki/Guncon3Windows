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
using Guncon3Console.Calibration;
using static Guncon3Console.GunStates.GunState;

namespace Guncon3Console
{
    internal static class Program
    {
        private static volatile bool _running = true;

        private static readonly TetherScript.AbsMouseFeeder _absMouse = new TetherScript.AbsMouseFeeder();
        private static readonly RelMouseFeeder _relMouse = new RelMouseFeeder();
        private static readonly TetherScript.KeyboardFeeder _keyboard = new TetherScript.KeyboardFeeder();
        private static readonly WindowsInput.AbsMouseFeeder _winAbsMouse = new WindowsInput.AbsMouseFeeder();
        private static readonly WindowsInput.KeyboardFeeder _winKeyboard = new WindowsInput.KeyboardFeeder();

        private static GunconDevice _gun1;
        private static GunconDevice _gun2;

        private static GunState _player1;
        private static GunState _player2;

        private static string AppPath;

        internal static class ProgramCalibration
        {
            public static bool TryRefresh(int player)
            {
                try
                {
                    if (player == 1)
                    {
                        _player1?.RefreshCalibration();
                        return true;
                    }
                    if (player == 2)
                    {
                        _player2?.RefreshCalibration();
                        return true;
                    }
                }
                catch { }
                return false;
            }
        }

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "GUNCON3 Console";
            PrintHeader();

            AppPath = AppDomain.CurrentDomain.BaseDirectory;

            // args (case-insensitive):
            //  - help|-h|/?     => show help and exit
            //  - keys           => show HID keycode table and exit
            //  - dump-hid       => dump present HID devices and exit
            //  - relmouse-manual=> open RelMouse manual sender UI and exit
            //
            // Runtime modes:
            //  - default is DUAL (one process reads up to 2 guns)
            //      P1 -> TetherScript AbsMouse + TetherScript Keyboard
            //      P2 -> WindowsInput AbsMouse + WindowsInput Keyboard
            //    If only 1 gun is detected, the app automatically switches to SINGLE.
            //  - single         => force SINGLE mode even if 2 guns are present
            //
            // Options:
            //  - test           => open test window showing gun input state (still feeds output)
            //  - 4by3           => enable 4:3-inside-16:9 X-scaling (for MAME setups)
            //  - relmouse       => output routing flag:
            //                     * SINGLE: applies to P1 (uses TetherScript Relative Mouse)
            //                     * DUAL:   applies to P2 (uses TetherScript Relative Mouse)
            //  - wininputabs    => output routing flag:
            //                     * SINGLE: applies to P1 (uses WindowsInput Absolute Mouse)
            //                     * DUAL:   ignored (P2 already uses WindowsInput AbsMouse when not using relmouse)
            bool dual = !args.Any(a => a.Equals("single", StringComparison.OrdinalIgnoreCase));
            bool testMode = args.Any(a => a.Equals("test", StringComparison.OrdinalIgnoreCase));
            bool force4by3 = args.Any(a => a.Equals("4by3", StringComparison.OrdinalIgnoreCase) || a.Equals("4:3", StringComparison.OrdinalIgnoreCase));
            bool useRelMouse = (args.Any(a => a.Equals("relmouse", StringComparison.OrdinalIgnoreCase)));
            bool useWindowsInputAbs = (!useRelMouse && args.Any(a => a.Equals("wininputabs", StringComparison.OrdinalIgnoreCase)));

            // === "help": show usage and exit ===
            if (args.Any(a => a.Equals("help", StringComparison.OrdinalIgnoreCase) || a.Equals("-h", StringComparison.OrdinalIgnoreCase) || a.Equals("/?", StringComparison.OrdinalIgnoreCase)))
            {
                PrintHelp();
                return;
            }

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

            try
            {
                Console.WriteLine("[Device Connection] Guncon3 connecting...");

                var infos = USBDevice.GetDevices(Constants.GunDeviceInterfaceGuid)
                                   .Where(x => x.VID == Constants.VendorId && x.PID == Constants.ProductId)
                                   .Take(2)
                                   .ToList();

                if (infos == null || infos.Count == 0)
                    throw new Exception("[Device Connection] Guncon3 device not found");

                if (dual && infos.Count < 2)
                {
                    dual = false;
                    Console.WriteLine("[Device Connection] Only one Guncon3 detected; switching to single mode.");
                }

                _gun1 = new GunconDevice(infos[0]);
                Console.WriteLine("[Device Connection] Guncon3 connected. #1=" + infos[0].DevicePath);

                if (dual)
                {
                    _gun2 = new GunconDevice(infos[1]);
                    Console.WriteLine("[Device Connection] Guncon3 connected. #2=" + infos[1].DevicePath);
                }
            }
            catch (Exception ex)
            {
                FailAndExit("[Device Connection] Could not connect to the Guncon3.", ex);
                return;
            }

            if (testMode)
            {
                Console.WriteLine("[Test] Opening test window...");
                using (var w = new TestWindow(_gun1, dual ? _gun2 : null))
                    Application.Run(w);
                return;
            }

            TryConnectFeeders(dual);

            _absMouse.Force4by3 = force4by3;
            _relMouse.Force4by3 = force4by3;
            _winAbsMouse.Force4by3 = force4by3;

            Console.WriteLine("[Options] 4by3 mode: " + (force4by3 ? "ENABLED" : "DISABLED"));

            IMouseFeeder p1MouseFeeder;
            IKeyboardFeeder p1KeyboardFeeder;
            IMouseFeeder p2MouseFeeder = null;
            IKeyboardFeeder p2KeyboardFeeder = null;
            if (!dual)
            {
                if (useRelMouse)
                {
                    p1MouseFeeder = _relMouse;
                    p1KeyboardFeeder = _keyboard;
                    Console.WriteLine("[Mode] SINGLE mode with TetherScript Relative Mouse & Keyboard output.");
                }
                else if (useWindowsInputAbs)
                {
                    p1MouseFeeder = _winAbsMouse;
                    p1KeyboardFeeder = _winKeyboard;
                    Console.WriteLine("[Mode] SINGLE mode with WindowsInput Absolute Mouse & Keyboard output.");
                }
                else
                {
                    p1MouseFeeder = _absMouse;
                    p1KeyboardFeeder = _keyboard;
                    Console.WriteLine("[Mode] SINGLE mode with TetherScript Absolute Mouse & Keyboard output.");
                }
            }
            else
            {
                p1MouseFeeder = _absMouse;
                p1KeyboardFeeder = _keyboard;
                Console.WriteLine("[Mode] DUAL mode with Player 1 / Gun 1 - TetherScript Relative Mouse & Keyboard output.");
                if (useRelMouse)
                {
                    p2MouseFeeder = _relMouse;
                    p2KeyboardFeeder = _winKeyboard;
                    Console.WriteLine("[Mode] DUAL mode with Player 2 / Gun 2 - TetherScript Relative Mouse & WindowsInput Keyboard output.");
                }
                else
                {
                    p2MouseFeeder = _winAbsMouse;
                    p2KeyboardFeeder = _winKeyboard;
                    Console.WriteLine("[Mode] DUAL mode with Player 2 / Gun 2 - WindowsInput Absolute Mouse & Keyboard output.");
                }

            }

            _player1 = new GunState(Player.Player1, _gun1, mouseFeeder: p1MouseFeeder, keyboardFeeder: p1KeyboardFeeder);
            _player2 = dual ? new GunState(Player.Player2, _gun2, mouseFeeder: p2MouseFeeder, keyboardFeeder: p2KeyboardFeeder) : null;

            LoadMappingsJson(dual, logOnly: true);

            string calibPathP1 = null;
            string modeString = null;
            if (!dual)
            {
                calibPathP1 = Path.Combine(AppPath, RectCalib.DefaultFileName);
                modeString = "SINGLE";
            }
            else
            {
                calibPathP1 = Path.Combine(AppPath, RectCalib.Player1FileName);
                modeString = "DUAL";
            }

            _player1.LoadNewCalibration(calibPathP1);
            if (!_player1.CalibrationIsValid)
            {
                Console.WriteLine($"[Calibration - {modeString} mode] Player 1 / Gun 1 calibration not found/invalid. Starting calibration...");
                RecalibrateGun(_player1, dual);

                if (!_player1.CalibrationIsValid)
                {
                    FailAndExit($"[Calibration - {modeString} mode] Cannot run without calibration.");
                    return;
                }
            }
            else
            {
                Console.WriteLine($"[Calibration - {modeString} mode] Player 1 / Gun 1 calibration loaded from " + _player1.Calibration.CalibrationPath);
            }

            if (dual)
             {
                _player2.LoadNewCalibration(Path.Combine(AppPath, RectCalib.Player2FileName));
                if (!_player2.CalibrationIsValid)
                {
                    Console.WriteLine($"[Calibration - {modeString} mode] Player 2 / Gun 2 calibration not found/invalid. Starting calibration...");
                    RecalibrateGun(_player2, dual);
                    if (!_player2.CalibrationIsValid)
                    {
                        FailAndExit($"[Calibration - {modeString} mode] Cannot run dual mode without both calibrations.");
                        return;
                    }
                }
                else
                {
                    Console.WriteLine($"[Calibration - {modeString} mode] Player 2 / Gun 2 calibration loaded from " + _player2.Calibration.CalibrationPath);
                }
            }

            Console.WriteLine("Ready to use!   (F12 = recalibrate all,  1/2 = recalibrate P1/P2,  T = test screen,  M = mappings UI,  R = reload mapping.json,  ESC = exit)");

            while (_running)
            {
                try
                {
                    _player1.UpdateAndFeed();
                    if (dual)
                    {
                        _player2.UpdateAndFeed();
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
                            using (var w = new TestWindow(_gun1, dual ? _gun2 : null))
                                Application.Run(w);
                        }
                        catch { }
                    }
                    else if (k.Key == ConsoleKey.F12)
                    {
                        RecalibrateGun(_player1, dual);
                        if (dual)
                            RecalibrateGun(_player2, dual);
                    }
                    else if (k.Key == ConsoleKey.D1)
                    {
                        RecalibrateGun(_player1, dual);
                    }
                    else if (k.Key == ConsoleKey.D2)
                    {
                        if (dual && _player2 != null)
                            RecalibrateGun(_player2, dual);
                    }
                    else if (k.Key == ConsoleKey.R)
                    {
                        LoadMappingsJson(dual, logOnly: false);
                    }
                    else if (k.Key == ConsoleKey.M)
                    {
                        try
                        {
                            var p1 = _player1.MappingPath;
                            var p2 = _player2?.MappingPath;

                            var p1Feeders = $"Mouse={p1MouseFeeder?.Name ?? "(none)"}, Keyboard={p1KeyboardFeeder?.Name ?? "(none)"}";
                            var p2Feeders = dual ? $"Mouse={p2MouseFeeder?.Name ?? "(none)"}, Keyboard={p2KeyboardFeeder?.Name ?? "(none)"}" : null;

                            using (var w = new Guncon3Console.Mapping.MappingEditorForm(p1, p2, enablePlayer2: dual, player1Device: _gun1, player2Device: _gun2, player1Feeders: p1Feeders, player2Feeders: p2Feeders))
                                w.ShowDialog();
                            LoadMappingsJson(dual, logOnly: false);
                        }
                        catch { }
                    }

                    Console.WriteLine("Ready to use!   (F12 = recalibrate all,  1/2 = recalibrate P1/P2,  T = test screen,  M = mappings UI,  R = reload mapping.json,  ESC = exit)");
                }

                Thread.Sleep(1);
            }
            try { _absMouse.Disconnect(); } catch { }
            try { _relMouse.Disconnect(); } catch { }
            try { _keyboard.Disconnect(); } catch { }
            try { _winAbsMouse.Disconnect(); } catch { }
            try { _winKeyboard.Disconnect(); } catch { }
            try { _gun1?.Dispose(); } catch { }
            try { _gun2?.Dispose(); } catch { }
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

        private static void RecalibrateGun(GunState gunState, bool dual)
        {
            string modeString = "SINGLE";
            if (dual)
                modeString = "DUAL";

            string gunString = "Player 1 / Gun 1";
            if (gunState.PlayerNum == Player.Player2)
                gunString = "Player 2 / Gun 2";

            Console.WriteLine($"[Calibration - {modeString} mode] Calibrating {gunString}...");
            LaunchCalibrationWindowModal(gunState.Calibration.CalibrationPath, $"Calibrating: {gunString}", gunState.Device);

            gunState.RefreshCalibration();
            if (!gunState.CalibrationIsValid)
            {
                FailAndExit($"[Calibration - {modeString} mode] {gunString} calibration failed or invalid.");
                return;
            }
            Console.WriteLine($"[Calibration - {modeString} mode] {gunString} calibration reloaded: " + gunState.Calibration.CalibrationPath);
        }

        private static void TryConnectFeeders(bool dual)
        {
            try
            {
                Console.WriteLine("[Feeder Connection] TetherScript Absolute Mouse connecting...");
                _absMouse.Connect();
                Console.WriteLine("[Feeder Connection] TetherScript Absolute Mouse connected.");

                Console.WriteLine("[Feeder Connection] TetherScript Relative Mouse connecting...");
                _relMouse.Connect();
                Console.WriteLine("[Feeder Connection] TetherScript Relative Mouse connected.");

                Console.WriteLine("[Feeder Connection] TetherScript Keyboard connecting...");
                _keyboard.Connect();
                Console.WriteLine("[Feeder Connection] TetherScript Keyboard connected.");

                Console.WriteLine("[Feeder Connection] WindowsInput Absolute Mouse connecting...");
                _winAbsMouse.Connect();
                Console.WriteLine("[Feeder Connection] WindowsInput Absolute Mouse connected.");

                Console.WriteLine("[Feeder Connection] WindowsInput Keyboard connecting...");
                _winKeyboard.Connect();
                Console.WriteLine("[Feeder Connection] WindowsInput Keyboard connected.");

                if (!_absMouse.IsConnected || !_relMouse.IsConnected || !_keyboard.IsConnected || (dual && (!_winAbsMouse.IsConnected || !_winKeyboard.IsConnected)))
                {
                    FailAndExit("[Feeder Connection] Could not connect to all feeders. Check that TetherScript and WindowsInput feeder services are running, and that the devices are properly configured in TetherScript.");
                    return;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Feeder Connection] Connect fail:\n" + ex);
            }
        }

        private static void LoadMappingsJson(bool dual, bool logOnly)
        {
            if (!logOnly)
            {
                Console.WriteLine("[Mapping] Reloading mapping json…");
                _player1.LoadMapping();
                if (dual && _player2 != null)
                    _player2.LoadMapping();
            }

            Console.WriteLine($"[Mapping] P1 Mouse: {_player1.MouseFeeder?.MappingCount() ?? 0}, P1 Keyboard: {_player1.KeyboardFeeder?.MappingCount() ?? 0}");
            if (dual && _player2 != null)
                Console.WriteLine($"[Mapping] P2 Mouse: {_player2.MouseFeeder?.MappingCount() ?? 0}, P2 Keyboard: {_player2.KeyboardFeeder?.MappingCount() ?? 0}");
            Console.WriteLine("[Mapping] OK.");
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
            Console.WriteLine("Use '4by3' flag to enable 4:3 inside 16:9 mode");
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: Guncon3Console.exe [options]");
            Console.WriteLine();
            Console.WriteLine("Modes:");
            Console.WriteLine("  (default)    Dual mode (reads up to 2 guns). If only 1 gun is found, switches to single automatically.");
            Console.WriteLine("  single       Force single-gun mode.");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  test         Open a test window showing gun input state.");
            Console.WriteLine("  4by3         Enable 4:3-inside-16:9 X-scaling (MAME-style setups).");
            Console.WriteLine("  relmouse     Output routing flag:");
            Console.WriteLine("               - SINGLE: affects P1 (TetherScript Relative Mouse)");
            Console.WriteLine("               - DUAL:   affects P2 (TetherScript Relative Mouse)");
            Console.WriteLine("  wininputabs  Output routing flag:");
            Console.WriteLine("               - SINGLE: affects P1 (WindowsInput Absolute Mouse)");
            Console.WriteLine("               - DUAL:   ignored (P2 already uses WindowsInput AbsMouse when not using relmouse)");
            Console.WriteLine();
            Console.WriteLine("Commands:");
            Console.WriteLine("  help|-h|/?   Show this help and exit.");
            Console.WriteLine("  keys         Print HID keycodes (4..111) and exit.");
            Console.WriteLine("  dump-hid     Dump HID devices and exit.");
            Console.WriteLine("  relmouse-manual  Open RelMouse manual sender UI and exit.");
            Console.WriteLine();
            Console.WriteLine("Runtime hotkeys (console):");
            Console.WriteLine("  ESC  Exit");
            Console.WriteLine("  T    Test window");
            Console.WriteLine("  M    Mapping UI");
            Console.WriteLine("  R    Reload mapping json");
            Console.WriteLine("  F12  Recalibrate all");
            Console.WriteLine("  1    Recalibrate Player 1");
            Console.WriteLine("  2    Recalibrate Player 2 (dual mode)");
            Console.WriteLine();
            Console.WriteLine("Press any key to exit…");
            try { Console.ReadKey(true); } catch { }
        }

        // === Full keycode table (4..111) ===
        private static void PrintKeyCodes()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("KEYCODE\tKEY");
            Console.ResetColor();

            var values = Enum.GetValues(typeof(HidKeyCode));
            foreach (HidKeyCode key in values)
            {
                var code = (byte)key;
                if (code < 4 || code > 111)
                    continue;

                Console.WriteLine($"{code}\t{key.ToString().ToUpperInvariant()}");
            }

            Console.WriteLine();
            Console.WriteLine("Press any key to exit…");
            try { Console.ReadKey(true); } catch { }
        }
    }
}
