using Guncon3Console.Feeders;
using Guncon3Console.GunStates;
using GunconUSB;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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
        public const ushort VMULTI_MESSAGE_USAGE = 0x0002;

        public const byte REPORTID_MOUSE = 0x03;
        public const byte REPORTID_CONTROL = 0x40;
        public const int CONTROL_REPORT_SIZE = 0x41; // 65 bytes

        public const byte MOUSE_BUTTON_1 = 0x01;
        public const byte MOUSE_BUTTON_2 = 0x02;
        public const byte MOUSE_BUTTON_3 = 0x04;

        public event EventHandler<LogArgs> OnLog;

        private SafeFileHandle _controlHandle;
        private SafeFileHandle _messageHandle;
        private bool _connected;
        private string _controlPath;
        private string _messagePath;
        private int _controlOutputReportByteLength;
        private int _messageOutputReportByteLength;
        private bool _loggedFirstReport;
        private bool _messageWritesDisabled;

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

            _controlHandle = null;
            _messageHandle = null;
            _controlPath = null;
            _messagePath = null;
            _controlOutputReportByteLength = 0;
            _messageOutputReportByteLength = 0;
            _loggedFirstReport = false;
            _messageWritesDisabled = false;

            LogDisabledVMultiMouseCollections();

            // Optional override for troubleshooting / nonstandard vmulti installs.
            // Set VMULTI_CONTROL_PATH to a full HID device interface path (e.g. "\\?\hid#vmultia&col08#...#{4d1e55b2-f16f-11cf-88cb-001111000030}")
            // to bypass SetupDi enumeration.
            string overridePath = null;
            try { overridePath = Environment.GetEnvironmentVariable("VMULTI_CONTROL_PATH"); } catch { }
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                DoLog("VMULTI_CONTROL_PATH=" + overridePath);
                if (TryOpenPathIfMatches(overridePath, out var h))
                {
                    // Override is assumed to be the control interface.
                    _controlHandle = h;
                    _controlPath = overridePath;
                    _controlOutputReportByteLength = TryGetOutputReportByteLength(h);
                    _connected = true;
                    DoLog($"Connected control: {_controlPath}");
                    DoLog($"Control OutputReportByteLength={_controlOutputReportByteLength}");
                    return;
                }

                DoLog("VMULTI_CONTROL_PATH set but did not match vMulti control interface: " + overridePath);
            }

            // First try: open the well-known vmultia control collection directly.
            // On this machine, the control device is exposed as: \\?\hid#vmultia&col08#... and reports UP:FF00_U:0001.
            if (TryOpenKnownVMultiPath(out var knownHandle, out var knownPath))
            {
                var usage = TryGetUsage(knownHandle);
                if (usage == VMULTI_CONTROL_USAGE)
                {
                    _controlHandle = knownHandle;
                    _controlPath = knownPath;
                    _controlOutputReportByteLength = TryGetOutputReportByteLength(knownHandle);
                    DoLog($"Connected control: {_controlPath}");
                    DoLog($"Control OutputReportByteLength={_controlOutputReportByteLength}");
                }
                else if (usage == VMULTI_MESSAGE_USAGE)
                {
                    _messageHandle = knownHandle;
                    _messagePath = knownPath;
                    _messageOutputReportByteLength = TryGetOutputReportByteLength(knownHandle);
                    DoLog($"Connected message: {_messagePath}");
                    DoLog($"Message OutputReportByteLength={_messageOutputReportByteLength}");
                }
                else
                {
                    knownHandle?.Close();
                }

                // Continue enumeration to find the other interface if needed.
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

                    if (!TryOpenMatchingInterface(info, ref ifData, out var handle, out var path, out var usage))
                        continue;

                    if (usage == VMULTI_CONTROL_USAGE)
                    {
                        _controlHandle = handle;
                        _controlPath = path;
                        _controlOutputReportByteLength = TryGetOutputReportByteLength(handle);
                        DoLog($"Connected control: {_controlPath}");
                        DoLog($"Control OutputReportByteLength={_controlOutputReportByteLength}");
                    }
                    else if (usage == VMULTI_MESSAGE_USAGE)
                    {
                        _messageHandle = handle;
                        _messagePath = path;
                        _messageOutputReportByteLength = TryGetOutputReportByteLength(handle);
                        DoLog($"Connected message: {_messagePath}");
                        DoLog($"Message OutputReportByteLength={_messageOutputReportByteLength}");
                    }
                    else
                    {
                        handle?.Close();
                    }

                    if (_controlHandle != null && !_controlHandle.IsInvalid)
                        _connected = true;

                    if (_controlHandle != null && !_controlHandle.IsInvalid && _messageHandle != null && !_messageHandle.IsInvalid)
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

        private void LogDisabledVMultiMouseCollections()
        {
            try
            {
                // If the actual vMulti mouse collections are disabled in Device Manager,
                // writing to the vendor-defined control interface will not produce OS mouse movement.
                // Common collections:
                //  - COL03 / COL04: HID-compliant mouse (often disabled to avoid duplicate pointers)
                DoLog("Scanning for vMulti mouse collections (COL03/COL04) disabled state...");

                HidD_GetHidGuid(out Guid hidGuid);
                IntPtr info = SetupDiGetClassDevs(
                    ref hidGuid,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    (uint)(DiGetClassFlags.DIGCF_PRESENT | DiGetClassFlags.DIGCF_DEVICEINTERFACE));

                if (info == INVALID_HANDLE_VALUE)
                    return;

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

                        if (!TryGetDeviceInterfacePath(info, ref ifData, out string path))
                            continue;

                        // The interface path isn't the instance id, but it contains "colXX".
                        string pl = path?.ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(pl))
                            continue;

                        if (!(pl.Contains("#col03#") || pl.Contains("#col04#")))
                            continue;

                        if (!TryGetInterfaceInstanceId(info, ref ifData, out string instanceId))
                            continue;

                        // Query ConfigFlags to infer disabled state.
                        bool? disabled = TryIsDeviceDisabled(info, ref ifData);
                        DoLog($"vMulti mouse collection found: {instanceId} Disabled={(disabled.HasValue ? disabled.Value.ToString() : "?")}");
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(info);
                }
            }
            catch
            {
                // diagnostics only
            }
        }

        private bool TryGetInterfaceInstanceId(IntPtr info, ref SP_DEVICE_INTERFACE_DATA ifData, out string instanceId)
        {
            instanceId = null;
            try
            {
                if (!SetupDiGetDeviceInterfaceDetail(info, ref ifData, IntPtr.Zero, 0, out uint needed, IntPtr.Zero))
                {
                    // expected for size query
                }

                if (needed == 0)
                    return false;

                IntPtr detail = Marshal.AllocHGlobal((int)needed);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    var devInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };

                    if (!SetupDiGetDeviceInterfaceDetailWithDevinfo(info, ref ifData, detail, needed, out needed, ref devInfo))
                        return false;

                    // Get instance id from SP_DEVINFO_DATA
                    var sb = new StringBuilder(512);
                    if (!SetupDiGetDeviceInstanceId(info, ref devInfo, sb, (uint)sb.Capacity, out uint _))
                        return false;

                    instanceId = sb.ToString();
                    return !string.IsNullOrWhiteSpace(instanceId);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
            catch
            {
                return false;
            }
        }

        private bool? TryIsDeviceDisabled(IntPtr info, ref SP_DEVICE_INTERFACE_DATA ifData)
        {
            try
            {
                if (!TryGetInterfaceDevinfoData(info, ref ifData, out var devInfo))
                    return null;

                // SPDRP_CONFIGFLAGS is a REG_DWORD. If CONFIGFLAG_DISABLED is set, device is disabled.
                const uint SPDRP_CONFIGFLAGS = 0x0000000A;
                const uint CONFIGFLAG_DISABLED = 0x00000001;

                byte[] buf = new byte[4];
                if (!SetupDiGetDeviceRegistryProperty(info, ref devInfo, SPDRP_CONFIGFLAGS, out uint _, buf, (uint)buf.Length, out uint _))
                    return null;

                uint flags = BitConverter.ToUInt32(buf, 0);
                return (flags & CONFIGFLAG_DISABLED) != 0;
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetInterfaceDevinfoData(IntPtr info, ref SP_DEVICE_INTERFACE_DATA ifData, out SP_DEVINFO_DATA devInfo)
        {
            devInfo = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>() };
            SetupDiGetDeviceInterfaceDetail(info, ref ifData, IntPtr.Zero, 0, out uint needed, IntPtr.Zero);
            if (needed == 0)
                return false;

            IntPtr detail = Marshal.AllocHGlobal((int)needed);
            try
            {
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                return SetupDiGetDeviceInterfaceDetailWithDevinfo(info, ref ifData, detail, needed, out needed, ref devInfo);
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        private bool TryOpenKnownVMultiPath(out SafeFileHandle handle, out string path)
        {
            handle = null;
            path = null;

            // HID path format resembles:
            //  \\?\hid#vmultia&col08#...#{4d1e55b2-f16f-11cf-88cb-001111000030}
            // The {..0030} GUID is GUID_DEVINTERFACE_HID.
            const string hidInterfaceGuid = "{4d1e55b2-f16f-11cf-88cb-001111000030}";
            // Actual device paths look like: "\\?\hid#vmultia&col08#...#{GUID}"
            string basePrefixCol08 = "\\\\?\\hid#vmultia&col08#";
            string basePrefixCol09 = "\\\\?\\hid#vmultia&col09#";
            // Device paths contain "#{GUID}" (without an extra separator).
            string suffix = hidInterfaceGuid;

            HidD_GetHidGuid(out Guid hidGuid);

            IntPtr info = SetupDiGetClassDevs(
                ref hidGuid,
                IntPtr.Zero,
                IntPtr.Zero,
                (uint)(DiGetClassFlags.DIGCF_PRESENT | DiGetClassFlags.DIGCF_DEVICEINTERFACE));

            if (info == INVALID_HANDLE_VALUE)
                return false;

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

                    if (!TryGetDeviceInterfacePath(info, ref ifData, out string candidatePath))
                        continue;

                    if (string.IsNullOrWhiteSpace(candidatePath))
                        continue;

                    DoLog("HID candidate: " + candidatePath);

                    string p = candidatePath.ToLowerInvariant();
                    if (!(p.StartsWith(basePrefixCol08) || p.StartsWith(basePrefixCol09)) || !p.Contains(suffix))
                        continue;

                    var h = CreateFile(candidatePath, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
                    if (h == null || h.IsInvalid)
                        h = CreateFile(candidatePath, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

                    if (h == null || h.IsInvalid)
                    {
                        DoLog("CreateFile failed: " + Marshal.GetLastWin32Error());
                        continue;
                    }

                    if (!IsMatchingVMultiControlDevice(h, out _))
                    {
                        DoLog("Not a vMulti control device.");
                        h.Close();
                        continue;
                    }

                    handle = h;
                    path = candidatePath;
                    return true;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(info);
            }

            return false;
        }

        private bool TryOpenPathIfMatches(string candidatePath, out SafeFileHandle handle)
        {
            handle = null;
            if (string.IsNullOrWhiteSpace(candidatePath))
                return false;

            var h = CreateFile(candidatePath, FileAccess.ReadWrite, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
            if (h == null || h.IsInvalid)
                h = CreateFile(candidatePath, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);

            if (h == null || h.IsInvalid)
            {
                DoLog("CreateFile (override) failed: " + Marshal.GetLastWin32Error());
                return false;
            }

            if (!IsMatchingVMultiControlDevice(h, out _))
            {
                DoLog("Override path opened but did not match vMulti control UsagePage/Usage.");
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
                // SP_DEVICE_INTERFACE_DETAIL_DATA is variable-sized.
                // We only write cbSize then read the returned DevicePath string directly from the buffer.
                // (Using a fixed-size string field here frequently breaks SetupDiGetDeviceInterfaceDetail.)
                Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);

                if (!SetupDiGetDeviceInterfaceDetail(info, ref ifData, detail, needed, out needed, IntPtr.Zero))
                {
                    DoLog("SetupDiGetDeviceInterfaceDetail failed: " + Marshal.GetLastWin32Error());
                    return false;
                }

                // On both x86 and x64, the string starts immediately after the initial DWORD cbSize,
                // but is aligned to pointer-size in the native struct layout.
                // Matches existing HIDController logic in this repo: the string starts right after cbSize.
                int pathOffset = 4;
                path = Marshal.PtrToStringAuto(IntPtr.Add(detail, pathOffset));
                return !string.IsNullOrWhiteSpace(path);
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }

        public void Disconnect()
        {
            _connected = false;

            try
            {
                _controlHandle?.Close();
            }
            catch { }

            try
            {
                _messageHandle?.Close();
            }
            catch { }

            _controlHandle = null;
            _messageHandle = null;
            _controlPath = null;
            _messagePath = null;
            _controlOutputReportByteLength = 0;
            _messageOutputReportByteLength = 0;
            _loggedFirstReport = false;
            _messageWritesDisabled = false;
        }

        public bool SendAbsoluteMouse(byte buttons, ushort x, ushort y, sbyte wheel)
        {
            if (!_connected || _controlHandle == null || _controlHandle.IsInvalid)
                return false;

            // vmulti native layout (CONTROL header + packed mouse report), as used by vmulti-client:
            // [0] control_report_id (0x40)
            // [1] report_length (sizeof mouse report = 7)
            // [2] report_id (0x03)
            // [3] buttons
            // [4..5] x
            // [6..7] y
            // [8] wheel_position
            byte[] buffer = new byte[CONTROL_REPORT_SIZE];
            buffer[0] = REPORTID_CONTROL;
            buffer[1] = 7; // sizeof(VMultiMouseReport)
            buffer[2] = REPORTID_MOUSE;
            buffer[3] = buttons;

            WriteUInt16LE(buffer, 4, x);
            WriteUInt16LE(buffer, 6, y);
            buffer[8] = unchecked((byte)wheel);

            bool ok = false;

            // Always send on control interface.
            ok |= WriteTo(_controlHandle, buffer, isControl: true);

            // Some vMulti builds consume output on the message interface as well.
            if (!_messageWritesDisabled && _messageHandle != null && !_messageHandle.IsInvalid)
                ok |= WriteTo(_messageHandle, buffer, isControl: false);

            return ok;
        }

        private bool WriteTo(SafeFileHandle handle, byte[] buffer, bool isControl)
        {
            if (!_loggedFirstReport)
            {
                _loggedFirstReport = true;
                DoLog("First report: " + ToHex(buffer, 0, Math.Min(16, buffer.Length)) + (buffer.Length > 16 ? " ..." : string.Empty));
            }

            // If the HID caps say there is no output report, WriteFile/HidD_SetOutputReport may "succeed" without effect.
            // vmulti should expose an output report length >= 0x41 for the control interface.
            int capLen = isControl ? _controlOutputReportByteLength : _messageOutputReportByteLength;
            if (capLen > 0 && capLen < buffer.Length)
            {
                DoLog($"Output report length too small for vMulti {(isControl ? "control" : "message")} write: caps={capLen}, need={buffer.Length}");
                if (!isControl)
                    _messageWritesDisabled = true;
                return false;
            }

            // Empirically, some vMulti builds return success for HidD_SetOutputReport but do not generate input.
            // The vmulti-client implementation uses WriteFile, so prefer it and only fall back to HidD_SetOutputReport.
            if (WriteFile(handle, buffer, (uint)buffer.Length, out uint written, IntPtr.Zero))
            {
                if (written == buffer.Length)
                    return true;

                DoLog($"WriteFile short write: {written}/{buffer.Length}");
                return false;
            }

            int eWrite = Marshal.GetLastWin32Error();
            if (HidD_SetOutputReport(handle, buffer, (uint)buffer.Length))
                return true;

            int eHid = Marshal.GetLastWin32Error();

            if (!isControl)
                _messageWritesDisabled = true;

            if (isControl || !_messageWritesDisabled)
                DoLog($"Output write failed ({(isControl ? "control" : "message")}). caps={capLen}, WriteFile={eWrite}, HidD_SetOutputReport={eHid}");
            return false;
        }

        private int TryGetOutputReportByteLength(SafeFileHandle h)
        {
            if (h == null || h.IsInvalid)
                return 0;

            if (!HidD_GetPreparsedData(h, out IntPtr ppd) || ppd == IntPtr.Zero)
                return 0;

            try
            {
                if (HidP_GetCaps(ppd, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                    return 0;

                return caps.OutputReportByteLength;
            }
            finally
            {
                HidD_FreePreparsedData(ppd);
            }
        }

        private static string ToHex(byte[] data, int offset, int count)
        {
            if (data == null || data.Length == 0 || count <= 0)
                return string.Empty;

            int end = Math.Min(data.Length, offset + count);
            var parts = new string[end - offset];
            int j = 0;
            for (int i = offset; i < end; i++)
                parts[j++] = data[i].ToString("X2");
            return string.Join(" ", parts);
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
            out string path,
            out ushort usage)
        {
            handle = null;
            path = null;
            usage = 0;

            if (!TryGetDeviceInterfacePath(info, ref ifData, out path))
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

            if (!IsMatchingVMultiControlDevice(h, out usage))
            {
                h.Close();
                return false;
            }

            handle = h;
            return true;
        }

        private bool IsMatchingVMultiControlDevice(SafeFileHandle h, out ushort usage)
        {
            usage = 0;
            var attrs = new HIDD_ATTRIBUTES
            {
                Size = Marshal.SizeOf<HIDD_ATTRIBUTES>()
            };

            if (!HidD_GetAttributes(h, ref attrs))
                return false;

            if (!HidD_GetPreparsedData(h, out IntPtr ppd) || ppd == IntPtr.Zero)
                return false;

            try
            {
                if (HidP_GetCaps(ppd, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                    return false;

                usage = caps.Usage;

                bool usageMatch = caps.UsagePage == VMULTI_USAGE_PAGE &&
                                  (caps.Usage == VMULTI_CONTROL_USAGE || caps.Usage == VMULTI_MESSAGE_USAGE);
                if (!usageMatch)
                    return false;

                // Avoid false positives: the vMulti control interface must accept the 0x41-byte control report.
                // Some other collections may share the same usage page/usage but not expose an output report.
                if (caps.OutputReportByteLength < CONTROL_REPORT_SIZE)
                {
                    DoLog($"Rejected interface: OutputReportByteLength={caps.OutputReportByteLength} (<{CONTROL_REPORT_SIZE})");
                    return false;
                }

                DoLog($"Matched interface: UsagePage=0x{caps.UsagePage:X4} Usage=0x{caps.Usage:X4} OutputReportByteLength={caps.OutputReportByteLength}");
                return true;
            }
            finally
            {
                HidD_FreePreparsedData(ppd);
            }
        }

        private ushort TryGetUsage(SafeFileHandle h)
        {
            if (h == null || h.IsInvalid)
                return 0;

            if (!HidD_GetPreparsedData(h, out IntPtr ppd) || ppd == IntPtr.Zero)
                return 0;

            try
            {
                if (HidP_GetCaps(ppd, out HIDP_CAPS caps) != HIDP_STATUS_SUCCESS)
                    return 0;

                return caps.Usage;
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
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        // Note: SP_DEVICE_INTERFACE_DETAIL_DATA contains a variable-length string.
        // We intentionally do not model it as a managed struct; see TryGetDeviceInterfacePath.

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

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true, EntryPoint = "SetupDiGetDeviceInterfaceDetail")]
        private static extern bool SetupDiGetDeviceInterfaceDetailWithDevinfo(
            IntPtr deviceInfoSet,
            ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData,
            uint deviceInterfaceDetailDataSize,
            out uint requiredSize,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceId(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            StringBuilder deviceInstanceId,
            uint deviceInstanceIdSize,
            out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SetupDiGetDeviceRegistryProperty(
            IntPtr deviceInfoSet,
            ref SP_DEVINFO_DATA deviceInfoData,
            uint property,
            out uint propertyRegDataType,
            byte[] propertyBuffer,
            uint propertyBufferSize,
            out uint requiredSize);

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

        // Note: vMulti control writes are done through HidD_SetOutputReport; WriteFile is intentionally not used.
    }
}