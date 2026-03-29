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
using Guncon3Console.Mapping;

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
        private static readonly TetherScript.KeyboardFeeder _keyboard = new TetherScript.KeyboardFeeder();
        private static readonly WindowsInput.KeyboardFeeder _winKeyboard = new WindowsInput.KeyboardFeeder();

        private const string MappingP1Json = "mapping.p1.json";
        private const string MappingP2Json = "mapping.p2.json";

        private static GunconDevice _gun1ForCal;
        private static GunconDevice _gun2ForCal;

        private static GunState _player1;
        private static GunState _player2;

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

            _player1 = new GunState(gun1, dual ? _rectP1 : _rect, mouseFeeder: _absMouse, keyboardFeeder: _keyboard);
            _player2 = dual ? new GunState(gun2, _rectP2, mouseFeeder: useRelMouse ? (IMouseFeeder)_relMouse : _winAbsMouse, keyboardFeeder: _winKeyboard) : null;

            LoadMappingsJson(dual);
            Console.WriteLine("Mapping OK (json).");
            Console.WriteLine("Ready to use!   (F12 = recalibrate,  T = test screen,  R = reload mapping.txt,  ESC = exit)");


            // Test window is launched modally via Application.Run when requested.

            while (_running)
            {
                if (dual)
                {
                    _player1.Update();
                    _player2.Update();
                }
                else
                {
                    _player1.Update();
                }

                try
                {
                    if (dual)
                    {
                        _player1.Feed();
                        _player2.Feed();
                    }
                    else
                    {
                        _player1.Feed();
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
                        Console.WriteLine("[Mapping] Reloading mapping json…");
                        LoadMappingsJson(dual);
                        Console.WriteLine("[Mapping] OK.");
                    }
                }

                Thread.Sleep(1);
            }
            try { if (useRelMouse) _relMouse.Disconnect(); } catch { }
            try { if (dual || useWindowsInputAbs) _winAbsMouse.Disconnect(); } catch { }
            try { _absMouse.Disconnect(); } catch { }
            try { _keyboard.Disconnect(); } catch { }
            try { _winKeyboard.Disconnect(); } catch { }
            try { gun1?.Dispose(); } catch { }
            try { gun2?.Dispose(); } catch { }
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
            Console.WriteLine("[Calibration] Single mode: calibrating...");
            LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibDefault), "Calibrating: Player 1 / Gun 1", _gun1ForCal);

            _player1.Calibration.Refresh();

            Console.WriteLine("[Calibration] Reloaded: " + CalibDefault);
        }

        private static void RecalibrateDual()
        {
            Console.WriteLine("[Calibration] Dual mode: calibrating P1...");
            LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP1), "Calibrating: Player 1 / Gun 1", _gun1ForCal);

            Console.WriteLine("[Calibration] Dual mode: calibrating P2...");
            LaunchCalibrationWindowModal(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibP2), "Calibrating: Player 2 / Gun 2", _gun2ForCal);

            _player1.Calibration.Refresh();
            _player2.Calibration.Refresh();

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

        private static void LoadMappingsJson(bool dual)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var p1Path = Path.Combine(baseDir, MappingP1Json);
            var p1 = GunMappingStore.Load(p1Path);
            GunMappingStore.ApplyToFeeders(p1, _player1.MouseFeeder, _player1.KeyboardFeeder);

            if (dual && _player2 != null)
            {
                var p2Path = Path.Combine(baseDir, MappingP2Json);
                var p2 = GunMappingStore.Load(p2Path);
                GunMappingStore.ApplyToFeeders(p2, _player2.MouseFeeder, _player2.KeyboardFeeder);
            }

            Console.WriteLine($"[Mapping] P1 Mouse: {_player1.MouseFeeder?.MappingCount() ?? 0}, P1 Keyboard: {_player1.KeyboardFeeder?.MappingCount() ?? 0}");
            if (dual && _player2 != null)
                Console.WriteLine($"[Mapping] P2 Mouse: {_player2.MouseFeeder?.MappingCount() ?? 0}, P2 Keyboard: {_player2.KeyboardFeeder?.MappingCount() ?? 0}");
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
