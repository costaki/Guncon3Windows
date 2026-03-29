using Guncon3Console.GunStates;
using GunconUSB;

namespace Guncon3Console.Feeders
{
    internal interface IMouseFeeder : IFeeder
    {
        bool Force4by3 { get; set; }
        new void ClearMapping();
        new int MappingCount();
        new void AddMapping(GunButton gunButton, dynamic mapping);
    }
}
