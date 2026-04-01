using Guncon3Console.Common;
using Guncon3Console.Common.Hid;
using Guncon3Console.TetherScript;

namespace Guncon3Console.Feeders
{
    internal interface IHidFeeder : IFeeder
    {
        IHidConnection Hid { get; }
        void OnHidLog(object sender, LogArgs e);
        ushort VendorId { get; }
        ushort ProductId { get; }
    }
}
