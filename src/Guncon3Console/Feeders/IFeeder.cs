using Guncon3Console.GunStates;
using GunconUSB;
using System.Collections.Generic;

namespace Guncon3Console.Feeders
{
    internal interface IFeeder
    {
        Dictionary<GunButton, dynamic> Mapping { get; }
        string Name { get; }
        bool IsConnected { get; }
        void Connect();
        void Disconnect();
        void Feed(IGunState state);
    }
}
