using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GunconUSB;

namespace Guncon3Console.TetherScript
{
    static class RelMouseFeeder
    {
        private static readonly HIDController HID = new HIDController();

        public static readonly Dictionary<GunButton, MouseButton> Mapping = new Dictionary<GunButton, MouseButton>();

        // Relative movement is derived from deltas between consecutive ABS samples.
        private static short _lastAbsX;
        private static short _lastAbsY;
        private static bool _haveLast;
        private static byte _btns;

        public static void Connect()
        {
            HID.OnLog += Log;
            HID.VendorID = (ushort)DriversConst.TTC_VENDORID;
            HID.ProductID = (ushort)DriversConst.TTC_PRODUCTID_MOUSEREL;
            HID.Connect();

            if (!HID.Connected)
                throw new Exception("Could not connect to TetherScript's RelMouse");

            _haveLast = false;
            _lastAbsX = 0;
            _lastAbsY = 0;
            _btns = 0;
        }

        public static void Disconnect()
        {
            HID.Disconnect();
            HID.OnLog -= Log;
        }

        private static void Log(object s, LogArgs e) => Console.WriteLine("MouseRel " + e.Msg);

        internal static void Feed()
        {
            // Buttons
            _btns = 0;
            foreach (var map in Mapping)
            {
                if (!GunState.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                if (map.Value == MouseButton.Left) _btns = (byte)(_btns | 1);
                if (map.Value == MouseButton.Right) _btns = (byte)(_btns | (1 << 1));
                if (map.Value == MouseButton.Middle) _btns = (byte)(_btns | (1 << 2));
            }

            // If outside screen, send zero delta but still update buttons.
            short absX = GunState.IsInsideScreen ? GunState.ABS_X : (short)0;
            short absY = GunState.IsInsideScreen ? GunState.ABS_Y : (short)0;

            short dx = 0;
            short dy = 0;

            if (_haveLast && GunState.IsInsideScreen)
            {
                dx = (short)(absX - _lastAbsX);
                dy = (short)(absY - _lastAbsY);

                // Clamp to avoid ridiculous jumps on recenter/first frame.
                dx = Clamp(dx, -2047, 2047);
                dy = Clamp(dy, -2047, 2047);
            }

            _lastAbsX = absX;
            _lastAbsY = absY;
            _haveLast = true;

            Send_Data_To_MouseRel(dx, dy);
        }

        private static short Clamp(short v, short min, short max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static void Send_Data_To_MouseRel(short dx, short dy)
        {
            // NOTE:
            // This struct layout is an educated guess based on MouseAbs + typical Rel mice.
            // If TetherScript expects a different payload, we’ll adjust once tested.
            var data = new SetFeatureMouseRel
            {
                ReportID = 1,
                CommandCode = 2,
                Buttons = _btns,
                Dx = dx,
                Dy = dy
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
    public struct SetFeatureMouseRel
    {
        public byte ReportID;
        public byte CommandCode;
        public byte Buttons;
        public short Dx;
        public short Dy;
    }
}