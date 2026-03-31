using Guncon3Console.Common;
using Guncon3Console.Common.Hid;
using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Guncon3Console.vMulti
{
    internal sealed class HidController : HidDeviceEnumerator, IHidConnection
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
        private int _outputReportByteLength;
        private readonly byte[] _controlReportBuffer = new byte[CONTROL_REPORT_SIZE];

        public bool Connected => _deviceHandle != null && !_deviceHandle.IsInvalid;

        private void DoLog(string msg)
        {
            RaiseLog(OnLog, this, msg);
        }

        public HidController()
        {
            Log = DoLog;
        }

        public void Connect()
        {
            DoLog("Connecting...");

            if (Connected)
            {
                DoLog("Already connected.");
                return;
            }

            _deviceHandle = null;
            _outputReportByteLength = 0;

            EnumeratePresentHidInterfaces((info, ifData) =>
            {
                if (!TryOpenMatchingInterface(info, ref ifData, out var handle, out var outputReportByteLength))
                    return true;

                _deviceHandle = handle;
                _outputReportByteLength = outputReportByteLength;
                DoLog("Connected.");
                return false;
            });

            if (!Connected)
                DoLog("vMulti control device not found.");
        }

        public void Disconnect()
        {
            try
            {
                _deviceHandle?.Close();
            }
            catch { }

            _deviceHandle = null;
            _outputReportByteLength = 0;
        }

        public void Dispose()
        {
            Disconnect();
        }

        public bool SendAbsoluteMouse(byte buttons, ushort x, ushort y, sbyte wheel)
        {
            if (!Connected)
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
            if (buffer == null || buffer.Length == 0)
                return false;

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

            if (!TryOpenHidHandle(path, out var h))
                return false;

            if (!IsMatchingVMultiControlDevice(h, out outputReportByteLength))
            {
                h.Close();
                return false;
            }

            handle = h;
            return true;
        }

        private new bool TryGetDeviceInterfacePath(IntPtr info, ref SP_DEVICE_INTERFACE_DATA ifData, out string path)
        {
            if (base.TryGetDeviceInterfacePath(info, ref ifData, out path))
                return true;

            LogLastError("SetupDiGetDeviceInterfaceDetail failed: ");
            return false;
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

        private const int HIDP_STATUS_SUCCESS = 0x00110000;


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

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(SafeFileHandle hFile, byte[] lpReportBuffer, uint reportBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(
            SafeFileHandle hFile,
            byte[] lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);
    }
}
