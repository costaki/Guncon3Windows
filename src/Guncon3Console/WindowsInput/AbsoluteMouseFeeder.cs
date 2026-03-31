using System;
using System.Collections.Generic;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;
using Guncon3Console.Common;

namespace Guncon3Console.WindowsInput
{
    internal sealed class AbsoluteMouseFeeder : IMouseFeeder, IFeeder
    {
        // Map: logical gun button (public enum in GunconUSB) -> mouse button
        private readonly Dictionary<GunButton, MouseButton> _mapping = new Dictionary<GunButton, MouseButton>();

        public bool Force4by3 { get; set; } = false;

        private byte _prevButtons;

        public AbsoluteMouseFeeder() { }

        public string Name => "WindowsInput AbsMouse";

        public bool IsConnected => true;

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
            _prevButtons = 0;
        }

        public void Disconnect()
        {
        }

        public void Feed(IGunState state)
        {
            uint flags = 0;
            ushort absX = 0;
            ushort absY = 0;

            if (state.IsInsideScreen)
            {
                var x = state.ABS_X;
                var y = state.ABS_Y;

                // 4:3 inside 16:9 (MAME)
                if (Force4by3)
                {
                    if (!Helper.IsInsideCentered4By3(x))
                    {
                        // Out-of-bounds when outside the 4:3 region.
                        x = (short)Helper.GunAxisMax;
                        y = (short)Helper.GunAxisMax;
                    }
                    else
                    {
                        x = (short)Helper.ConvertRange4By3(x);
                    }
                }

                // mouse_event w/ ABSOLUTE expects normalized 0..65535 when used with MOUSEEVENTF_ABSOLUTE.
                absX = ConvertAbsToUShort(x, state.ScreenW);
                absY = ConvertAbsToUShort(y, state.ScreenH);

                flags |= (NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK);
            }

            var buttons = ComputeButtonsMask(state);

            flags |= ComputeButtonTransitionFlags(buttons);

            // One mouse_event per Feed call.
            // When outside the screen, do not include MOVE/ABSOLUTE; only send button transitions.
            if (flags != 0)
            {
                if (!state.IsInsideScreen)
                {
                    // Send out-of-bounds.
                    flags |= (NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE | NativeMethods.MOUSEEVENTF_VIRTUALDESK);
                    absX = 65535;
                    absY = 65535;
                }

                NativeMethods.mouse_event(flags, absX, absY, 0, UIntPtr.Zero);
            }

            _prevButtons = buttons;
        }

        private uint ComputeButtonTransitionFlags(byte buttons)
        {
            uint flags = 0;

            flags |= ComputeButtonTransitionFlag(buttons, 0, NativeMethods.MOUSEEVENTF_LEFTDOWN, NativeMethods.MOUSEEVENTF_LEFTUP);
            flags |= ComputeButtonTransitionFlag(buttons, 1, NativeMethods.MOUSEEVENTF_RIGHTDOWN, NativeMethods.MOUSEEVENTF_RIGHTUP);
            flags |= ComputeButtonTransitionFlag(buttons, 2, NativeMethods.MOUSEEVENTF_MIDDLEDOWN, NativeMethods.MOUSEEVENTF_MIDDLEUP);

            return flags;
        }

        private uint ComputeButtonTransitionFlag(byte buttons, int bit, uint downFlag, uint upFlag)
        {
            var mask = (byte)(1 << bit);
            var was = (_prevButtons & mask) != 0;
            var now = (buttons & mask) != 0;

            if (was == now)
                return 0;

            return now ? downFlag : upFlag;
        }

        private static ushort ConvertAbsToUShort(short abs, int calibratedSize)
        {
            // RectCalib produces 0..32767 regardless of resolution. If we know the calibrated resolution,
            // map that into 0..65535 so the cursor reaches the full screen extents.
            if (abs <= 0)
                return 0;

            if (calibratedSize > 1)
            {
                // Convert abs(0..32767) -> pixel(0..size-1) -> normalized(0..65535)
                double n = abs / 32767.0;
                if (n < 0) n = 0; else if (n > 1) n = 1;
                int px = (int)Math.Round(n * (calibratedSize - 1));
                return (ushort)Math.Round(px * (65535.0 / (calibratedSize - 1)));
            }

            // Fallback: keep old behavior.
            return (ushort)Math.Min(65535, abs * 2);
        }

        private byte ComputeButtonsMask(IGunState state)
        {
            byte btns = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) btns = (byte)(btns | 1);
                if (map.Value == MouseButton.Right) btns = (byte)(btns | (1 << 1));
                if (map.Value == MouseButton.Middle) btns = (byte)(btns | (1 << 2));
            }

            return btns;
        }

    }
}
