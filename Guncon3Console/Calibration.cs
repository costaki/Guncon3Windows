using System;
using System.IO;

namespace Guncon3Console
{
    /// <summary>
    /// Stub de compatibilidad: la calibración real la lleva ahora RectCalib + CalibrationWindow.
    /// Mantenemos la firma de 'Calibration' para no romper llamadas antiguas.
    /// </summary>
    public static class Calibration
    {
        /// <summary>
        /// Indicador compat: si alguien lo consulta no rompe. No lo usamos.
        /// </summary>
        public static bool k_coefs_seted = false;

        /// <summary>
        /// Ruta por defecto del archivo de calibración rectangular.
        /// </summary>
        public static string DefaultPath =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "calibration_rect.txt");

        /// <summary>
        /// No-op. Dejamos el método por compatibilidad con llamadas antiguas.
        /// </summary>
        public static void Do_Calibration(ref short x, ref short y)
        {
            // La calibración efectiva ahora se hace con RectCalib.Map en Program.cs
            // Dejamos esto vacío para compatibilidad.
        }
    }
}
