using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
using Guncon3Console.Common;
using GunconUSB;
using System;
using System.Collections.Generic;

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
    internal sealed class VMultiAbsMouseFeeder : IMouseFeeder
    {
        private readonly VMultiHidController HID = new VMultiHidController();

        // Logical gun button -> vmulti mouse button mapping
        private readonly Dictionary<GunButton, MouseButton> _mapping = new Dictionary<GunButton, MouseButton>();

        public bool Force4by3 { get; set; } = false;

        private byte _buttons;

        public string Name => "vMulti AbsMouse";

        public bool IsConnected => HID.Connected;

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
            HID.OnLog += OnHidLog;
            HID.Connect();

            if (!HID.Connected)
                throw new Exception("Could not connect to vmulti absolute mouse control device.");
        }

        public void Disconnect()
        {
            HID.Disconnect();
            HID.OnLog -= OnHidLog;
        }

        public void OnHidLog(object sender, LogArgs e) => Log(e.Msg);

        public void Feed(IGunState state)
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
                        _buttons |= VMultiHidController.MOUSE_BUTTON_1;
                        break;
                    case MouseButton.Right:
                        _buttons |= VMultiHidController.MOUSE_BUTTON_2;
                        break;
                    case MouseButton.Middle:
                        _buttons |= VMultiHidController.MOUSE_BUTTON_3;
                        break;
                }
            }

            HID.SendAbsoluteMouse(_buttons, x, y, 0);
        }

        private static ushort ClampToUShort15(short value)
        {
            if (value < 0) return 0;
            if (value > 0x7FFF) return 0x7FFF;
            return (ushort)value;
        }
    }
}