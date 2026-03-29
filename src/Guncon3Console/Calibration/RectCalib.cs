using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;


namespace Guncon3Console.Calibration
{
    /// <summary>
    /// Minimal rectangular calibration: maps RAW_X/RAW_Y (gun raw range)
    /// into screen space (0..ScreenW-1, 0..ScreenH-1).
    /// </summary>
    [DataContract]
    public class RectCalib
    {
        public const string DefaultFileName = "calibration_rect.json";
        public const string Player1FileName = "calibration_rect_p1.json";
        public const string Player2FileName = "calibration_rect_p2.json";

        [IgnoreDataMember]
        public string CalibrationPath { get; private set; }

        [DataMember(Order = 1)]
        public double RawMinX { get; set; }
        [DataMember(Order = 2)]
        public double RawMaxX { get; set; }
        [DataMember(Order = 3)]
        public double RawMinY { get; set; }
        [DataMember(Order = 4)]
        public double RawMaxY { get; set; }
        [DataMember(Order = 5)]
        public int ScreenW { get; set; }
        [DataMember(Order = 6)]
        public int ScreenH { get; set; }
        [DataMember(Order = 7)]
        public bool InvertY { get; set; }

        public RectCalib(string calibrationPath)
        {
            if (string.IsNullOrWhiteSpace(calibrationPath))
                throw new ArgumentException("Calibration path is required.", nameof(calibrationPath));
            CalibrationPath = calibrationPath;
        }

        private RectCalib()
        {
        }

        /// <summary>Has valid ranges and a valid screen size?</summary>
        public bool IsValid()
        {
            return RawMaxX > RawMinX &&
                   RawMaxY > RawMinY &&
                   ScreenW > 0 && ScreenH > 0;
        }

        /// <summary>Maps a RAW point into screen space.</summary>
        public (double X, double Y) Map(double rawX, double rawY)
        {
            if (!IsValid())
                return (0, 0);

            double nx = (rawX - RawMinX) / (RawMaxX - RawMinX);
            double ny = (rawY - RawMinY) / (RawMaxY - RawMinY);

            // clamp
            if (nx < 0) nx = 0; if (nx > 1) nx = 1;
            if (ny < 0) ny = 0; if (ny > 1) ny = 1;

            if (InvertY) ny = 1.0 - ny;

            double sx = nx * (ScreenW - 1);
            double sy = ny * (ScreenH - 1);
            return (sx, sy);
        }

        public void Save()
        {
            var ser = new DataContractJsonSerializer(typeof(RectCalib));
            using (var fs = File.Create(CalibrationPath))
                ser.WriteObject(fs, this);
        }

        /// <summary>Loads from the given path. Returns null if missing or invalid.</summary>
        public static RectCalib Load(string calibrationPath)
        {
            if (string.IsNullOrWhiteSpace(calibrationPath))
                throw new ArgumentException("Calibration path is required.", nameof(calibrationPath));

            if (!File.Exists(calibrationPath))
                return null;

            try
            {
                var ser = new DataContractJsonSerializer(typeof(RectCalib));
                using (var fs = File.OpenRead(calibrationPath))
                {
                    var rc = (RectCalib)ser.ReadObject(fs);
                    if (rc == null || !rc.IsValid())
                        return null;
                    rc.CalibrationPath = calibrationPath;
                    return rc;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
