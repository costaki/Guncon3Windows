using System;
using System.Globalization;
using System.IO;

namespace Guncon3Console
{
    using System.Collections.Generic;
    /// <summary>
    /// Minimal rectangular calibration: maps RAW_X/RAW_Y (gun raw range)
    /// into screen space (0..ScreenW-1, 0..ScreenH-1).
    /// </summary>
    public class RectCalib
    {
        public double RawMinX { get; set; }
        public double RawMaxX { get; set; }
        public double RawMinY { get; set; }
        public double RawMaxY { get; set; }
        public int ScreenW { get; set; }
        public int ScreenH { get; set; }
        public bool InvertY { get; set; }
        public string CalibrationFile { get; set; }

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

        /// <summary>Saves to calibration_rect.txt (next to the EXE by default).</summary>
        public void Save(string path = null)
        {
            if (path == null)
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, CalibrationFile ?? "calibration_rect.txt");

            using (var sw = new StreamWriter(path, false))
            {
                var ci = CultureInfo.InvariantCulture;

                sw.WriteLine("RawMinX=" + RawMinX.ToString("R", ci));
                sw.WriteLine("RawMaxX=" + RawMaxX.ToString("R", ci));
                sw.WriteLine("RawMinY=" + RawMinY.ToString("R", ci));
                sw.WriteLine("RawMaxY=" + RawMaxY.ToString("R", ci));
                sw.WriteLine("ScreenW=" + ScreenW.ToString(ci));
                sw.WriteLine("ScreenH=" + ScreenH.ToString(ci));
                sw.WriteLine("InvertY=" + (InvertY ? "1" : "0"));
            }
        }

        /// <summary>Loads from calibration_rect.txt. Returns null if missing or invalid.</summary>
        public static RectCalib Load(string path = null)
        {
            if (path == null)
                path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calibration_rect.txt");

            if (!File.Exists(path))
                return null;

            var rc = new RectCalib();
            var ci = CultureInfo.InvariantCulture;

            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine?.Trim();
                if (line.StartsWith("KEYBOARD")) continue; // Skip keyboard mappings
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim();
                var val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "RawMinX": if (double.TryParse(val, NumberStyles.Float, ci, out var rminx)) rc.RawMinX = rminx; break;
                    case "RawMaxX": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxx)) rc.RawMaxX = rmaxx; break;
                    case "RawMinY": if (double.TryParse(val, NumberStyles.Float, ci, out var rminy)) rc.RawMinY = rminy; break;
                    case "RawMaxY": if (double.TryParse(val, NumberStyles.Float, ci, out var rmaxy)) rc.RawMaxY = rmaxy; break;
                    case "ScreenW": if (int.TryParse(val, NumberStyles.Integer, ci, out var sw)) rc.ScreenW = sw; break;
                    case "ScreenH": if (int.TryParse(val, NumberStyles.Integer, ci, out var sh)) rc.ScreenH = sh; break;
                    case "InvertY": rc.InvertY = (val == "1" || val.Equals("true", StringComparison.OrdinalIgnoreCase)); break;
                }
            }

            // Add support for per-gun calibration files
            return rc.IsValid() ? rc : null;
        }
    }
}
