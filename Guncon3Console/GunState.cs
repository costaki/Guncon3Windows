using System;
using System.Collections.Generic;

namespace Guncon3Console
{
    public static class GunState
    {
        public static readonly Dictionary<GunButton, bool> BtnState;

        static GunState()
        {
            var values = Enum.GetValues(typeof(GunButton));
            BtnState = new Dictionary<GunButton, bool>(values.Length);
            foreach (GunButton item in values)
                BtnState[item] = false;
        }

        // Valores que ya usaba el proyecto
        public static long ABS_RY { get; set; }
        public static long ABS_RX { get; set; }
        public static long ABS_HAT0Y { get; set; }
        public static long ABS_HAT0X { get; set; }
        public static short Z { get; set; }
        public static short ABS_Y { get; set; }
        public static short ABS_X { get; set; }

        // Indicadores existentes
        public static bool INDICATOR1 { get; set; }
        public static bool INDICATOR2 { get; set; }
        public static bool IsInsideScreen => !INDICATOR2;

        // ============================
        // NUEVO: alias compatibles
        // ============================
        // Para calibración queremos leer el "RAW" del dispositivo. En este driver,
        // lo más cercano son ABS_X / ABS_Y antes de aplicar la transformación,
        // así que exponemos RAW_X/RAW_Y como alias a esos campos.
        public static int RAW_X
        {
            get => ABS_X;
            set => ABS_X = (short)value;
        }

        public static int RAW_Y
        {
            get => ABS_Y;
            set => ABS_Y = (short)value;
        }

        // Mapear el gatillo a la tabla de botones
        public static bool BTN_TRIGGER
        {
            get => BtnState.TryGetValue(GunButton.Trigger, out var v) && v;
            set => BtnState[GunButton.Trigger] = value;
        }

        // Alias adicionales por si algún código espera estos nombres
        public static bool Trigger
        {
            get => BTN_TRIGGER;
            set => BTN_TRIGGER = value;
        }

        public static int PointerX
        {
            get => ABS_X;
            set => ABS_X = (short)value;
        }

        public static int PointerY
        {
            get => ABS_Y;
            set => ABS_Y = (short)value;
        }
    }
}
