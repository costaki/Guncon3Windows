using System;
using System.Collections.Generic;
using GunconUSB;

namespace Guncon3Console.GunStates
{
    internal sealed class GunState : IGunState
    {
        public Dictionary<GunButton, bool> BtnState { get; }

        public short ABS_X { get; set; }
        public short ABS_Y { get; set; }

        public byte ABS_RX { get; set; }
        public byte ABS_RY { get; set; }

        public bool INDICATOR2 { get; set; }

        public bool IsInsideScreen => !INDICATOR2;

        public GunState()
        {
            var values = Enum.GetValues(typeof(GunButton));
            BtnState = new Dictionary<GunButton, bool>(values.Length);
            foreach (GunButton b in values)
                BtnState[b] = false;
        }
    }
}
