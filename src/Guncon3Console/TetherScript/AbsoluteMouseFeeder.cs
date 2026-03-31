using System;
using System.Runtime.InteropServices;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;
using Guncon3Console.Common;
using System.Threading;

namespace Guncon3Console.TetherScript
{
    internal sealed class AbsoluteMouseFeeder : BaseDisposableFeeder<MouseButton>, IMouseFeeder, ITetherScriptFeeder
    {
        private readonly HidController _hid = new HidController();
        public HidController Hid => _hid;
        public bool Force4by3 { get; set; } = false;
        private byte _btns;

        public AbsoluteMouseFeeder() { }

        public override string Name => "TetherScript AbsMouse";

        public ushort VendorId => (ushort)DriversConst.TTC_VENDORID;
        public ushort ProductId => (ushort)DriversConst.TTC_PRODUCTID_MOUSEABS;

        public override bool IsConnected => _hid.Connected;

        public override void Connect()
        {
            _hid.OnLog += OnHidLog;
            _hid.VendorID = VendorId;
            _hid.ProductID = ProductId;
            _hid.Connect();

            if (!_hid.Connected)
                throw new Exception("Coud not connect to TetherScript's AbsMouse");
        }

        public override void Disconnect()
        {
            _hid.Disconnect();
            _hid.OnLog -= OnHidLog;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disconnect();
                _hid.Dispose();
            }
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

        public override void Feed(IGunState state)
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
