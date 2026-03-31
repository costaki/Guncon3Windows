using Guncon3Console.Common;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Guncon3Console.vMulti
{
    internal sealed class VMultiHidController
    {
        public const ushort VMULTI_USAGE_PAGE = 0xFF00;
        public const ushort VMULTI_CONTROL_USAGE = 0x0001;

        public const byte REPORTID_MOUSE = 0x03;
        public const byte REPORTID_CONTROL = 0x40;
        public const int CONTROL_REPORT_SIZE = 0x41; // 65 bytes

        public const byte MOUSE_BUTTON_1 = 0x01;
        public const byte MOUSE_BUTTON_2 = 0x02;
        public const byte MOUSE_BUTTON_3 = 0x04;

        public event EventHandler<LogArgs> OnLog;

        private SafeFileHandle _deviceHandle;
        private bool _connected;
        private int _outputReportByteLength;
        private readonly byte[] _controlReportBuffer = new byte[CONTROL_REPORT_SIZE];

        public bool Connected => _connected;

        private void DoLog(string msg)
        {
            try { OnLog?.Invoke(this, new LogArgs { Msg = msg }); }
            catch { }
        }

        public void Connect()
        {
            if (_connected)
            {
                DoLog("Already connected.");
                return;
            }

            _deviceHandle = null;
            _outputReportByteLength = 0;

            HidD_GetHidGuid(out Guid hidGuid);

            IntPtr info = SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                (uint)(DiGetClassFlags.DIGCF_PRESENT | DiGetClassFlags.DIGCF_DEVICEINTERFACE));

            if (info == INVALID_HANDLE_VALUE)
            {
                DoLog("SetupDiGetClassDevs failed.");
                return;
            }

            try
            {
                uint index = 0;
                while (true)
                {
                    var ifData = new SP_DEVICE_INTERFACE_DATA
                    {
                        cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
                    };

                    if (!SetupDiEnumDeviceInterfaces(info, IntPtr.Zero, ref hidGuid, index, ref ifData))
                        break;

                    index++;

                    if (!TryOpenMatchingInterface(info, ref ifData, out var handle, out var outputReportByteLength))
                        continue;

                    _deviceHandle = handle;
                    _outputReportByteLength = outputReportByteLength;
                    _connected = true;
                    return;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }

            if (!_connected)
                DoLog("vmulti control device not found.");
        }

        public void Disconnect()
        {
            _connected = false;

            try
            {
                _deviceHandle?.Close();
            }
            catch { }

            _deviceHandle = null;
            _outputReportByteLength = 0;
        }

        public bool SendAbsoluteMouse(byte buttons, ushort x, ushort y, sbyte wheel)
        {
            if (!_connected || _deviceHandle == null || _deviceHandle.IsInvalid)
                return false;

            _controlReportBuffer[0] = REPORTID_CONTROL;
            _controlReportBuffer[1] = 7; // sizeof(VMultiMouseReport)
            _controlReportBuffer[2] = REPORTID_MOUSE;
            _controlReportBuffer[3] = buttons;

            WriteUInt16LE(_controlReportBuffer, 4, x);
            WriteUInt16LE(_controlReportBuffer, 6, y);
            _controlReportBuffer[8] = unchecked((byte)wheel);

            return Write(_controlReportBuffer);
        }

        private bool Write(byte[] buffer)
        {
            if (_outputReportByteLength > 0 && _outputReportByteLength < buffer.Length)
            {
                DoLog($"Output report length too small for vMulti write: caps={_outputReportByteLength}, need={buffer.Length}");
                return false;
            }

            if (WriteFile(_deviceHandle, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero))
            {
                if (written == buffer.Length)
                    return true;

                DoLog($"WriteFile short write: {written}/{buffer.Length}");
                return false;
            }

            int eWrite = Marshal.GetLastWin32Error();
            if (HidD_SetOutputReport(_deviceHandle, buffer, (uint)buffer.Length))
                return true;

            int eHid = Marshal.GetLastWin32Error();

            DoLog($"Output write failed. WriteFile={eWrite}, HidD_SetOutputReport={eHid}");
            return false;
        }

        private static void WriteUInt16LE(byte[] buffer, int offset, ushort value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        private bool TryOpenMatchingInterface(
            IntPtr info,
            ref SP_DEVICE_INTERFACE_DATA ifData,
            out SafeFileHandle handle,
            out int outputReportByteLength)
        {
            handle = null;
            outputReportByteLength = 0;

            if (!TryGetDeviceInterfacePath(info, ref ifData, out string path))
                return false;

            var h = CreateFile(
                path,
                FileAccess.ReadWrite,
                FileShare.ReadWrite,
                IntPtr.Zero,
                FileMode.Open,
                0,
                IntPtr.Zero);

            if (h == null || h.IsInvalid)
            {
                h = CreateFile(
                    path,
                    0,
                    FileShare.ReadWrite,
                    IntPtr.Zero,
                    FileMode.Open,
                    0,
                    IntPtr.Zero);
            }

            if (h == null || h.IsInvalid)
                return false;

            if (!IsMatchingVMultiControlDevice(h, out outputReportByteLength))
            {
                h.Close();
                return false;
            }

            handle = h;
            return true;
        }

        private bool TryGetDeviceInterfacePath(IntPtr info, ref SP_DEVICE_INTERFACE_DATA ifData, out string path)
        {
            path = null;

            SetupDiGetDeviceInterfaceDetail(info, ref ifData, IntPtr.Zero, 0, out uint needed, IntPtr.Zero);
            if (needed == 0)
            {
                DoLog("SetupDiGetDeviceInterfaceDetail size query failed: " + Marshal.GetLastWin32Error());
                return false;
            }

            IntPtr detail = Marshal.AllocHGlobal((int)needed);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                if (!SetupDiGetDeviceInterfaceDetail(info, ref ifData, detail, needed, out needed, IntPtr.Zero))
                {
                    DoLog("SetupDiGetDeviceInterfaceDetail failed: " + Marshal.GetLastWin32Error());
                    return false;
                }

                int pathOffset = 4;
                path = Marshal.PtrToStringAuto(IntPtr.Add(detail, pathOffset));
                return !string.IsNullOrWhiteSpace(path);
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        private bool IsMatchingVMultiControlDevice(SafeFileHandle h, out int outputReportByteLength)
        {
            outputReportByteLength = 0;

            if (!HidD_GetPreparsedData(h, out IntPtr ppd) || ppd == IntPtr.Zero)
                return false;

            try
            {
                if (HidP_GetCaps(ppd, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                    return false;

                outputReportByteLength = caps.OutputReportByteLength;

                bool usageMatch = caps.UsagePage == VMULTI_USAGE_PAGE &&
                                  (caps.Usage == VMULTI_CONTROL_USAGE);
                if (!usageMatch)
                    return false;

                if (caps.OutputReportByteLength < CONTROL_REPORT_SIZE)
                {
                    DoLog($"Rejected interface: OutputReportByteLength={caps.OutputReportByteLength} (<{CONTROL_REPORT_SIZE})");
                    return false;
                }

                return true;
            }
            finally
            {
                HidD_FreePreparsedData(ppd);
            }
        }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private const int HIDP_STATUS_SUCCESS = 0x00110000;

        [Flags]
        private enum DiGetClassFlags : uint
        {
            DIGCF_PRESENT = 0x00000002,
            DIGCF_DEVICEINTERFACE = 0x00000010,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid interfaceClassGuid;
            public uint flags;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public short InputReportByteLength;
            public short OutputReportByteLength;
            public short FeatureReportByteLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public short[] Reserved;

            public short NumberLinkCollectionNodes;
            public short NumberInputButtonCaps;
            public short NumberInputValueCaps;
            public short NumberInputDataIndices;
            public short NumberOutputButtonCaps;
            public short NumberOutputValueCaps;
            public short NumberOutputDataIndices;
            public short NumberFeatureButtonCaps;
            public short NumberFeatureValueCaps;
            public short NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(SafeFileHandle hFile, byte[] lpReportBuffer, uint reportBufferLength);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(
            ref Guid classGuid,
            IntPtr enumerator,
            IntPtr hwndParent,
            uint flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet,
            IntPtr deviceInfoData,
            ref Guid interfaceClassGuid,
            uint memberIndex,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern SafeFileHandle CreateFile(
            string fileName,
            FileAccess fileAccess,
            FileShare fileShare,
            IntPtr securityAttributes,
            FileMode creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);
    }
}
