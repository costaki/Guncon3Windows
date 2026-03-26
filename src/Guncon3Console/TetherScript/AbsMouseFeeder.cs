using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB;

// Important: always use the enum from the GunconUSB project (singular)


namespace Guncon3Console.TetherScript
{
    static class AbsMouseFeeder
    {
        private static readonly HIDController HID = new HIDController();

        // Map: logical gun button (public enum in GunconUSB) -> TetherScript virtual mouse button
        public static readonly Dictionary<GunButton, MouseButton> Mapping = new Dictionary<GunButton, MouseButton>();

        public static bool Force4by3 = false;
        private static byte btns = 0;

        public static void Connect()
        {
            HID.OnLog += Log;
            HID.VendorID = (ushort)DriversConst.TTC_VENDORID;            // VendorId TetherScript
            HID.ProductID = (ushort)DriversConst.TTC_PRODUCTID_MOUSEABS;  // ProductId Mouse Abs
            HID.Connect();

            if (!HID.Connected)
                throw new Exception("Coud not connect to TetherScript's AbsMouse");
        }

        public static void Disconnect()
        {
            HID.Disconnect();
            HID.OnLog -= Log;
        }

        private static void Log(object s, LogArgs e) => Console.WriteLine("Mouse " + e.Msg);

        public static void Send_Data_To_MouseAbs(ushort x, ushort y)
        {
            var data = new SetFeatureMouseAbs
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = btns,
                X = x,
                Y = y
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
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            return arr;
        }

        internal static void Feed()
        {
            short absX = 0;
            short absY = 0;

            if (GunState.IsInsideScreen)
            {
                absX = GunState.ABS_X;
                absY = GunState.ABS_Y;

                // 4:3 inside 16:9 (MAME)
                if (Force4by3)
                    absX = (short)Helper.ConvertRange(4096, 28671, 0, 32767, absX);
            }

            // buttons
            btns = 0;
            foreach (var map in Mapping)
            {
                if (!GunState.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) btns = (byte)(btns | 1);
                if (map.Value == MouseButton.Right) btns = (byte)(btns | (1 << 1));
                if (map.Value == MouseButton.Middle) btns = (byte)(btns | (1 << 2));
            }

            Send_Data_To_MouseAbs((ushort)absX, (ushort)absY);
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct SetFeatureMouseAbs
    {
        public byte ReportID;
        public byte CommandCode;
        public byte Buttons;
        public ushort X;
        public ushort Y;
    }
}
