using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
using Guncon3Console.Common;
using GunconUSB;
using System;

namespace Guncon3Console.vMulti
{
    /// <summary>
    /// Feeds the stock vmulti absolute mouse endpoint.
    ///
    /// This mirrors vmulti's native client logic:
    /// - enumerate HID devices
    /// - match vmulti VID/PID
    /// - match HID caps UsagePage=0xFF00, Usage=0x0001 (control device)
    /// - write a 0x41-byte control report:
    ///     [VMultiControlReportHeader][VMultiMouseReport][padding...]
    /// </summary>
    internal sealed class AbsoluteMouseFeeder : BaseDisposableFeeder<MouseButton>, IMouseFeeder
    {
        private readonly HidController _hid = new HidController();

        public bool Force4by3 { get; set; } = false;

        private byte _buttons;

        public override string Name => "vMulti AbsMouse";

        public override bool IsConnected => _hid.Connected;

        public override void Connect()
        {
            _hid.OnLog += OnHidLog;
            _hid.Connect();

            if (!_hid.Connected)
                throw new Exception("Could not connect to vmulti absolute mouse control device.");
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

        public override void Feed(IGunState state)
        {
            short absX = 0;
            short absY = 0;

            if (state.IsInsideScreen)
            {
                absX = state.ABS_X;
                absY = state.ABS_Y;

                // Same behaviour as your existing feeder for 4:3 inside 16:9.
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

            // Clamp to vmulti's declared mouse range: 0x0000..0x7FFF
            ushort x = ClampToUShort15(absX);
            ushort y = ClampToUShort15(absY);

            _buttons = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                switch (map.Value)
                {
                    case MouseButton.Left:
                        _buttons |= HidController.MOUSE_BUTTON_1;
                        break;
                    case MouseButton.Right:
                        _buttons |= HidController.MOUSE_BUTTON_2;
                        break;
                    case MouseButton.Middle:
                        _buttons |= HidController.MOUSE_BUTTON_3;
                        break;
                }
            }

            _hid.SendAbsoluteMouse(_buttons, x, y, 0);
        }

        private static ushort ClampToUShort15(short value)
        {
            if (value < 0) return 0;
            if (value > 0x7FFF) return 0x7FFF;
            return (ushort)value;
        }
    }
}