using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GunconUSB
{
    public static class Constants
    {
        public const string GunDeviceInterfaceGuidString = "{A5DCBF10-6530-11D2-901F-00C04FB951ED}";

        private static Guid _gunDeviceInterfaceGuid = new Guid(GunDeviceInterfaceGuidString);
        public static Guid GunDeviceInterfaceGuid { get { return _gunDeviceInterfaceGuid; } }

        public const int VendorId = 2970;
        public const int ProductId = 2048;
    }
}
