using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Common;

namespace Guncon3Console.Feeders
{
    internal static class MouseFeederHelper
    {
        // Shared 4:3 normalization for absolute mouse feeders
        public static void Normalize43(ref short x, ref short y, bool force43)
        {
            if (force43)
            {
                if (!Helper.IsInsideCentered4By3(x))
                {
                    x = (short)Helper.GunAxisMax;
                    y = (short)Helper.GunAxisMax;
                }
                else
                {
                    x = (short)Helper.ConvertRange4By3(x);
                }
            }
        }

        // Clamp to 15-bit unsigned (for vMulti)
        public static ushort ClampToUShort15(short v)
        {
            if (v < 0) return 0;
            if (v > 0x7FFF) return 0x7FFF;
            return (ushort)v;
        }

        // Compute mouse button mask from mapping and state
        public static byte ComputeButtonsMask(System.Collections.Generic.Dictionary<GunButton, MouseButton> mapping, IGunState state)
        {
            byte mask = 0;
            foreach (var map in mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) mask = (byte)(mask | 1);
                if (map.Value == MouseButton.Right) mask = (byte)(mask | (1 << 1));
                if (map.Value == MouseButton.Middle) mask = (byte)(mask | (1 << 2));
            }
            return mask;
        }
    }
}
