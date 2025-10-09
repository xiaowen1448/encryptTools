using System;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace EncryptTools
{
    internal static class Program
    {
        [STAThread]
        [SupportedOSPlatform("windows6.1")]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}