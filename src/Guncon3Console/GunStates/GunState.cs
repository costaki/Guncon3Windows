using System;
using System.Collections.Generic;
using GunconUSB;
using Guncon3Console.Calibration;
using Guncon3Console.Feeders;

namespace Guncon3Console.GunStates
{
    internal sealed class GunState : IGunState
    {
        public GunconDevice Device { get; }

        public IMouseFeeder MouseFeeder { get; set; }

        public IKeyboardFeeder KeyboardFeeder { get; set; }

        public RectCalib Calibration { get; set; }

        public Dictionary<GunButton, bool> BtnState { get; set; }

        public short ABS_X { get; set; }
        public short ABS_Y { get; set; }

        public bool ScreenIndicator { get; set; }

        public bool IsInsideScreen => !ScreenIndicator;

        public GunState(GunconDevice device, RectCalib calibration, IMouseFeeder mouseFeeder = null, IKeyboardFeeder keyboardFeeder = null)
        {
            Device = device ?? throw new ArgumentNullException(nameof(device));
            Calibration = calibration;
            MouseFeeder = mouseFeeder;
            KeyboardFeeder = keyboardFeeder;

            var values = Enum.GetValues(typeof(GunButton));
            BtnState = new Dictionary<GunButton, bool>(values.Length);
            foreach (GunButton b in values)
                BtnState[b] = false;
        }

        private void UpdateFromDevice()
        {
            Device.Read(out var btnState, out short absX, out short absY, out bool screenIndicator);
            BtnState = btnState;
            ABS_X = absX;
            ABS_Y = absY;
            ScreenIndicator = screenIndicator;
        }

        private void ApplyCalibration()
        {
            var (calX, calY) = Calibration.ApplyCalibration(ABS_X, ABS_Y);
            ABS_X = calX;
            ABS_Y = calY;
        }

        public void Update()
        {
            UpdateFromDevice();
            ApplyCalibration();
        }

        public void Feed()
        {
            MouseFeeder?.Feed(this);
            KeyboardFeeder?.Feed(this);
        }
    }
}
