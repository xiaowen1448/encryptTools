using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

namespace EncryptTools
{
    /// <summary>
    /// 在 net48 进程内无法调用 AesGcm 时，通过 dotnet 运行同目录下的 EncryptTools.GcmCli.dll 执行 GCM 加密/解密。
    /// 若程序目录下存在 EncryptTools.GcmCli.dll 且本机已安装 .NET 8，则可用；否则返回 false。
    /// </summary>
    internal static class GcmRunner
    {
        private const string DllName = "EncryptTools.GcmCli.dll";
        private const string ConfigName = "EncryptTools.GcmCli.runtimeconfig.json";

        /// <summary>
        /// 获取 GcmCli 所在目录：优先程序同目录，其次从嵌入资源解压到临时目录。
        /// </summary>
        private static string GetGcmCliDir()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string dllPath = Path.Combine(baseDir, DllName);
            if (File.Exists(dllPath))
                return baseDir;

            string tempDir = Path.Combine(Path.GetTempPath(), "encryptTools_gcm_" + Guid.NewGuid().ToString("N"));
            try
            {
                ExtractEmbeddedGcmCli(tempDir);
                if (File.Exists(Path.Combine(tempDir, DllName)))
                    return tempDir;
            }
            catch { }
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            return null;
        }

        private static void ExtractEmbeddedGcmCli(string targetDir)
        {
            Directory.CreateDirectory(targetDir);
            Assembly asm = Assembly.GetExecutingAssembly();
            string[] names = asm.GetManifestResourceNames();
            foreach (string name in names)
            {
                string outFileName = null;
                if (name.IndexOf(DllName, StringComparison.OrdinalIgnoreCase) >= 0) outFileName = DllName;
                else if (name.IndexOf(ConfigName, StringComparison.OrdinalIgnoreCase) >= 0) outFileName = ConfigName;
                if (string.IsNullOrEmpty(outFileName)) continue;
                string outPath = Path.Combine(targetDir, outFileName);
                using (Stream s = asm.GetManifestResourceStream(name))
                {
                    if (s == null) continue;
                    using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None))
                        s.CopyTo(fs);
                }
            }
        }

        /// <summary>
        /// 使用 GcmCli 加密。返回 true 表示成功，false 表示未找到或执行失败。
        /// </summary>
        public static async Task<bool> EncryptAsync(string inputPath, string outputPath, string password, Action<string> log = null)
        {
            string cliDir = GetGcmCliDir();
            if (string.IsNullOrEmpty(cliDir))
                return false;
            string pwdFile = Path.Combine(Path.GetTempPath(), "encryptTools_pwd_" + Guid.NewGuid().ToString("N") + ".tmp");
            string dllPath = Path.Combine(cliDir, DllName);
            bool isTempDir = cliDir.IndexOf(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                File.WriteAllText(pwdFile, password ?? "");
                var psi = new ProcessStartInfo("dotnet")
                {
                    Arguments = "\"" + dllPath + "\" --encrypt --input \"" + inputPath + "\" --output \"" + outputPath + "\" --password-file \"" + pwdFile + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = cliDir
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    await Task.Run(() => p.WaitForExit(120000)).ConfigureAwait(false);
                    if (p.ExitCode != 0)
                        log?.Invoke("GCM 加密失败，退出码: " + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("GCM 执行异常: " + ex.Message);
                return false;
            }
            finally
            {
                try { if (File.Exists(pwdFile)) File.Delete(pwdFile); } catch { }
                if (isTempDir) try { Directory.Delete(cliDir, true); } catch { }
            }
        }

        /// <summary>
        /// 使用 GcmCli 解密。返回 true 表示成功。
        /// </summary>
        public static async Task<bool> DecryptAsync(string inputPath, string outputPath, string password, Action<string> log = null)
        {
            string cliDir = GetGcmCliDir();
            if (string.IsNullOrEmpty(cliDir))
                return false;
            string pwdFile = Path.Combine(Path.GetTempPath(), "encryptTools_pwd_" + Guid.NewGuid().ToString("N") + ".tmp");
            string dllPath = Path.Combine(cliDir, DllName);
            bool isTempDir = cliDir.IndexOf(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                File.WriteAllText(pwdFile, password ?? "");
                var psi = new ProcessStartInfo("dotnet")
                {
                    Arguments = "\"" + dllPath + "\" --decrypt --input \"" + inputPath + "\" --output \"" + outputPath + "\" --password-file \"" + pwdFile + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = cliDir
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    await Task.Run(() => p.WaitForExit(120000)).ConfigureAwait(false);
                    if (p.ExitCode != 0)
                        log?.Invoke("GCM 解密失败，退出码: " + p.ExitCode);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke("GCM 执行异常: " + ex.Message);
                return false;
            }
            finally
            {
                try { if (File.Exists(pwdFile)) File.Delete(pwdFile); } catch { }
                if (isTempDir) try { Directory.Delete(cliDir, true); } catch { }
            }
        }
    }
}
