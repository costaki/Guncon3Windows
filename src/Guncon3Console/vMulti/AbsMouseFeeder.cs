using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
using GunconUSB;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Guncon3Console.vMulti
{
    /// <summary>
    /// Feeds the stock vmulti absolute mouse endpoint.
    ///
    /// This mirrors vmulti's native client logic:
    /// - enumerate HID devices
    /// - match vmulti VID/PID
    /// - match HID caps UsagePage=0xFF00, Usage=0x0001 (control device)
    /// - write a 0x41-byte control report:
    ///     [VMultiControlReportHeader][VMultiMouseReport][padding...]
    /// </summary>
    internal sealed class VMultiAbsMouseFeeder : IMouseFeeder
    {
        private readonly VMultiHidController HID = new VMultiHidController();

        // Logical gun button -> vmulti mouse button mapping
        public readonly Dictionary<GunButton, TetherScript.MouseButton> _mapping = new Dictionary<GunButton, TetherScript.MouseButton>();

        public bool Force4by3 { get; set; } = false;

        private byte _buttons;

        public VMultiAbsMouseFeeder() { }

        public string Name => "vMulti AbsMouse";

        public bool IsConnected => HID.Connected;

        public void Log(string message) => Console.WriteLine("[" + Name + "]: " + message);

        public void ClearMapping() => _mapping.Clear();

        public int MappingCount() => _mapping.Count;

        public void AddMapping(GunButton gunButton, dynamic mapping)
        {
            if (mapping is TetherScript.MouseButton btn)
                _mapping[gunButton] = btn;
        }

        public dynamic GetMapping(GunButton gunButton)
        {
            if (_mapping.TryGetValue(gunButton, out var v))
                return v;
            return null;
        }

        public void Connect()
        {
            HID.OnLog += OnHidLog;
            HID.Connect();

            if (!HID.Connected)
                throw new Exception("Could not connect to vmulti absolute mouse control device.");
        }

        public void Disconnect()
        {
            HID.Disconnect();
            HID.OnLog -= OnHidLog;
        }

        public void OnHidLog(object sender, LogArgs e) => Log(e.Msg);

        public void SendDataToMouseAbs(ushort x, ushort y, sbyte wheel = 0)
        {
            HID.SendAbsoluteMouse(_buttons, x, y, wheel);
        }

        public void Feed(IGunState state)
        {
            short absX = 0;
            short absY = 0;

            if (state.IsInsideScreen)
            {
                absX = state.ABS_X;
                absY = state.ABS_Y;

                // Same behaviour as your existing feeder for 4:3 inside 16:9.
                if (Force4by3)
                {
                    if (!Helper.IsInsideCentered4By3(absX))
                    {
                        // Treat outside the 4:3 region as out-of-bounds.
                        absX = (short)Helper.GunAxisMax;
                        absY = (short)Helper.GunAxisMax;
                    }
                    else
                    {
                        absX = (short)Helper.ConvertRange4By3(absX);
                    }
                }
            }

            // Clamp to vmulti's declared mouse range: 0x0000..0x7FFF
            ushort x = ClampToUShort15(absX);
            ushort y = ClampToUShort15(absY);

            _buttons = 0;
            foreach (var map in _mapping)
            {
                if (!state.BtnState.TryGetValue(map.Key, out bool pressed) || !pressed)
                    continue;

                switch (map.Value)
                {
                    case TetherScript.MouseButton.Left:
                        _buttons |= VMultiHidController.MOUSE_BUTTON_1;
                        break;
                    case TetherScript.MouseButton.Right:
                        _buttons |= VMultiHidController.MOUSE_BUTTON_2;
                        break;
                    case TetherScript.MouseButton.Middle:
                        _buttons |= VMultiHidController.MOUSE_BUTTON_3;
                        break;
                }
            }

            SendDataToMouseAbs(x, y, 0);
        }

        private static ushort ClampToUShort15(short value)
        {
            if (value < 0) return 0;
            if (value > 0x7FFF) return 0x7FFF;
            return (ushort)value;
        }
    }

    public class LogArgs : EventArgs { public string Msg; }

    internal sealed class VMultiHidController
    {
        public const ushort VMULTI_VID = 0x00FF;
        public const ushort VMULTI_PID = 0xBACC;

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
        private string _devicePath;

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

                    if (!TryOpenMatchingInterface(info, ref ifData, out var handle, out var path))
                        continue;

                    _deviceHandle = handle;
                    _devicePath = path;
                    _connected = true;
                    DoLog($"Connected: {_devicePath}");
                    return;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }

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
            _devicePath = null;
        }

        public bool SendAbsoluteMouse(byte buttons, ushort x, ushort y, sbyte wheel)
        {
            if (!_connected || _deviceHandle == null || _deviceHandle.IsInvalid)
                return false;

            // vmulti native layout:
            // [0] ReportID = REPORTID_CONTROL (0x40)
            // [1] ReportLength = sizeof(VMultiMouseReport) = 7
            // [2] Mouse ReportID = REPORTID_MOUSE (0x03)
            // [3] Button
            // [4..5] XValue
            // [6..7] YValue
            // [8] WheelPosition
            // remaining bytes zero-padded to CONTROL_REPORT_SIZE (0x41)
            byte[] buffer = new byte[CONTROL_REPORT_SIZE];
            buffer[0] = REPORTID_CONTROL;
            buffer[1] = 7; // sizeof(VMultiMouseReport)
            buffer[2] = REPORTID_MOUSE;
            buffer[3] = buttons;

            WriteUInt16LE(buffer, 4, x);
            WriteUInt16LE(buffer, 6, y);
            buffer[8] = unchecked((byte)wheel);

            return Write(buffer);
        }

        private bool Write(byte[] buffer)
        {
            if (!WriteFile(_deviceHandle, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero))
            {
                DoLog("WriteFile failed: " + Marshal.GetLastWin32Error());
                return false;
            }

            if (written != buffer.Length)
            {
                DoLog($"Short write: {written}/{buffer.Length}");
                return false;
            }

            return true;
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
            out string path)
        {
            handle = null;
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

                IntPtr pPath = new IntPtr(detail.ToInt64() + 4);
                path = Marshal.PtrToStringAuto(pPath);

                var h = CreateFile(
                    path,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite,
                    IntPtr.Zero,
                    FileMode.Open,
                    0,
                    IntPtr.Zero);

                if (h == null || h.IsInvalid)
                    return false;

                if (!IsMatchingVMultiControlDevice(h))
                {
                    h.Close();
                    return false;
                }

                handle = h;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        private bool IsMatchingVMultiControlDevice(SafeFileHandle h)
        {
            var attrs = new HIDD_ATTRIBUTES
            {
                Size = Marshal.SizeOf<HIDD_ATTRIBUTES>()
            };

            if (!HidD_GetAttributes(h, ref attrs))
                return false;

            if (attrs.VendorID != VMULTI_VID || attrs.ProductID != VMULTI_PID)
                return false;

            if (!HidD_GetPreparsedData(h, out IntPtr ppd) || ppd == IntPtr.Zero)
                return false;

            try
            {
                if (HidP_GetCaps(ppd, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                    return false;

                return caps.UsagePage == VMULTI_USAGE_PAGE &&
                       caps.Usage == VMULTI_CONTROL_USAGE;
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
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
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
        private static extern bool HidD_GetAttributes(SafeFileHandle device, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr preparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

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