using GunconUSB;
using System.Collections.Generic;

namespace Guncon3Console.GunStates
{

    internal interface IGunState
    {
        Dictionary<GunButton, bool> BtnState { get; }
        short ABS_X { get; }
        short ABS_Y { get; }
        bool IsInsideScreen { get; }

        int ScreenW { get; }
        int ScreenH { get; }
    }
}
