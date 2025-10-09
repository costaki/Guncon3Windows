using System;
using System.Collections.Generic;

namespace GunconUSB
{
    public static class GunState
    {
        // Botonera lógica
        public static readonly Dictionary<GunButton, bool> BtnState;

        static GunState()
        {
            BtnState = new Dictionary<GunButton, bool>();
            foreach (GunButton b in Enum.GetValues(typeof(GunButton)))
                BtnState[b] = false;
        }

        // Valores RAW de la gun
        public static long ABS_RY { get; set; }
        public static long ABS_RX { get; set; }
        public static long ABS_HAT0Y { get; set; }
        public static long ABS_HAT0X { get; set; }
        public static short Z { get; set; }
        public static short ABS_Y { get; set; }
        public static short ABS_X { get; set; }

        public static bool INDICATOR1 { get; set; }
        public static bool INDICATOR2 { get; set; }

        // Compatibilidad con el calibrador rectangular
        public static double RAW_X { get; set; }
        public static double RAW_Y { get; set; }
        public static bool BTN_TRIGGER { get; set; }

        public static bool IsInsideScreen => !INDICATOR2;
    }
}
