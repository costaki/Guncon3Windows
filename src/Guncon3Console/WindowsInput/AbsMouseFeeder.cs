using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;

namespace Guncon3Console.WindowsInput
{
    public enum MouseButton
    {
        LeftButton,
        MiddleButton,
        RightButton
    }

    internal sealed class AbsMouseFeeder : IMouseFeeder, IFeeder
    {

        [DllImport("user32.dll", SetLastError = false)]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        // Map: logical gun button (public enum in GunconUSB) -> mouse button
        private readonly Dictionary<GunButton, MouseButton> _mapping = new Dictionary<GunButton, MouseButton>();

        public bool Force4by3 { get; set; } = false;

        private byte _prevButtons;
        private ushort _lastAbsX;
        private ushort _lastAbsY;
        private bool _hasLastAbs;

        public AbsMouseFeeder() { }

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
            _hasLastAbs = false;
            _lastAbsX = 0;
            _lastAbsY = 0;
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
                    x = (short)Helper.ConvertRange(4096, 28671, 0, 32767, x);

                // mouse_event w/ ABSOLUTE expects normalized 0..65535 when used with MOUSEEVENTF_ABSOLUTE.
                absX = ConvertSigned32768ToUShort(x);
                absY = ConvertSigned32768ToUShort(y);

                flags |= (MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);

                _lastAbsX = absX;
                _lastAbsY = absY;
                _hasLastAbs = true;
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
                    flags |= (MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);
                    absX = 65535;
                    absY = 65535;
                }

                mouse_event(flags, absX, absY, 0, UIntPtr.Zero);
            }

            _prevButtons = buttons;
        }

        private uint ComputeButtonTransitionFlags(byte buttons)
        {
            uint flags = 0;

            flags |= ComputeButtonTransitionFlag(buttons, 0, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
            flags |= ComputeButtonTransitionFlag(buttons, 1, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
            flags |= ComputeButtonTransitionFlag(buttons, 2, MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP);

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

        private static ushort ConvertSigned32768ToUShort(short v)
        {
            // Gun range is expected to be 0..32767 when in-screen, but keep behavior stable if negative.
            if (v <= 0)
                return 0;

            return (ushort)Math.Min(65535, v * 2);
        }

        private byte ComputeButtonsMask(IGunState state)
        {
            byte btns = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.LeftButton) btns = (byte)(btns | 1);
                if (map.Value == MouseButton.RightButton) btns = (byte)(btns | (1 << 1));
                if (map.Value == MouseButton.MiddleButton) btns = (byte)(btns | (1 << 2));
            }

            return btns;
        }

    }
}
