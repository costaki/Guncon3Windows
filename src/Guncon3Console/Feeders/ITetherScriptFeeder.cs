using Guncon3Console.TetherScript;
using Guncon3Console.Common;

namespace Guncon3Console.Feeders
{
    internal interface ITetherScriptFeeder : IFeeder
    {
        HIDController Hid { get; }
        void OnHidLog(object sender, LogArgs e);
        ushort VendorId { get; }
        ushort ProductId { get; }
    }
}
