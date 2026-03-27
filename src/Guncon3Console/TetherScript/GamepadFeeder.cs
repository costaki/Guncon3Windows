using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB;
using Guncon3Console.GunStates;

namespace Guncon3Console.TetherScript
{
    static class GamepadFeeder
    {
        private static readonly HIDController HID = new HIDController();

        // Map: logical gun button -> bit index in the gamepad Buttons field (0..15)
        public static readonly Dictionary<GunButton, int> Mapping = new Dictionary<GunButton, int>();

        private static ushort _buttons;

        public static void Connect()
        {
            HID.OnLog += Log;
            HID.VendorID = (ushort)DriversConst.TTC_VENDORID;
            HID.ProductID = (ushort)DriversConst.TTC_PRODUCTID_GAMEPAD;
            HID.Connect();

            if (!HID.Connected)
                throw new Exception("Could not connect to TetherScript Gamepad.");

            _buttons = 0;
        }

        public static void Disconnect()
        {
            HID.Disconnect();
            HID.OnLog -= Log;
        }

        private static void Log(object s, LogArgs e) => Console.WriteLine("Gamepad " + e.Msg);

        internal static void Feed(IGunState state)
        {
            _buttons = 0;
            foreach (var kv in Mapping)
            {
                if (!state.BtnState.TryGetValue(kv.Key, out bool pressed) || !pressed)
                    continue;

                int bit = kv.Value;
                if (bit < 0 || bit > 15) continue;
                _buttons = (ushort)(_buttons | (1 << bit));
            }

            var data = new SetFeatureGamepad
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = _buttons,
                LX = 0,
                LY = 0,
                RX = 0,
                RY = 0
            };

            byte[] buf = StructToBytes(data, Marshal.SizeOf(data));
            HID.SendData(buf, (uint)Marshal.SizeOf(data));
        }

        private static byte[] StructToBytes<T>(T value, int size) where T : struct
        {
            byte[] arr = new byte[size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, ptr, false);
                Marshal.Copy(ptr, arr, 0, size);
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return arr;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureGamepad
    {
        public byte ReportID;
        public byte CommandCode;
        public ushort Buttons;
        public short LX;
        public short LY;
        public short RX;
        public short RY;
    }
}
