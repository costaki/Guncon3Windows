using Guncon3Console.GunStates;

namespace Guncon3Console.Feeders
{
    internal interface IMouseFeeder : IFeeder
    {
        bool Force4by3 { get; set; }
    }
}
