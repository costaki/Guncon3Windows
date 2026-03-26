using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace GunconUSB
{
    static class GammaManager
    {

        [DllImport("gdi32.dll")]
        private unsafe static extern bool SetDeviceGammaRamp(Int32 hdc, void* ramp);

        [DllImport("gdi32.dll")]
        private unsafe static extern bool SetDeviceGammaRamp(Int32 hdc, ref RAMP lpRamp);

        [DllImport("gdi32.dll")]
        private unsafe static extern bool GetDeviceGammaRamp(Int32 hdc, ref RAMP lpRamp);


        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        public struct RAMP
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public UInt16[] Red;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public UInt16[] Green;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
            public UInt16[] Blue;
        }

        private sealed class Session : IDisposable
        {
            private bool _initialized;
            private IntPtr _hdc;
            private RAMP _originalRamp;

            public void EnsureInitialized()
            {
                if (_initialized) return;

                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                {
                    _hdc = g.GetHdc();
                    _originalRamp = new RAMP
                    {
                        Red = new ushort[256],
                        Green = new ushort[256],
                        Blue = new ushort[256]
                    };
                    GetDeviceGammaRamp(_hdc.ToInt32(), ref _originalRamp);
                }

                _initialized = true;
            }

            public bool RestoreBrightness()
            {
                if (!_initialized) return false;
                return SetDeviceGammaRamp(_hdc.ToInt32(), ref _originalRamp);
            }

            public unsafe bool SetBrightness(short brightness)
            {
                EnsureInitialized();

                if (brightness > 255) brightness = 255;
                if (brightness < 0) brightness = 0;

                short* gArray = stackalloc short[3 * 256];
                short* idx = gArray;

                for (int j = 0; j < 3; j++)
                {
                    for (int i = 0; i < 256; i++)
                    {
                        int arrayVal = i * (brightness + 128);
                        if (arrayVal > 65535) arrayVal = 65535;
                        *idx = (short)arrayVal;
                        idx++;
                    }
                }

                return SetDeviceGammaRamp(_hdc.ToInt32(), gArray);
            }

            public void Dispose()
            {
                // Nothing to dispose: Graphics is scoped within EnsureInitialized.
                _hdc = IntPtr.Zero;
            }
        }

        private static readonly object _lock = new object();
        private static Session _shared;

        private static Session Shared
        {
            get
            {
                lock (_lock)
                {
                    if (_shared == null)
                        _shared = new Session();
                    return _shared;
                }
            }
        }

        public static unsafe bool GetBrightness()
        {
            Shared.EnsureInitialized();

            RAMP r = new RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256]
            };
            GetDeviceGammaRamp(((Int32)typeof(Session).GetField("_hdc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(Shared)), ref r);
            var aaaa = Color.FromArgb(r.Red[1], r.Green[1], r.Blue[1]);
            return true;



            //if (brightness > 255)
            //    brightness = 255;

            //if (brightness < 0)
            //    brightness = 0;

            //short* gArray = stackalloc short[3 * 256];
            //short* idx = gArray;

            //for (int j = 0; j < 3; j++)
            //{
            //    for (int i = 0; i < 256; i++)
            //    {
            //        int arrayVal = i * (brightness + 128);

            //        if (arrayVal > 65535)
            //            arrayVal = 65535;

            //        *idx = (short)arrayVal;
            //        idx++;
            //    }
            //}

            //For some reason, this always returns false?
            //bool retVal = GetDeviceGammaRamp(hdc, gArray);

            //Memory allocated through stackalloc is automatically free'd
            //by the CLR.

            //return retVal;
        }

        public static unsafe bool RestoreBrightness()
        {
            return Shared.RestoreBrightness();
        }

        public static unsafe bool SetBrightness(short brightness)
        {
            return Shared.SetBrightness(brightness);
        }

        public static IDisposable BeginSession(out Func<short, bool> setBrightness, out Func<bool> restoreBrightness)
        {
            var s = new Session();
            s.EnsureInitialized();
            setBrightness = s.SetBrightness;
            restoreBrightness = s.RestoreBrightness;
            return s;
        }

    }
}
