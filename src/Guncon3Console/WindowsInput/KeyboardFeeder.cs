using System;
using System.Collections.Generic;
using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
using Guncon3Console.TetherScript;
using GunconUSB;

namespace Guncon3Console.WindowsInput
{
    internal sealed class KeyboardFeeder : BaseFeeder<ushort>, IKeyboardFeeder
    {
        private readonly HashSet<ushort> _prevDown = new HashSet<ushort>();
        private readonly HashSet<ushort> _nowDown = new HashSet<ushort>();
        private readonly NativeMethods.INPUT[] _singleInput = new NativeMethods.INPUT[1];

        public override string Name => "WindowsInput Keyboard";
        public override bool IsConnected => true;

        public override void Connect() { }
        public override void Disconnect() { }

        protected override ushort ConvertMapping(dynamic mapping)
        {
            if (mapping is HidKeyCode hidEnum)
            {
                var translated = TranslateHidKeyCode(hidEnum);
                if (translated.HasValue)
                    return translated.Value;
            }
            if (mapping is ushort u)
                return u;
            return base.ConvertMapping((ushort)mapping);
        }

        private static ushort? TranslateHidKeyCode(HidKeyCode key)
        {
            // Return Win32 virtual-key codes.
            switch (key)
            {
                // Letters
                case HidKeyCode.A: return 0x41;
                case HidKeyCode.B: return 0x42;
                case HidKeyCode.C: return 0x43;
                case HidKeyCode.D: return 0x44;
                case HidKeyCode.E: return 0x45;
                case HidKeyCode.F: return 0x46;
                case HidKeyCode.G: return 0x47;
                case HidKeyCode.H: return 0x48;
                case HidKeyCode.I: return 0x49;
                case HidKeyCode.J: return 0x4A;
                case HidKeyCode.K: return 0x4B;
                case HidKeyCode.L: return 0x4C;
                case HidKeyCode.M: return 0x4D;
                case HidKeyCode.N: return 0x4E;
                case HidKeyCode.O: return 0x4F;
                case HidKeyCode.P: return 0x50;
                case HidKeyCode.Q: return 0x51;
                case HidKeyCode.R: return 0x52;
                case HidKeyCode.S: return 0x53;
                case HidKeyCode.T: return 0x54;
                case HidKeyCode.U: return 0x55;
                case HidKeyCode.V: return 0x56;
                case HidKeyCode.W: return 0x57;
                case HidKeyCode.X: return 0x58;
                case HidKeyCode.Y: return 0x59;
                case HidKeyCode.Z: return 0x5A;

                // Digits
                case HidKeyCode.D0: return 0x30;
                case HidKeyCode.D1: return 0x31;
                case HidKeyCode.D2: return 0x32;
                case HidKeyCode.D3: return 0x33;
                case HidKeyCode.D4: return 0x34;
                case HidKeyCode.D5: return 0x35;
                case HidKeyCode.D6: return 0x36;
                case HidKeyCode.D7: return 0x37;
                case HidKeyCode.D8: return 0x38;
                case HidKeyCode.D9: return 0x39;

                // Common controls
                case HidKeyCode.Enter: return 0x0D;
                case HidKeyCode.Escape: return 0x1B;
                case HidKeyCode.Backspace: return 0x08;
                case HidKeyCode.Tab: return 0x09;
                case HidKeyCode.Space: return 0x20;

                // Arrows
                case HidKeyCode.UpArrow: return 0x26;
                case HidKeyCode.DownArrow: return 0x28;
                case HidKeyCode.LeftArrow: return 0x25;
                case HidKeyCode.RightArrow: return 0x27;

                // Function keys
                case HidKeyCode.F1: return 0x70;
                case HidKeyCode.F2: return 0x71;
                case HidKeyCode.F3: return 0x72;
                case HidKeyCode.F4: return 0x73;
                case HidKeyCode.F5: return 0x74;
                case HidKeyCode.F6: return 0x75;
                case HidKeyCode.F7: return 0x76;
                case HidKeyCode.F8: return 0x77;
                case HidKeyCode.F9: return 0x78;
                case HidKeyCode.F10: return 0x79;
                case HidKeyCode.F11: return 0x7A;
                case HidKeyCode.F12: return 0x7B;

                // Numpad
                case HidKeyCode.Keypad0: return 0x60;
                case HidKeyCode.Keypad1: return 0x61;
                case HidKeyCode.Keypad2: return 0x62;
                case HidKeyCode.Keypad3: return 0x63;
                case HidKeyCode.Keypad4: return 0x64;
                case HidKeyCode.Keypad5: return 0x65;
                case HidKeyCode.Keypad6: return 0x66;
                case HidKeyCode.Keypad7: return 0x67;
                case HidKeyCode.Keypad8: return 0x68;
                case HidKeyCode.Keypad9: return 0x69;
                case HidKeyCode.KeypadDecimal: return 0x6E;
                case HidKeyCode.KeypadAdd: return 0x6B;
                case HidKeyCode.KeypadSubtract: return 0x6D;
                case HidKeyCode.KeypadMultiply: return 0x6A;
                case HidKeyCode.KeypadDivide: return 0x6F;
                case HidKeyCode.NumLock: return 0x90;
            }

            return null;
        }

        public override void Feed(IGunState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (_mapping.Count == 0)
                return;

            _nowDown.Clear();

            foreach (var kv in _mapping)
            {
                if (!state.BtnState.TryGetValue(kv.Key, out var pressed) || !pressed)
                    continue;

                _nowDown.Add(kv.Value);
            }

            foreach (var key in _nowDown)
            {
                if (_prevDown.Contains(key))
                    continue;

                SendKey(key, true);
            }

            foreach (var key in _prevDown)
            {
                if (_nowDown.Contains(key))
                    continue;

                SendKey(key, false);
            }

            _prevDown.Clear();
            foreach (var key in _nowDown)
                _prevDown.Add(key);
        }

        private void SendKey(ushort vk, bool down)
        {
            ushort scan = (ushort)(NativeMethods.MapVirtualKey(vk, 0) & 0xFF);
            bool extended = IsExtendedKey(vk);
            uint flags = down ? 0u : NativeMethods.KEYEVENTF_KEYUP;
            if (extended) flags |= NativeMethods.KEYEVENTF_EXTENDEDKEY;

            _singleInput[0] = new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                Data = new NativeMethods.MOUSEKEYBDHARDWAREINPUT
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = vk,
                        wScan = scan,
                        dwFlags = flags,
                        time = 0,
                        dwExtraInfo = NativeMethods.GetMessageExtraInfo()
                    }
                }
            };

            _ = NativeMethods.SendInput(1, _singleInput, System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.INPUT)));
        }

        private static bool IsExtendedKey(ushort vk)
        {
            // Mirrors WindowsInput's extended key list.
            switch (vk)
            {
                case 0x12: // VK_MENU
                case 0xA5: // VK_RMENU
                case 0x11: // VK_CONTROL
                case 0xA3: // VK_RCONTROL
                case 0x2D: // VK_INSERT
                case 0x2E: // VK_DELETE
                case 0x24: // VK_HOME
                case 0x23: // VK_END
                case 0x21: // VK_PRIOR
                case 0x22: // VK_NEXT
                case 0x27: // VK_RIGHT
                case 0x26: // VK_UP
                case 0x25: // VK_LEFT
                case 0x28: // VK_DOWN
                case 0x90: // VK_NUMLOCK
                case 0x03: // VK_CANCEL
                case 0x2C: // VK_SNAPSHOT
                case 0x6F: // VK_DIVIDE
                    return true;
            }

            return false;
        }

    }
}
