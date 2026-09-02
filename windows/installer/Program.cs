using System;
using System.Threading;
using System.Windows.Forms;

namespace IntelByteSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
            {
                MessageBox.Show(
                    e.Exception != null ? e.Exception.Message : "Unknown error",
                    "IntelByte Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                var ex = e.ExceptionObject as Exception;
                MessageBox.Show(
                    ex != null ? ex.Message : "Unknown fatal error",
                    "IntelByte Setup",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new InstallerForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "IntelByte Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
