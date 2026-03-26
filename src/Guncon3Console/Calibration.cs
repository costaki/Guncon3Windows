using System;
using System.IO;

namespace Guncon3Console
{
    /// <summary>
    /// Compatibility stub: real calibration is now handled by RectCalib + CalibrationWindow.
    /// Keeps the 'Calibration' API to avoid breaking older call sites.
    /// </summary>
    public static class Calibration
    {
        /// <summary>
        /// Compatibility flag: kept so older code does not break if it queries it. Not used.
        /// </summary>
        public static bool k_coefs_seted = false;

        /// <summary>
        /// Default path for the rectangular calibration file.
        /// </summary>
        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calibration_rect.txt");

        /// <summary>
        /// No-op. Kept for compatibility with older call sites.
        /// </summary>
        public static void Do_Calibration(ref short x, ref short y)
        {
            // Effective calibration is now handled by RectCalib.Map in Program.cs
            // Intentionally left blank for compatibility.
        }
    }
}
