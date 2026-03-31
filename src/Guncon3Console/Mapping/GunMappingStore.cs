using Guncon3Console.Common;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace Guncon3Console.Mapping
{
    internal static class GunMappingStore
    {
        public const string Player1FileName = "mapping.p1.json";
        public const string Player2FileName = "mapping.p2.json";

        public static GunMappingModel Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentNullException(nameof(path));

            if (!File.Exists(path))
                return new GunMappingModel { MappingPath = path };

            using (var fs = File.OpenRead(path))
            {
                var ser = new DataContractJsonSerializer(typeof(GunMappingModel), new DataContractJsonSerializerSettings
                {
                    UseSimpleDictionaryFormat = true
                });

                var obj = ser.ReadObject(fs) as GunMappingModel;
                var model = obj ?? new GunMappingModel();
                model.MappingPath = path;
                return model;
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

            switch (a.ToUpperInvariant())
            {
                case "LEFT":
                    return MouseButton.Left;
                case "RIGHT":
                    return MouseButton.Right;
                case "MIDDLE":
                    return MouseButton.Middle;
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
