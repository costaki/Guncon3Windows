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
                var translated = Feeders.KeyboardMappingHelper.TranslateHidKeyCode(hidEnum);
                if (translated.HasValue)
                    return translated.Value;
            }
            if (mapping is ushort u)
                return u;
            return base.ConvertMapping((ushort)mapping);
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
