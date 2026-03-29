using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;

namespace Guncon3Console.TetherScript
{
    internal sealed class GamepadFeeder : ITetherScriptFeeder
    {
        private readonly HIDController _hid = new HIDController();
        public HIDController Hid => _hid;

        // Map: logical gun button -> bit index in the gamepad Buttons field (0..15)
        private readonly Dictionary<GunButton, int> _mapping = new Dictionary<GunButton, int>();

        private ushort _buttons;
        public GamepadFeeder() { }

        public string Name => "TetherScript Gamepad";

        public ushort VendorId => (ushort)DriversConst.TTC_VENDORID;
        public ushort ProductId => (ushort)DriversConst.TTC_PRODUCTID_GAMEPAD;

        public bool IsConnected => _hid.Connected;

        public void Log(string message) => Console.WriteLine("[" + Name + "]: " + message);

        public void ClearMapping() => _mapping.Clear();

        public int MappingCount() => _mapping.Count;

        public void AddMapping(GunButton gunButton, dynamic mapping)
        {
            try
            {
                if (mapping is int i)
                    _mapping[gunButton] = i;
                else if (mapping is byte b)
                    _mapping[gunButton] = b;
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
            _hid.OnLog += OnHidLog;
            _hid.VendorID = VendorId;
            _hid.ProductID = ProductId;
            _hid.Connect();

            if (!_hid.Connected)
                throw new Exception("Could not connect to TetherScript Gamepad.");

            _buttons = 0;
        }

        public void Disconnect()
        {
            _hid.Disconnect();
            _hid.OnLog -= OnHidLog;
        }

        public void OnHidLog(object sender, LogArgs e) => Log(e.Msg);

        public void Feed(IGunState state)
        {
            _buttons = 0;
            foreach (var kv in _mapping)
            {
                if (!state.BtnState.TryGetValue(kv.Key, out bool pressed) || !pressed)
                    continue;

                int bit = kv.Value;
                if (bit < 0 || bit > 15) continue;
                _buttons = (ushort)(_buttons | (1 << bit));
            }

            var data = new SetFeatureGamepad
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = _buttons,
                LX = 0,
                LY = 0,
                RX = 0,
                RY = 0
            };

            byte[] buf = TetherScriptMarshal.StructToBytes(data);
            _hid.SendData(buf, (uint)buf.Length);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureGamepad
    {
        public byte ReportID;
        public byte CommandCode;
        public ushort Buttons;
        public short LX;
        public short LY;
        public short RX;
        public short RY;
    }
}
