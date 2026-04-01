using Guncon3Console.Common;
using System;

namespace Guncon3Console.Common.Hid
{
    internal interface IHidConnection : IDisposable
    {
        event EventHandler<LogArgs> OnLog;

        bool Connected { get; }

        ushort ProductID { get; set; }

        ushort VendorID { get; set; }

        void Connect();

        void Disconnect();
    }
}
