using Guncon3Console.GunStates;
using GunconUSB;
using System.Collections.Generic;

namespace Guncon3Console.Feeders
{
    internal interface IFeeder
    {
        string Name { get; }
        void Log(string message);
        void ClearMapping();
        int MappingCount();
        void AddMapping(GunButton gunButton, dynamic mapping);
        dynamic GetMapping(GunButton gunButton);
        bool IsConnected { get; }
        void Connect();
        void Disconnect();
        void Feed(IGunState state);
    }
}
