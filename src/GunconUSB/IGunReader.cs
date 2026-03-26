using System;
using System.Collections.Generic;

namespace GunconUSB
{
    public interface IGunReader : IDisposable
    {
        void ReadInto(Dictionary<GunButton, bool> btnState, out short absX, out short absY, out bool indicator2);
    }
}
