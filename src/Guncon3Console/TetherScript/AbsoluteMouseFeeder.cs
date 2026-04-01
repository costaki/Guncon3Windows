using System;
using System.Runtime.InteropServices;
using GunconUSB;
using Guncon3Console.GunStates;
using Guncon3Console.Feeders;
using Guncon3Console.Common;
using System.Threading;

namespace Guncon3Console.TetherScript
{
    // Refactored to use BaseHidFeeder
    internal sealed class AbsoluteMouseFeeder : BaseTetherScriptMouseFeeder, IMouseFeeder, IHidFeeder
    {
        public override string Name => "TetherScript AbsMouse";
        public override ushort ProductId => (ushort)DriversConst.TTC_PRODUCTID_MOUSEABS;

        public void Send_Data_To_MouseAbs(ushort x, ushort y)
        {
            var data = new SetFeatureMouseAbs
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = _buttons,
                X = x,
                Y = y
            };

            byte[] buf = TetherScriptMarshal.StructToBytes(data);
            SendHidReport(buf);
        }

        public override void Feed(IGunState state)
        {
            short absX = 0;
            short absY = 0;

            if (state.IsInsideScreen)
            {
                absX = state.ABS_X;
                absY = state.ABS_Y;

                // 4:3 inside 16:9 (MAME)
                if (Force4by3)
                {
                    if (!Helper.IsInsideCentered4By3(absX))
                    {
                        // Treat outside the 4:3 region as out-of-bounds.
                        absX = (short)Helper.GunAxisMax;
                        absY = (short)Helper.GunAxisMax;
                    }
                    else
                    {
                        absX = (short)Helper.ConvertRange4By3(absX);
                    }
                }
            }

            ComputeButtonsMask(state);
            Send_Data_To_MouseAbs((ushort)absX, (ushort)absY);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureMouseAbs
    {
        public byte ReportID;
        public byte CommandCode;
        public byte Buttons;
        public ushort X;
        public ushort Y;
    }
}
