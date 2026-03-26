using MadWizard.WinUSBNet;
using System;
using System.Collections.Generic;

namespace GunconUSB
{
    public sealed class GunconDeviceReader : IGunReader
    {
        private readonly GunconDevice _device;

        public GunconDeviceReader(USBDeviceInfo devInfo)
        {
            if (devInfo == null) throw new ArgumentNullException(nameof(devInfo));
            _device = new GunconDevice(devInfo);
        }

        public void ReadInto(Dictionary<GunButton, bool> btnState, out short absX, out short absY, out bool indicator2)
        {
            _device.ReadInto(btnState, out absX, out absY, out indicator2);
        }

        public void Dispose()
        {
            _device.Dispose();
        }
    }
}
