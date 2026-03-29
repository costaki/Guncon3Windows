using System;
using System.Collections.Generic;
using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
using Guncon3Console.TetherScript;
using GunconUSB;

namespace Guncon3Console.WindowsInput
{
    internal sealed class KeyboardFeeder : IKeyboardFeeder
    {
        private readonly global::WindowsInput.InputSimulator _input = new global::WindowsInput.InputSimulator();

        // Map: logical gun button (public enum in GunconUSB) -> WindowsInput VirtualKeyCode
        private readonly Dictionary<GunButton, global::WindowsInput.Native.VirtualKeyCode> _mapping =
            new Dictionary<GunButton, global::WindowsInput.Native.VirtualKeyCode>();

        private readonly HashSet<global::WindowsInput.Native.VirtualKeyCode> _prevDown =
            new HashSet<global::WindowsInput.Native.VirtualKeyCode>();

        public string Name => "WindowsInput Keyboard";

        public bool IsConnected => true;

        public void Log(string message) => Console.WriteLine("[" + Name + "]: " + message);

        public void ClearMapping() => _mapping.Clear();

        public int MappingCount() => _mapping.Count;

        public void AddMapping(GunButton gunButton, dynamic mapping)
        {
            if (mapping is HidKeyCode hidEnum)
            {
                var translated = TranslateHidKeyCode(hidEnum);
                if (translated.HasValue)
                    _mapping[gunButton] = translated.Value;
                return;
            }
        }

        private static global::WindowsInput.Native.VirtualKeyCode? TranslateHidKeyCode(HidKeyCode key)
        {
            // Letters
            if (key >= HidKeyCode.A && key <= HidKeyCode.Z)
            {
                var ch = (char)('A' + ((byte)key - (byte)HidKeyCode.A));
                if (Enum.TryParse<global::WindowsInput.Native.VirtualKeyCode>("VK_" + ch, out var vkc))
                    return vkc;
                return null;
            }

            // Digits (top row)
            switch (key)
            {
                case HidKeyCode.D0: return global::WindowsInput.Native.VirtualKeyCode.VK_0;
                case HidKeyCode.D1: return global::WindowsInput.Native.VirtualKeyCode.VK_1;
                case HidKeyCode.D2: return global::WindowsInput.Native.VirtualKeyCode.VK_2;
                case HidKeyCode.D3: return global::WindowsInput.Native.VirtualKeyCode.VK_3;
                case HidKeyCode.D4: return global::WindowsInput.Native.VirtualKeyCode.VK_4;
                case HidKeyCode.D5: return global::WindowsInput.Native.VirtualKeyCode.VK_5;
                case HidKeyCode.D6: return global::WindowsInput.Native.VirtualKeyCode.VK_6;
                case HidKeyCode.D7: return global::WindowsInput.Native.VirtualKeyCode.VK_7;
                case HidKeyCode.D8: return global::WindowsInput.Native.VirtualKeyCode.VK_8;
                case HidKeyCode.D9: return global::WindowsInput.Native.VirtualKeyCode.VK_9;

                case HidKeyCode.Enter: return global::WindowsInput.Native.VirtualKeyCode.RETURN;
                case HidKeyCode.Escape: return global::WindowsInput.Native.VirtualKeyCode.ESCAPE;
                case HidKeyCode.Backspace: return global::WindowsInput.Native.VirtualKeyCode.BACK;
                case HidKeyCode.Tab: return global::WindowsInput.Native.VirtualKeyCode.TAB;
                case HidKeyCode.Space: return global::WindowsInput.Native.VirtualKeyCode.SPACE;

                case HidKeyCode.Minus: return global::WindowsInput.Native.VirtualKeyCode.OEM_MINUS;
                case HidKeyCode.Equals: return global::WindowsInput.Native.VirtualKeyCode.RETURN;
                case HidKeyCode.LeftBracket: return global::WindowsInput.Native.VirtualKeyCode.OEM_4;
                case HidKeyCode.RightBracket: return global::WindowsInput.Native.VirtualKeyCode.OEM_6;
                case HidKeyCode.Backslash: return global::WindowsInput.Native.VirtualKeyCode.OEM_5;
                case HidKeyCode.Semicolon: return global::WindowsInput.Native.VirtualKeyCode.OEM_1;
                case HidKeyCode.Apostrophe: return global::WindowsInput.Native.VirtualKeyCode.OEM_7;
                case HidKeyCode.Grave: return global::WindowsInput.Native.VirtualKeyCode.OEM_3;
                case HidKeyCode.Comma: return global::WindowsInput.Native.VirtualKeyCode.OEM_COMMA;
                case HidKeyCode.Period: return global::WindowsInput.Native.VirtualKeyCode.OEM_PERIOD;
                case HidKeyCode.Slash: return global::WindowsInput.Native.VirtualKeyCode.OEM_2;

                case HidKeyCode.CapsLock: return global::WindowsInput.Native.VirtualKeyCode.CAPITAL;

                case HidKeyCode.PrintScreen: return global::WindowsInput.Native.VirtualKeyCode.SNAPSHOT;
                case HidKeyCode.ScrollLock: return global::WindowsInput.Native.VirtualKeyCode.SCROLL;
                case HidKeyCode.Pause: return global::WindowsInput.Native.VirtualKeyCode.PAUSE;
                case HidKeyCode.Insert: return global::WindowsInput.Native.VirtualKeyCode.INSERT;
                case HidKeyCode.Home: return global::WindowsInput.Native.VirtualKeyCode.HOME;
                case HidKeyCode.PageUp: return global::WindowsInput.Native.VirtualKeyCode.PRIOR;
                case HidKeyCode.Delete: return global::WindowsInput.Native.VirtualKeyCode.DELETE;
                case HidKeyCode.End: return global::WindowsInput.Native.VirtualKeyCode.END;
                case HidKeyCode.PageDown: return global::WindowsInput.Native.VirtualKeyCode.NEXT;

                case HidKeyCode.RightArrow: return global::WindowsInput.Native.VirtualKeyCode.RIGHT;
                case HidKeyCode.LeftArrow: return global::WindowsInput.Native.VirtualKeyCode.LEFT;
                case HidKeyCode.DownArrow: return global::WindowsInput.Native.VirtualKeyCode.DOWN;
                case HidKeyCode.UpArrow: return global::WindowsInput.Native.VirtualKeyCode.UP;
            }

            // Function keys
            if (key >= HidKeyCode.F1 && key <= HidKeyCode.F24)
            {
                var idx = (byte)key - (byte)HidKeyCode.F1 + 1;
                if (Enum.TryParse<global::WindowsInput.Native.VirtualKeyCode>("F" + idx, out var vf))
                    return vf;
            }

            // Numpad
            switch (key)
            {
                case HidKeyCode.NumLock: return global::WindowsInput.Native.VirtualKeyCode.NUMLOCK;

                case HidKeyCode.KeypadDivide: return global::WindowsInput.Native.VirtualKeyCode.DIVIDE;
                case HidKeyCode.KeypadMultiply: return global::WindowsInput.Native.VirtualKeyCode.MULTIPLY;
                case HidKeyCode.KeypadSubtract: return global::WindowsInput.Native.VirtualKeyCode.SUBTRACT;
                case HidKeyCode.KeypadAdd: return global::WindowsInput.Native.VirtualKeyCode.ADD;
                case HidKeyCode.KeypadEnter: return global::WindowsInput.Native.VirtualKeyCode.RETURN;
                case HidKeyCode.KeypadDecimal: return global::WindowsInput.Native.VirtualKeyCode.DECIMAL;

                case HidKeyCode.Keypad0: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD0;
                case HidKeyCode.Keypad1: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD1;
                case HidKeyCode.Keypad2: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD2;
                case HidKeyCode.Keypad3: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD3;
                case HidKeyCode.Keypad4: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD4;
                case HidKeyCode.Keypad5: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD5;
                case HidKeyCode.Keypad6: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD6;
                case HidKeyCode.Keypad7: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD7;
                case HidKeyCode.Keypad8: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD8;
                case HidKeyCode.Keypad9: return global::WindowsInput.Native.VirtualKeyCode.NUMPAD9;
            }

            return null;
        }

        public dynamic GetMapping(GunButton gunButton)
        {
            if (_mapping.TryGetValue(gunButton, out var v))
                return v;
            return null;
        }

        public void Connect()
        {
            _prevDown.Clear();
        }

        public void Disconnect()
        {
            try
            {
                foreach (var key in _prevDown)
                    _input.Keyboard.KeyUp(key);
            }
            catch { }
            finally
            {
                _prevDown.Clear();
            }
        }

        public void Feed(IGunState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            if (_mapping.Count == 0)
                return;

            var nowDown = new HashSet<global::WindowsInput.Native.VirtualKeyCode>();

            foreach (var kv in _mapping)
            {
                if (!state.BtnState.TryGetValue(kv.Key, out var pressed) || !pressed)
                    continue;

                nowDown.Add(kv.Value);
            }

            foreach (var key in nowDown)
            {
                if (_prevDown.Contains(key))
                    continue;

                _input.Keyboard.KeyDown(key);
            }

            foreach (var key in _prevDown)
            {
                if (nowDown.Contains(key))
                    continue;

                _input.Keyboard.KeyUp(key);
            }

            _prevDown.Clear();
            foreach (var key in nowDown)
                _prevDown.Add(key);
        }
    }
}
