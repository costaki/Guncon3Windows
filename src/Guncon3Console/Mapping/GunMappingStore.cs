using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Guncon3Console.Mapping
{
    internal static class GunMappingStore
    {
        public static GunMappingModel Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            if (!File.Exists(path))
                return new GunMappingModel();

            using (var fs = File.OpenRead(path))
            {
                var ser = new DataContractJsonSerializer(typeof(GunMappingModel), new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });

                var obj = ser.ReadObject(fs) as GunMappingModel;
                return obj ?? new GunMappingModel();
            }
        }

        public static void Save(string path, GunMappingModel model)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var fs = File.Create(path))
            {
                var ser = new DataContractJsonSerializer(typeof(GunMappingModel), new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });

                ser.WriteObject(fs, model);
            }
        }

        public static void ApplyToFeeders(GunMappingModel model, Guncon3Console.Feeders.IMouseFeeder mouseFeeder, Guncon3Console.Feeders.IKeyboardFeeder keyboardFeeder)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            mouseFeeder?.ClearMapping();
            keyboardFeeder?.ClearMapping();

            if (mouseFeeder != null)
            {
                foreach (var kv in model.Mouse)
                {
                    var btn = ToMouseButton(mouseFeeder, kv.Value);
                    if (btn != null)
                        mouseFeeder.AddMapping(kv.Key, btn);
                }
            }

            if (keyboardFeeder != null)
            {
                foreach (var kv in model.Keyboard)
                {
                    var key = ToKeyboardKeyCode(keyboardFeeder, kv.Value);
                    if (key != null)
                        keyboardFeeder.AddMapping(kv.Key, key);
                }
            }
        }

        private static dynamic ToMouseButton(Guncon3Console.Feeders.IMouseFeeder feeder, string action)
        {
            if (string.IsNullOrWhiteSpace(action))
                return null;

            var a = action.Trim();

            // We have 2 distinct MouseButton enums (TetherScript vs WindowsInput). Feeders already accept dynamic.
            // Detect by runtime type name to avoid referencing both enums here.
            var typeName = feeder.GetType().FullName ?? string.Empty;

            bool isWindowsInput = typeName.IndexOf("Guncon3Console.WindowsInput", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isWindowsInput)
            {
                switch (a.ToUpperInvariant())
                {
                    case "LEFT":
                        return global::WindowsInput.MouseButton.LeftButton;
                    case "RIGHT":
                        return global::WindowsInput.MouseButton.RightButton;
                    case "MIDDLE":
                        return global::WindowsInput.MouseButton.MiddleButton;
                    default: return null;
                }
            }

            switch (a.ToUpperInvariant())
            {
                case "LEFT":
                    return Guncon3Console.TetherScript.MouseButton.Left;
                case "RIGHT":
                    return Guncon3Console.TetherScript.MouseButton.Right;
                case "MIDDLE":
                    return Guncon3Console.TetherScript.MouseButton.Middle;
                default: return null;
            }
        }

        private static dynamic ToKeyboardKeyCode(Guncon3Console.Feeders.IKeyboardFeeder feeder, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var s = raw.Trim();

            if (Enum.TryParse<Guncon3Console.TetherScript.HidKeyCode>(s, ignoreCase: true, out var hk))
                return hk;

            //if (int.TryParse(s, out var hid))
            //    return hid;

            return null;
        }
    }
}
