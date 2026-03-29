using GunconUSB;

namespace Guncon3Console.Feeders
{
    internal interface IKeyboardFeeder : IFeeder
    {
        new void ClearMapping();
        new int MappingCount();
        new void AddMapping(GunButton gunButton, dynamic mapping);
    }
}
