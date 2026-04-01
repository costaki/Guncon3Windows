using Guncon3Console.Common;
using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
using GunconUSB;
using System;
using static Guncon3Console.Feeders.BaseTetherScriptMouseFeeder;

namespace Guncon3Console.vMulti
{
    /// <summary>
    /// Feeds the stock vmulti absolute mouse endpoint.
    ///
    /// This mirrors vmulti's native client logic:
    /// - enumerate HID devices
    /// - match vmulti VID/PID
    /// - match HID caps UsagePage=0xFF00, Usage=0x0001 (control device)
    /// - write a 0x41-byte control report:
    ///     [VMultiControlReportHeader][VMultiMouseReport][padding...]
    /// </summary>
    internal sealed class AbsoluteMouseFeeder : BaseVMultiMouseFeeder, IMouseFeeder, IHidFeeder
    {
        public override string Name => "vMulti AbsMouse";
        public override ushort ProductId => (ushort)0xBA1C;

        public override void Feed(IGunState state)
        {
            short absX = 0;
            short absY = 0;

            if (state.IsInsideScreen)
            {
                absX = state.ABS_X;
                absY = state.ABS_Y;
                Feeders.MouseFeederHelper.Normalize43(ref absX, ref absY, Force4by3);
            }

            ushort x = Feeders.MouseFeederHelper.ClampToUShort15(absX);
            ushort y = Feeders.MouseFeederHelper.ClampToUShort15(absY);

            ComputeButtonsMask(state);

            _hid.SendAbsoluteMouse(_buttons, x, y, 0);
        }
    }
}