using Guncon3Console.Common;
using System;

namespace Guncon3Console.Common.Hid
{
    internal interface IHidConnection : IDisposable
    {
        event EventHandler<LogArgs> OnLog;

        bool Connected { get; }

        void Connect();

        void Disconnect();
    }
}
