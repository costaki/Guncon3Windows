using System;
using System.IO;
using System.IO.Pipes;
using System.Windows.Forms;
using GunconUSB; // para usar GunconReader / GunState que acabas de añadir

namespace Guncon3Calibration
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Creamos la ventana de calibración
            var form = new Form1();

            if (args != null && args.Length > 0)
            {
                // === MODO PIPE (como hasta ahora) ===
                form.SetPipeHandle(args[0]); // esto habilita el TargetNext() por pipe
                Application.Run(form);
                return;
            }

            // === MODO STANDALONE (sin argumentos) ===
            // 1) Arrancar el lector de la pistola
            GunconReader.ProgressChanged += (s, e) =>
            {
                // Cada vez que llega un paquete, avisamos al formulario
                form.OnGunReport(GunState.PointerX, GunState.PointerY, GunState.Trigger);
            };

            // 2) Arrancar el lector y abrir la ventana
            try
            {
                GunconReader.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo iniciar la lectura de la GunCon3.\n" + ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            Application.Run(form);

            // 3) Al cerrar la ventana, paramos
            try { GunconReader.Stop(); } catch { }
        }
    }
}

