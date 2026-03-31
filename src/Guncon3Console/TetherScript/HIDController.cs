using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using GunconUSB;
using Guncon3Console.Common;
using Guncon3Console.Common.Hid;

namespace Guncon3Console.TetherScript
{
    class HidController : HidDeviceEnumerator, IHidConnection
    {
        public event EventHandler<LogArgs> OnLog;

        private Guid HIDGuid;
        protected ushort FProductID;
        protected ushort FVendorID;
        protected SafeFileHandle FDevHandle;
        protected string FDevicePathName;

        public bool Connected => FDevHandle != null && !FDevHandle.IsInvalid;
        public ushort ProductID { get => FProductID; set => FProductID = value; }
        public ushort VendorID { get => FVendorID; set => FVendorID = value; }

        // NOTE: common HID enumeration types/PInvoke live in HidDeviceEnumerator.

        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        // ===== Fixed P/Invoke =====

        [DllImport("hid.dll", CharSet = CharSet.Auto)]
        static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] Buffer, uint BufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool ReadFileEx(
            SafeFileHandle hFile,
            [Out] byte[] lpbuffer,
            uint nNumberOfBytesToRead,
            ref NativeOverlapped lpOverlapped,
            IntPtr lpCompletionRoutine);

        // ===== util =====
        private void DoLog(string msg) { RaiseLog(OnLog, this, msg); }

        public HidController()
        {
            Log = DoLog;
        }

        public void DumpTetherscriptCandidates()
        {
            try
            {
                HidD_GetHidGuid(out HIDGuid);

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[HID] Enumerating present HID devices:");
                Console.ResetColor();

                EnumeratePresentHidInterfaces((info, ifData) =>
                {
                    if (TryGetDeviceInterfacePath(info, ref ifData, out string path) &&
                        TryOpenHidHandle(path, out var h))
                    {
                        ushort vid = 0, pid = 0;
                        try
                        {
                            var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                            if (HidD_GetAttributes(h, ref a)) { vid = a.VendorID; pid = a.ProductID; }
                        }
                        finally { h.Close(); }

                        Console.WriteLine($" - {path}");
                        Console.WriteLine($"   VID=0x{vid:X4} PID=0x{pid:X4}");
                    }
                });
            }
            catch (Exception ex) { Console.WriteLine("[HID] Dump error: " + ex.Message); }
        }

        public void Connect()
        {
            DoLog("Connecting...");
            if (Connected) { DoLog("Already connected."); return; }

            HidD_GetHidGuid(out HIDGuid);

            EnumeratePresentHidInterfaces((info, ifData) =>
            {
                if (TryGetDeviceInterfacePath(info, ref ifData, out FDevicePathName) &&
                    TryOpenHidHandle(FDevicePathName, out var h))
                {
                    var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                    if (HidD_GetAttributes(h, ref a) && a.VendorID == FVendorID && a.ProductID == FProductID)
                    {
                        FDevHandle = h; // keep the valid handle
                        DoLog("Connected.");
                        return false;
                    }
                    else
                    {
                        h.Close();
                    }
                }

                return true;
            });
        }

        public void Disconnect()
        {
            try { FDevHandle?.Close(); } catch { }
            FDevHandle = null;
        }

        public void Dispose()
        {
            Disconnect();
        }

        public bool SendData(byte[] buffer, uint bufferLength)
        {
            if (!Connected) return false;
            if (buffer == null) return false;
            uint len = bufferLength;
            if (len == 0 && buffer != null)
                len = (uint)buffer.Length;
            if (len == 0 || len > (uint)buffer.Length) return false;
            return HidD_SetFeature(FDevHandle, buffer, len);
        }

        public bool ReadData(byte[] buffer, uint bufferLength)
        {
            if (!Connected) return false;
            if (buffer == null) return false;
            if (bufferLength == 0 || bufferLength > (uint)buffer.Length) return false;
            var ov = new NativeOverlapped { EventHandle = IntPtr.Zero };
            _ = ReadFileEx(FDevHandle, buffer, bufferLength, ref ov, IntPtr.Zero);
            return true;
        }
    }
}
