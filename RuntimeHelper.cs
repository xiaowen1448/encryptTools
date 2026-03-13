using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace EncryptTools
{
    /// <summary>
    /// 检测当前运行时及本机是否安装 .NET 8，以决定是否显示/允许使用 AES-GCM。
    /// </summary>
    internal static class RuntimeHelper
    {
        private static bool? _isNet8OrHigher;
        private static bool? _isNet8InstalledOnMachine;

        /// <summary>
        /// 当前进程运行时是否为 .NET 8 或更高（.NET 8 才支持 AES-GCM 执行）。
        /// </summary>
        public static bool IsNet8OrHigher
        {
            get
            {
                if (_isNet8OrHigher.HasValue)
                    return _isNet8OrHigher.Value;
                var desc = RuntimeInformation.FrameworkDescription ?? "";
                bool isNetFramework = desc.IndexOf(".NET Framework", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isNetFramework)
                {
                    _isNet8OrHigher = false;
                    return false;
                }
                if (Environment.Version.Major >= 8)
                {
                    _isNet8OrHigher = true;
                    return true;
                }
                _isNet8OrHigher = desc.IndexOf(".NET 8", StringComparison.OrdinalIgnoreCase) >= 0
                    || desc.IndexOf(".NET 9", StringComparison.OrdinalIgnoreCase) >= 0;
                return _isNet8OrHigher.Value;
            }
        }

        /// <summary>
        /// 本机是否已安装 .NET 8 运行时（用于在 .NET 4.6 程序里决定是否显示 AES-GCM 选项）。
        /// </summary>
        public static bool IsNet8InstalledOnMachine
        {
            get
            {
                if (_isNet8InstalledOnMachine.HasValue)
                    return _isNet8InstalledOnMachine.Value;
                try
                {
                    var psi = new ProcessStartInfo("dotnet")
                    {
                        Arguments = "--list-runtimes",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };
                    using (var p = Process.Start(psi))
                    {
                        if (p == null) { _isNet8InstalledOnMachine = false; return false; }
                        var output = p.StandardOutput.ReadToEnd();
                        p.WaitForExit(5000);
                        // 输出行示例: Microsoft.WindowsDesktop.App 8.0.x [...] 或 Microsoft.NETCore.App 8.0.x
                        _isNet8InstalledOnMachine = output != null && (
                            output.IndexOf(" 8.0.", StringComparison.Ordinal) >= 0 ||
                            output.IndexOf(" 8.0 ", StringComparison.Ordinal) >= 0);
                    }
                }
                catch
                {
                    _isNet8InstalledOnMachine = false;
                }
                return _isNet8InstalledOnMachine ?? false;
            }
        }

        /// <summary>
        /// 若当前不支持 AES-GCM 且需提示用户时使用（例如解密到 GCM 文件但本机无 .NET 8）。
        /// </summary>
        public static string GetAesGcmRequirementMessage()
        {
            return "该文件为 AES-GCM 加密。本机未检测到 .NET 8，无法解密。\n\n"
                + "请安装 .NET 8 桌面运行时后重试：\n"
                + "https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0";
        }
    }
}
