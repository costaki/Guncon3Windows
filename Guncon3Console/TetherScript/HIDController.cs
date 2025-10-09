using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using GunconUSB;

namespace Guncon3Console.TetherScript
{
    public class LogArgs : EventArgs { public string Msg; }

    class HIDController
    {
        public event EventHandler<LogArgs> OnLog;

        public Guid HIDGuid;
        protected bool FConnected = false;
        protected ushort FProductID;
        protected ushort FVendorID;
        protected SafeFileHandle FDevHandle;
        protected string FDevicePathName;

        public bool Connected { get => FConnected; set => FConnected = value; }
        public ushort ProductID { get => FProductID; set => FProductID = value; }
        public ushort VendorID { get => FVendorID; set => FVendorID = value; }

        public enum DiGetClassFlags : uint
        {
            DIGCF_PRESENT = 0x00000002,
            DIGCF_DEVICEINTERFACE = 0x00000010,
        }

        private readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public int flags;
            private IntPtr reserved;
        }

        // No lo usamos con marshalling directo; hacemos buffer manual con IntPtr.
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 1)]
        public struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            public uint cbSize;
            public char devicePath;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        // ===== P/Invoke corregido =====

        [DllImport("hid.dll", CharSet = CharSet.Auto)]
        static extern void HidD_GetHidGuid(out Guid ClassGuid);

        [DllImport("hid.dll", CharSet = CharSet.Auto)]
        static extern bool HidD_GetAttributes(SafeFileHandle HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool HidD_SetFeature(SafeFileHandle HidDeviceObject, byte[] Buffer, uint BufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern IntPtr SetupDiGetClassDevs(
            ref Guid ClassGuid,
            IntPtr Enumerator,
            IntPtr hwndParent,
            int Flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr hDevInfo,
            IntPtr devInfo,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        // 1ª pasada: pedir tamaño (deviceInfoData no usado -> IntPtr.Zero)
        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr hDevInfo,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        // CreateFile con FileAccess/FileShare correctos
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern SafeFileHandle CreateFile(
            string fileName,
            FileAccess fileAccess,
            FileShare fileShare,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool ReadFileEx(
            SafeFileHandle hFile,
            [Out] byte[] lpbuffer,
            uint nNumberOfBytesToRead,
            ref NativeOverlapped lpOverlapped,
            IntPtr lpCompletionRoutine);

        // ===== util =====
        private void DoLog(string msg) { try { OnLog?.Invoke(this, new LogArgs { Msg = msg }); } catch { } }

        public void DumpTetherscriptCandidates()
        {
            try
            {
                HidD_GetHidGuid(out HIDGuid);
                IntPtr info = SetupDiGetClassDevs(ref HIDGuid, IntPtr.Zero, IntPtr.Zero,
                    (int)(DiGetClassFlags.DIGCF_PRESENT | DiGetClassFlags.DIGCF_DEVICEINTERFACE));
                if (info == INVALID_HANDLE_VALUE) { Console.WriteLine("[HID] SetupDiGetClassDevs FAIL"); return; }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[HID] Enumerando HID presentes:");
                Console.ResetColor();

                uint i = 0;
                while (true)
                {
                    var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref HIDGuid, i, ref ifData)) break;

                    uint needed;
                    SetupDiGetDeviceInterfaceDetail(info, ref ifData, IntPtr.Zero, 0, out needed, IntPtr.Zero);

                    IntPtr detail = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        // cbSize: 8 en x64, 6 en x86
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                        if (SetupDiGetDeviceInterfaceDetail(info, ref ifData, detail, needed, out needed, IntPtr.Zero))
                        {
                            IntPtr pPath = new IntPtr(detail.ToInt64() + 4);
                            string path = Marshal.PtrToStringAuto(pPath);

                            ushort vid = 0, pid = 0;
                            var h = CreateFile(path, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                            if (h.IsInvalid)
                                h = CreateFile(path, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

                            if (!h.IsInvalid)
                            {
                                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                                if (HidD_GetAttributes(h, ref a)) { vid = a.VendorID; pid = a.ProductID; }
                                h.Close();
                            }

                            Console.WriteLine($" - {path}");
                            Console.WriteLine($"   VID=0x{vid:X4} PID=0x{pid:X4}");
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }

                    i++;
                }
            }
            catch (Exception ex) { Console.WriteLine("[HID] Dump error: " + ex.Message); }
        }

        public void Connect()
        {
            DoLog("Connecting...");
            if (FConnected) { DoLog("Already connected."); return; }

            HidD_GetHidGuid(out HIDGuid);

            IntPtr pnp = SetupDiGetClassDevs(ref HIDGuid, IntPtr.Zero, IntPtr.Zero,
                (int)(DiGetClassFlags.DIGCF_PRESENT | DiGetClassFlags.DIGCF_DEVICEINTERFACE));
            if (pnp == INVALID_HANDLE_VALUE) { DoLog("Connect: SetupDiGetClassDevs failed."); return; }

            bool foundAny = false, foundMine = false;
            uint idx = 0;

            do
            {
                var ifData = new SP_DEVICE_INTERFACE_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA)) };
                foundAny = SetupDiEnumDeviceInterfaces(pnp, IntPtr.Zero, ref HIDGuid, idx, ref ifData);
                if (foundAny)
                {
                    uint needed;
                    // 1ª llamada: tamaño
                    SetupDiGetDeviceInterfaceDetail(pnp, ref ifData, IntPtr.Zero, 0, out needed, IntPtr.Zero);

                    IntPtr detail = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        // 2ª llamada: datos
                        if (SetupDiGetDeviceInterfaceDetail(pnp, ref ifData, detail, needed, out needed, IntPtr.Zero))
                        {
                            IntPtr pPath = new IntPtr(detail.ToInt64() + 4);
                            FDevicePathName = Marshal.PtrToStringAuto(pPath);

                            var h = CreateFile(FDevicePathName, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                            if (h.IsInvalid)
                                h = CreateFile(FDevicePathName, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

                            if (!h.IsInvalid)
                            {
                                var a = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES)) };
                                if (HidD_GetAttributes(h, ref a) && a.VendorID == FVendorID && a.ProductID == FProductID)
                                {
                                    FDevHandle = h; // nos quedamos el handle válido
                                    foundMine = true;
                                    FConnected = true;
                                    DoLog("Connected.");
                                }
                                else
                                {
                                    h.Close();
                                }
                            }
                        }
                    }
                    finally { Marshal.FreeHGlobal(detail); }
                }
                idx++;
            }
            while (foundAny && !foundMine);
        }

        public void Disconnect()
        {
            FConnected = false;
            try { FDevHandle?.Close(); } catch { }
        }

        public bool SendData(byte[] buffer, uint bufferLength)
        {
            if (!FConnected) return false;
            return HidD_SetFeature(FDevHandle, buffer, bufferLength + 1);
        }

        public bool ReadData(byte[] buffer, uint bufferLength)
        {
            if (!FConnected || FDevHandle.IsInvalid) return false;
            var ov = new NativeOverlapped { EventHandle = IntPtr.Zero };
            _ = ReadFileEx(FDevHandle, buffer, bufferLength, ref ov, IntPtr.Zero);
            return true;
        }
    }
}
