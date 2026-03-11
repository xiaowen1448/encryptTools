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
            // 主入口：工作区式主窗口（菜单 + 左侧工作区列表 + 右侧欢迎面板）。
            Application.Run(new WorkspaceForm());
        }
    }
}