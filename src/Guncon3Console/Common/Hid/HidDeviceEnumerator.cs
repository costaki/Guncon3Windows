using Microsoft.Win32.SafeHandles;
using Guncon3Console.Common;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Guncon3Console.Common.Hid
{
    internal abstract class HidDeviceEnumerator
    {
        protected Action<string> Log { get; set; }

        protected void RaiseLog(EventHandler<LogArgs> handler, object sender, string message)
        {
            if (handler == null)
                return;

            try
            {
                handler(sender, new LogArgs { Msg = message });
            }
            catch { }
        }

        protected static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [Flags]
        protected enum DiGetClassFlags : uint
        {
            DIGCF_PRESENT = 0x00000002,
            DIGCF_DEVICEINTERFACE = 0x00000010,
        }

        [StructLayout(LayoutKind.Sequential)]
        protected struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public uint flags;
            public IntPtr reserved;
        }

        [DllImport("hid.dll")]
        protected static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("setupapi.dll", SetLastError = true)]
        protected static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        protected static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        protected static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        protected static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        protected static extern SafeFileHandle CreateFile(
            string fileName,
            FileAccess fileAccess,
            FileShare fileShare,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        protected bool TryGetDeviceInterfacePath(IntPtr info, ref SP_DEVICE_INTERFACE_DATA ifData, out string path)
        {
            path = null;

            SetupDiGetDeviceInterfaceDetail(info, ref ifData, IntPtr.Zero, 0, out uint needed, IntPtr.Zero);
            if (needed == 0)
                return false;

            IntPtr detail = Marshal.AllocHGlobal((int)needed);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                if (!SetupDiGetDeviceInterfaceDetail(info, ref ifData, detail, needed, out needed, IntPtr.Zero))
                    return false;

                path = Marshal.PtrToStringAuto(IntPtr.Add(detail, 4));
                return !string.IsNullOrWhiteSpace(path);
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        protected bool TryOpenHidHandle(string path, out SafeFileHandle handle)
        {
            handle = null;

            var h = CreateFile(path, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (h == null || h.IsInvalid)
                h = CreateFile(path, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

            if (h == null || h.IsInvalid)
                return false;

            handle = h;
            return true;
        }

        protected void LogLastError(string prefix)
        {
            Log?.Invoke(prefix + Marshal.GetLastWin32Error());
        }

        protected void EnumeratePresentHidInterfaces(Action<IntPtr, SP_DEVICE_INTERFACE_DATA> onInterface)
        {
            if (onInterface == null)
                throw new ArgumentNullException(nameof(onInterface));

            EnumeratePresentHidInterfaces((info, ifData) =>
            {
                onInterface(info, ifData);
                return true;
            });
        }

        protected void EnumeratePresentHidInterfaces(Func<IntPtr, SP_DEVICE_INTERFACE_DATA, bool> onInterface)
        {
            if (onInterface == null)
                throw new ArgumentNullException(nameof(onInterface));

            HidD_GetHidGuid(out Guid hidGuid);
            IntPtr info = SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                (uint)(DiGetClassFlags.DIGCF_PRESENT | DiGetClassFlags.DIGCF_DEVICEINTERFACE));

            if (info == INVALID_HANDLE_VALUE)
            {
                LogLastError("SetupDiGetClassDevs failed: ");
                return;
            }

            try
            {
                uint index = 0;
                while (true)
                {
                    var ifData = new SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA))
                    };

                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, index, ref ifData))
                        break;

                    if (!onInterface(info, ifData))
                        break;
                    index++;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }
        }
    }
}
