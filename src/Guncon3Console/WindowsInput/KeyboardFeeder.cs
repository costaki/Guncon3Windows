using System;
using System.Collections.Generic;
using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
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
            if (mapping is global::WindowsInput.Native.VirtualKeyCode vkc)
            {
                _mapping[gunButton] = vkc;
                return;
            }

            if (mapping is int i)
            {
                _mapping[gunButton] = (global::WindowsInput.Native.VirtualKeyCode)i;
                return;
            }

            if (mapping is uint ui)
            {
                _mapping[gunButton] = (global::WindowsInput.Native.VirtualKeyCode)ui;
                return;
            }
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
