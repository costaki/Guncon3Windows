using MadWizard.WinUSBNet;
using System;
using System.Collections.Generic;

namespace GunconUSB
{
    public sealed class GunconDevice : IDisposable
    {
        private readonly USBDevice _device;

        private const int ExpectedReadLength = 15;

        private static readonly byte[] key = new byte[] { 0x01, 0x12, 0x6f, 0x32, 0x24, 0x60, 0x17, 0x21 };

        private static readonly byte[] KEY_TABLE = new byte[]{
            0x75, 0xC3, 0x10, 0x31, 0xB5, 0xD3, 0x69, 0x84, 0x89, 0xBA, 0xD6, 0x89, 0xBD, 0x70, 0x19, 0x8E, 0x58, 0xA8,
            0x3D, 0x9B, 0x5D, 0xF0, 0x49, 0xE8, 0xAD, 0x9D, 0x7A, 0x0D, 0x7E, 0x24, 0xDA, 0xFC, 0x0D, 0x14, 0xC5, 0x23,
            0x91, 0x11, 0xF5, 0xC0, 0x4B, 0xCD, 0x44, 0x1C, 0xC5, 0x21, 0xDF, 0x61, 0x54, 0xED, 0xA2, 0x81, 0xB7, 0xE5,
            0x74, 0x94, 0xB0, 0x47, 0xEE, 0xF1, 0xA5, 0xBB, 0x21, 0xC8, 0x91, 0xFD, 0x4C, 0x8B, 0x20, 0xC1, 0x7C, 0x09, 0x58,
            0x14, 0xF6, 0x00, 0x52, 0x55, 0xBF, 0x41, 0x75, 0xC0, 0x13, 0x30, 0xB5, 0xD0, 0x69, 0x85, 0x89, 0xBB, 0xD6, 0x88,
            0xBC, 0x73, 0x18, 0x8D, 0x58, 0xAB, 0x3D, 0x98, 0x5C, 0xF2, 0x48, 0xE9, 0xAC, 0x9F, 0x7A, 0x0C, 0x7C, 0x25, 0xD8,
            0xFF, 0xDC, 0x7D, 0x08, 0xDB, 0xBC, 0x18, 0x8C, 0x1D, 0xD6, 0x3C, 0x35, 0xE1, 0x2C, 0x14, 0x8E, 0x64, 0x83, 0x39,
            0xB0, 0xE4, 0x4E, 0xF7, 0x51, 0x7B, 0xA8, 0x13, 0xAC, 0xE9, 0x43, 0xC0, 0x08, 0x25, 0x0E, 0x15, 0xC4, 0x20, 0x93,
            0x13, 0xF5, 0xC3, 0x48, 0xCC, 0x47, 0x1C, 0xC5, 0x20, 0xDE, 0x60, 0x55, 0xEE, 0xA0, 0x40, 0xB4, 0xE7, 0x74,
            0x95, 0xB0, 0x46, 0xEC, 0xF0, 0xA5, 0xB8, 0x23, 0xC8, 0x04, 0x06, 0xFC, 0x28, 0xCB, 0xF8, 0x17, 0x2C, 0x25, 0x1C,
            0xCB, 0x18, 0xE3, 0x6C, 0x80, 0x85, 0xDD, 0x7E, 0x09, 0xD9, 0xBC, 0x19, 0x8F, 0x1D, 0xD4, 0x3D, 0x37, 0xE1, 0x2F,
            0x15, 0x8D, 0x64, 0x06, 0x04, 0xFD, 0x29, 0xCF, 0xFA, 0x14, 0x2E, 0x25, 0x1F, 0xC9, 0x18, 0xE3, 0x6D, 0x81, 0x84,
            0x80, 0x3B, 0xB1, 0xE5, 0x4D, 0xF7, 0x51, 0x78, 0xA9, 0x13, 0xAD, 0xE9, 0x80, 0xC1, 0x0B, 0x25, 0x93, 0xFC,
            0x4D, 0x89, 0x23, 0xC2, 0x7C, 0x0B, 0x59, 0x15, 0xF6, 0x01, 0x50, 0x55, 0xBF, 0x81, 0x75, 0xC3, 0x10, 0x31, 0xB5,
            0xD3, 0x69, 0x84, 0x89, 0xBA, 0xD6, 0x89, 0xBD, 0x70, 0x19, 0x8E, 0x58, 0xA8, 0x3D, 0x9B, 0x5D, 0xF0, 0x49,
            0xE8, 0xAD, 0x9D, 0x7A, 0x0D, 0x7E, 0x24, 0xDA, 0xFC, 0x0D, 0x14, 0xC5, 0x23, 0x91, 0x11, 0xF5, 0xC0, 0x4B, 0xCD,
            0x44, 0x1C, 0xC5, 0x21, 0xDF, 0x61, 0x54, 0xED, 0xA2, 0x81, 0xB7, 0xE5, 0x74, 0x94, 0xB0, 0x47, 0xEE, 0xF1,
            0xA5, 0xBB, 0x21, 0xC8
        };

        public GunconDevice(USBDeviceInfo devInfo)
        {
            if (devInfo == null) throw new ArgumentNullException(nameof(devInfo));
            _device = new USBDevice(devInfo);
        }

        public void Dispose()
        {
            try { _device?.Dispose(); } catch { }
        }

        public void ReadInto(Dictionary<GunButton, bool> btnState, out short absX, out short absY, out bool indicator2)
        {
            if (btnState == null) throw new ArgumentNullException(nameof(btnState));

            // Ensure all keys exist so callers can pass a fresh Dictionary per device.
            foreach (GunButton b in Enum.GetValues(typeof(GunButton)))
                if (!btnState.ContainsKey(b)) btnState[b] = false;

            var iface = _device.Interfaces[0];
            iface.OutPipe.Write(key);

            byte[] data = new byte[ExpectedReadLength];
            int n = iface.InPipe.Read(data);
            if (n != ExpectedReadLength) throw new Exception("Invalid Guncon read length");

            var decoded = Decode(data);
            if (decoded == null || decoded.Count < 13)
                throw new Exception("Guncon decode error");

            btnState[GunButton.Trigger] = (decoded[11] & 0x20) != 0;
            btnState[GunButton.A1] = (decoded[12] & 0x04) != 0;
            btnState[GunButton.A2] = (decoded[12] & 0x02) != 0;
            btnState[GunButton.B1] = (decoded[11] & 0x04) != 0;
            btnState[GunButton.B2] = (decoded[11] & 0x02) != 0;
            btnState[GunButton.C1] = (decoded[11] & 0x80) != 0;
            btnState[GunButton.C2] = (decoded[12] & 0x08) != 0;
            btnState[GunButton.AClick] = (decoded[10] & 0x80) != 0;
            btnState[GunButton.BClick] = (decoded[10] & 0x40) != 0;

            absY = (short)(decoded[6] * 256 + decoded[7]);
            absX = (short)(decoded[8] * 256 + decoded[9]);

            indicator2 = (decoded[11] & 0x08) != 0;

            const int DEAD = 20;
            int lx = decoded[3];
            int ly = decoded[2];

            btnState[GunButton.LLeft] = lx < (128 - DEAD);
            btnState[GunButton.LRight] = lx > (128 + DEAD);
            btnState[GunButton.LUp] = ly < (128 - DEAD);
            btnState[GunButton.LDown] = ly > (128 + DEAD);
        }

        public bool TryReadDecoded(out byte[] decoded)
        {
            decoded = null;
            try
            {
                var iface = _device.Interfaces[0];
                iface.OutPipe.Write(key);

                byte[] data = new byte[ExpectedReadLength];
                int n = iface.InPipe.Read(data);
                if (n != ExpectedReadLength) return false;

                var list = Decode(data);
                if (list == null || list.Count < 13) return false;

                decoded = list.ToArray();
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static List<byte> Decode(byte[] data2)
        {
            var ret = new List<byte>();
            if (data2 == null || data2.Length != 15) return ret;

            var data = new byte[15];
            Array.Copy(data2, data, 15);

            long b_sum = data[13] ^ data[12];
            b_sum = b_sum + data[11] + data[10] - data[9] - data[8];
            b_sum = (b_sum ^ data[7]) & 0xFF;
            long a_sum = data[6] ^ b_sum;
            a_sum = a_sum - data[5] - data[4];
            a_sum = (a_sum ^ data[3]) + data[2] + data[1] - data[0];
            a_sum &= 0xFF;

            if (a_sum != key[7]) return null;

            long key_offset = key[1] ^ key[2];
            key_offset = key_offset - key[3] - key[4];
            key_offset = (key_offset ^ key[5]) + key[6] - key[7];
            key_offset = (key_offset ^ data[14]) + 0x26;
            key_offset &= 0xFF;

            long key_index = 4;

            for (long x = 12; x >= 0; x--)
            {
                long _byte = data[x];

                for (long y = 4; y > 1; y--)
                {
                    key_offset--;
                    long bkey = KEY_TABLE[key_offset + 0x41];
                    long keyr = key[key_index];
                    if (--key_index == 0) key_index = 7;

                    switch (bkey & 3)
                    {
                        case 0: _byte = (_byte - bkey) - keyr; break;
                        case 1: _byte = (_byte + bkey) + keyr; break;
                        default: _byte = (_byte ^ bkey) ^ keyr; break;
                    }
                }
                ret.Add((byte)_byte);
            }

            return ret;
        }
    }
}
