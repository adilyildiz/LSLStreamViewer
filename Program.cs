using System;
using System.Windows.Forms;

namespace LSLStreamViewer
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            NativeLibraryLoader.Initialize();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.Run(new MainForm());
        }
    }
}
