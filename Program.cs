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
            // 若自身携带载荷（打包为可执行解密器），则进入解密模式；否则进入主界面。
            var exePath = Application.ExecutablePath;
            if (!string.IsNullOrWhiteSpace(exePath) && ExePayload.HasPayload(exePath))
            {
                Application.Run(new DecryptPayloadForm(exePath));
                return;
            }
            Application.Run(new WorkspaceForm());
        }
    }
}