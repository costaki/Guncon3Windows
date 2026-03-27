using System;
using System.Collections.Generic;
using GunconUSB;
using WindowsInput;

namespace Guncon3Console.TetherScript
{
    internal static class WindowsInputAbsMouseFeeder
    {
        private static readonly InputSimulator Input = new InputSimulator();

        // Map: logical gun button (public enum in GunconUSB) -> WindowsInput mouse button
        public static readonly Dictionary<GunButton, WindowsInput.MouseButton> Mapping = new Dictionary<GunButton, WindowsInput.MouseButton>();

        public static bool Force4by3 = false;

        private static byte _prevButtons;

        public static void Connect()
        {
            _prevButtons = 0;
        }

        public static void Disconnect()
        {
        }

        internal static void Feed()
        {
            throw new NotSupportedException("Use Feed(IGunState) and pass a per-gun state.");
        }

        internal static void Feed(IGunState state)
        {
            ushort absX = 0;
            ushort absY = 0;

            if (state.IsInsideScreen)
            {
                var x = state.ABS_X;
                var y = state.ABS_Y;

                // 4:3 inside 16:9 (MAME)
                if (Force4by3)
                    x = (short)Helper.ConvertRange(4096, 28671, 0, 32767, x);

                // WindowsInput expects unsigned 0..65535 for absolute coords.
                absX = ConvertSigned32768ToUShort(x);
                absY = ConvertSigned32768ToUShort(y);

                // Send absolute movement every frame.
                Input.Mouse.MoveMouseTo(absX, absY);
            }

            var buttons = ComputeButtonsMask(state);

            SyncButtons(buttons);
            _prevButtons = buttons;
        }

        private static ushort ConvertSigned32768ToUShort(short v)
        {
            // Gun range is expected to be 0..32767 when in-screen, but keep behavior stable if negative.
            if (v <= 0)
                return 0;

            return (ushort)Math.Min(65535, v * 2);
        }

        private static byte ComputeButtonsMask(IGunState state)
        {
            byte btns = 0;
            foreach (var map in Mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == WindowsInput.MouseButton.LeftButton) btns = (byte)(btns | 1);
                if (map.Value == WindowsInput.MouseButton.RightButton) btns = (byte)(btns | (1 << 1));
                // WindowsInput fork in this repo doesn't expose middle button down/up on IMouseSimulator.
                // Keep mask bit reserved for compatibility, but no-op in SyncButtons.
            }

            return btns;
        }

        private static void SyncButtons(byte buttons)
        {
            SyncButton(buttons, 0, Input.Mouse.LeftButtonDown, Input.Mouse.LeftButtonUp);
            SyncButton(buttons, 1, Input.Mouse.RightButtonDown, Input.Mouse.RightButtonUp);
        }

        private static void SyncButton(byte buttons, int bit,
            Func<IMouseSimulator> down,
            Func<IMouseSimulator> up)
        {
            var mask = (byte)(1 << bit);
            var was = (_prevButtons & mask) != 0;
            var now = (buttons & mask) != 0;

            if (was == now)
                return;

            if (now)
                down();
            else
                up();
        }
    }
}
