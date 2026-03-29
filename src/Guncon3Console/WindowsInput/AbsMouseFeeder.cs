using System;
using System.Collections.Generic;
using GunconUSB;
using WindowsInput;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;

namespace Guncon3Console.WindowsInput
{
    internal sealed class AbsMouseFeeder : IMouseFeeder
    {
        private readonly InputSimulator _input = new InputSimulator();

        // Map: logical gun button (public enum in GunconUSB) -> WindowsInput mouse button
        private readonly Dictionary<GunButton, global::WindowsInput.MouseButton> _mapping = new Dictionary<GunButton, MouseButton>();

        public bool Force4by3 { get; set; } = false;

        private byte _prevButtons;
        public AbsMouseFeeder() { }

        public string Name => "WindowsInput AbsMouse";

        public bool IsConnected => true;

        public void Log(string message) => Console.WriteLine("[" + Name + "]: " + message);

        public void ClearMapping() => _mapping.Clear();

        public int MappingCount() => _mapping.Count;

        public void AddMapping(GunButton gunButton, dynamic mapping)
        {
            if (mapping is global::WindowsInput.MouseButton btn)
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
                _input.Mouse.MoveMouseTo(absX, absY);
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

        private byte ComputeButtonsMask(IGunState state)
        {
            byte btns = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == global::WindowsInput.MouseButton.LeftButton) btns = (byte)(btns | 1);
                if (map.Value == global::WindowsInput.MouseButton.RightButton) btns = (byte)(btns | (1 << 1));
                // WindowsInput fork in this repo doesn't expose middle button down/up on IMouseSimulator.
                // Keep mask bit reserved for compatibility, but no-op in SyncButtons.
            }

            return btns;
        }

        private void SyncButtons(byte buttons)
        {
            SyncButton(buttons, 0, _input.Mouse.LeftButtonDown, _input.Mouse.LeftButtonUp);
            SyncButton(buttons, 1, _input.Mouse.RightButtonDown, _input.Mouse.RightButtonUp);
        }

        private void SyncButton(byte buttons, int bit,
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
