using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB; // <-- GunButton
using Guncon3Console.GunStates; // <-- IGunState
using Guncon3Console.Feeders;

namespace Guncon3Console.TetherScript
{
    internal sealed class KeyboardFeeder : IFeeder
    {
        private readonly HIDController _hid = new HIDController();

        private readonly uint _fTimeout = 5000;

        // Logical mapping -> keycode (numbers from "keys")
        private readonly Dictionary<GunButton, byte> _mapping = new Dictionary<GunButton, byte>();

        // Last sent state
        private readonly byte[] _lastKeys = new byte[6];
        private bool _lastHadAny = false;
        private long _lastSendTicks = 0;
        private readonly long _minSendIntervalTicks = TimeSpan.FromMilliseconds(2).Ticks; // ~500 Hz máx
        public KeyboardFeeder() { }

        public string Name => "TetherScript Keyboard";

        public bool IsConnected => _hid.Connected;

        public void ClearMapping() => _mapping.Clear();

        public int MappingCount() => _mapping.Count;

        public void AddMapping(GunButton gunButton, dynamic mapping)
        {
            try
            {
                if (mapping is byte b)
                    _mapping[gunButton] = b;
                else if (mapping is int i)
                    _mapping[gunButton] = unchecked((byte)i);
            }
            catch { }
        }

        public dynamic GetMapping(GunButton gunButton)
        {
            if (_mapping.TryGetValue(gunButton, out var v))
                return v;
            return null;
        }

        public void Connect()
        {
            _hid.OnLog += Log;
            _hid.VendorID = (ushort)DriversConst.TTC_VENDORID;
            _hid.ProductID = (ushort)DriversConst.TTC_PRODUCTID_KEYBOARD;
            _hid.Connect();
            if (!_hid.Connected)
                throw new Exception("Could not connect to TetherScript Keyboard.");

            Array.Clear(_lastKeys, 0, _lastKeys.Length);
            _lastHadAny = false;
            _lastSendTicks = 0;
        }

        public void Disconnect()
        {
            try
            {
                // Release keys for safety
                Send(0, 0, 0, 0, 0, 0, 0, 0);
            }
            catch { }
            _hid.Disconnect();
            _hid.OnLog -= Log;
        }

        private static void Log(object s, LogArgs e) => Console.WriteLine("Keyboard " + e.Msg);

        public void Send(byte Modifier, byte Padding, byte Key0, byte Key1, byte Key2, byte Key3, byte Key4, byte Key5)
        {
            SetFeatureKeyboard data = new SetFeatureKeyboard
            {
                ReportID = 1,
                CommandCode = 2,
                Timeout = _fTimeout / 5,
                Modifier = Modifier,
                Padding = Padding,
                Key0 = Key0,
                Key1 = Key1,
                Key2 = Key2,
                Key3 = Key3,
                Key4 = Key4,
                Key5 = Key5
            };

            byte[] buf = GetBytes(data, Marshal.SizeOf(data));
            _hid.SendData(buf, (uint)buf.Length);
        }

        public void Ping()
        {
            SetFeatureKeyboard data = new SetFeatureKeyboard
            {
                ReportID = 1,
                CommandCode = 3,
                Timeout = _fTimeout / 5
            };
            byte[] buf = GetBytes(data, Marshal.SizeOf(data));
            _hid.SendData(buf, (uint)buf.Length);
        }

        private static byte[] GetBytes(SetFeatureKeyboard sfj, int size)
        {
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(sfj, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return arr;
        }

        // Hold keys: no "machine-gun" repeats
        public void Feed(IGunState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            // Keep the driver alive
            Ping();

            if (_mapping.Count == 0) return;

            // Build current set
            byte[] current = new byte[6];
            int idx = 0;

            foreach (var kv in _mapping)
            {
                if (idx >= 6) break;

                bool pressed = state.BtnState.TryGetValue(kv.Key, out var v) && v;
                if (pressed)
                {
                    byte code = kv.Value;

                    bool already = false;
                    for (int i = 0; i < idx; i++)
                        if (current[i] == code) { already = true; break; }

                    if (!already) current[idx++] = code;
                }
            }

            // Sort for stable comparison
            for (int i = 0; i < idx - 1; i++)
                for (int j = i + 1; j < idx; j++)
                    if (current[j] < current[i]) { byte t = current[i]; current[i] = current[j]; current[j] = t; }

            bool haveAny = idx > 0;

            bool changed = (haveAny != _lastHadAny);
            if (!changed)
            {
                for (int i = 0; i < 6; i++)
                {
                    byte a = (i < idx) ? current[i] : (byte)0;
                    if (_lastKeys[i] != a) { changed = true; break; }
                }
            }

            long now = DateTime.UtcNow.Ticks;
            if (!changed && (now - _lastSendTicks) < _minSendIntervalTicks)
                return;

            if (changed)
            {
                byte k0 = (idx > 0) ? current[0] : (byte)0;
                byte k1 = (idx > 1) ? current[1] : (byte)0;
                byte k2 = (idx > 2) ? current[2] : (byte)0;
                byte k3 = (idx > 3) ? current[3] : (byte)0;
                byte k4 = (idx > 4) ? current[4] : (byte)0;
                byte k5 = (idx > 5) ? current[5] : (byte)0;

                Send(0, 0, k0, k1, k2, k3, k4, k5);

                for (int i = 0; i < 6; i++)
                    _lastKeys[i] = (i < idx) ? current[i] : (byte)0;

                _lastHadAny = haveAny;
                _lastSendTicks = now;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureKeyboard
    {
        public byte ReportID;
        public byte CommandCode;
        public uint Timeout;
        public byte Modifier;
        public byte Padding;
        public byte Key0;
        public byte Key1;
        public byte Key2;
        public byte Key3;
        public byte Key4;
        public byte Key5;
    }
}
