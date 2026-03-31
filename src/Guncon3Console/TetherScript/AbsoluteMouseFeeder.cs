using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;
using Guncon3Console.Common;

// Important: always use the enum from the GunconUSB project (singular)


namespace Guncon3Console.TetherScript
{
    internal sealed class AbsoluteMouseFeeder : IMouseFeeder, ITetherScriptFeeder
    {
        private readonly HidController _hid = new HidController();
        public HidController Hid => _hid;

        // Map: logical gun button (public enum in GunconUSB) -> TetherScript virtual mouse button
        private readonly Dictionary<GunButton, MouseButton> _mapping = new Dictionary<GunButton, MouseButton>();

        public bool Force4by3 { get; set; } = false;
        private byte _btns;

        public AbsoluteMouseFeeder() { }

        public string Name => "TetherScript AbsMouse";

        public ushort VendorId => (ushort)DriversConst.TTC_VENDORID;
        public ushort ProductId => (ushort)DriversConst.TTC_PRODUCTID_MOUSEABS;

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
            _hid.VendorID = VendorId;            // VendorId TetherScript
            _hid.ProductID = ProductId;  // ProductId Mouse Abs
            _hid.Connect();

            if (!_hid.Connected)
                throw new Exception("Coud not connect to TetherScript's AbsMouse");
        }

        public void Disconnect()
        {
            _hid.Disconnect();
            _hid.OnLog -= OnHidLog;
        }

        public void OnHidLog(object sender, LogArgs e) => Log(e.Msg);

        public void Send_Data_To_MouseAbs(ushort x, ushort y)
        {
            var data = new SetFeatureMouseAbs
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = _btns,
                X = x,
                Y = y
            };

            byte[] buf = TetherScriptMarshal.StructToBytes(data);
            _hid.SendData(buf, (uint)buf.Length);
        }

        public void Feed(IGunState state)
        {
            short absX = 0;
            short absY = 0;

            if (state.IsInsideScreen)
            {
                absX = state.ABS_X;
                absY = state.ABS_Y;

                // 4:3 inside 16:9 (MAME)
                if (Force4by3)
                {
                    if (!Helper.IsInsideCentered4By3(absX))
                    {
                        // Treat outside the 4:3 region as out-of-bounds.
                        absX = (short)Helper.GunAxisMax;
                        absY = (short)Helper.GunAxisMax;
                    }
                    else
                    {
                        absX = (short)Helper.ConvertRange4By3(absX);
                    }
                }
            }

            // buttons
            _btns = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) _btns = (byte)(_btns | 1);
                if (map.Value == MouseButton.Right) _btns = (byte)(_btns | (1 << 1));
                if (map.Value == MouseButton.Middle) _btns = (byte)(_btns | (1 << 2));
            }

            Send_Data_To_MouseAbs((ushort)absX, (ushort)absY);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureMouseAbs
    {
        public byte ReportID;
        public byte CommandCode;
        public byte Buttons;
        public ushort X;
        public ushort Y;
    }
}
