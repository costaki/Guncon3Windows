using Guncon3Console.Calibration;
using Guncon3Console.Feeders;
using Guncon3Console.Mapping;
using GunconUSB;
using System;
using System.Collections.Generic;
using System.IO;

namespace Guncon3Console.GunStates
{
    internal sealed class GunState : IGunState
    {
        public enum Player
        {
            Player1,
            Player2
        }

        public Player PlayerNum { get; }

        public GunconDevice Device { get; }

        public IMouseFeeder MouseFeeder { get; }

        public IKeyboardFeeder KeyboardFeeder { get; }

        public string MappingPath { get; private set; }
        private GunMappingModel MappingModel { get; set; }

        public RectCalib Calibration { get; set; }
        public bool HasCalibration => Calibration != null;
        public bool CalibrationIsValid => Calibration != null && Calibration.IsValid();

        public Dictionary<GunButton, bool> BtnState { get; set; }

        public short ABS_X { get; set; }
        public short ABS_Y { get; set; }

        public bool ScreenIndicator { get; set; }

        public bool IsInsideScreen => !ScreenIndicator;

        public GunState(Player player, GunconDevice device, IMouseFeeder mouseFeeder = null, IKeyboardFeeder keyboardFeeder = null, RectCalib calibration = null )
        {
            PlayerNum = player;
            Device = device ?? throw new ArgumentNullException(nameof(device));
            Calibration = calibration;
            MouseFeeder = mouseFeeder;
            KeyboardFeeder = keyboardFeeder;

            var values = Enum.GetValues(typeof(GunButton));
            BtnState = new Dictionary<GunButton, bool>(values.Length);
            foreach (GunButton b in values)
                BtnState[b] = false;

            LoadMapping();
        }

        public void LoadNewCalibration(string path)
        {
            Calibration = RectCalib.Load(path);
        }

        public void RefreshCalibration()
        {
            if (Calibration != null)
                Calibration.Refresh();
        }

        public void LoadMapping()
        {
            if (!string.IsNullOrEmpty(MappingPath))
                LoadMapping(MappingPath);
            else
            {
                string fileName;
                if (PlayerNum == Player.Player1)
                    fileName = GunMappingStore.Player1FileName;
                else
                    fileName = GunMappingStore.Player2FileName;
                MappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                LoadMapping(MappingPath);
            }
        }

        public void LoadMapping(string path)
        {
            MappingPath = path;
            var model = GunMappingStore.Load(path);
            MappingModel = model;
            GunMappingStore.ApplyToFeeders(model, MouseFeeder, KeyboardFeeder);
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

        public void UpdateAndFeed()
        {
            Update();
            Feed();
        }
    }
}
