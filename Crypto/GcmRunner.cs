using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
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
        /// 使用 GcmCli 解密 .pwd 文件（GCM 格式）。仅在本机已安装 .NET 8 且存在 GcmCli 时可用。返回密码或 null。
        /// </summary>
        public static string DecryptPasswordFile(string pwdFilePath)
        {
            if (string.IsNullOrEmpty(pwdFilePath) || !File.Exists(pwdFilePath))
                return null;
            string cliDir = GetGcmCliDir();
            if (string.IsNullOrEmpty(cliDir))
                return null;
            string dllPath = Path.Combine(cliDir, DllName);
            try
            {
                var psi = new ProcessStartInfo("dotnet")
                {
                    Arguments = "\"" + dllPath + "\" --decrypt-pwd --input \"" + pwdFilePath + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = cliDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return null;
                    string stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(10000);
                    return p.ExitCode == 0 ? stdout?.Trim() : null;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 使用 GcmCli 生成旧版 .pwd（无格式字节的 AES-GCM）：Key(32)+Nonce(12)+Tag(16)+Ciphertext。
        /// 仅在本机已安装 .NET 8 且存在 GcmCli 时可用。
        /// </summary>
        public static bool EncryptPasswordFile(string pwdFilePath, string password)
        {
            if (string.IsNullOrEmpty(pwdFilePath)) return false;
            string cliDir = GetGcmCliDir();
            if (string.IsNullOrEmpty(cliDir)) return false;
            string dllPath = Path.Combine(cliDir, DllName);
            string pwdTemp = Path.Combine(Path.GetTempPath(), "encryptTools_pwd_" + Guid.NewGuid().ToString("N") + ".tmp");
            bool isTempDir = cliDir.IndexOf(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                File.WriteAllText(pwdTemp, password ?? "");
                var psi = new ProcessStartInfo("dotnet")
                {
                    Arguments = "\"" + dllPath + "\" --encrypt-pwd --output \"" + pwdFilePath + "\" --password-file \"" + pwdTemp + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = cliDir,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    p.WaitForExit(15000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                try { if (File.Exists(pwdTemp)) File.Delete(pwdTemp); } catch { }
                if (isTempDir) try { Directory.Delete(cliDir, true); } catch { }
            }
        }

        /// <summary>
        /// 使用 GcmCli 加密。返回 true 表示成功，false 表示未找到或执行失败。
        /// 若传入 progress，则按输出文件大小轮询上报进度（GCM 子进程无回调，用输出文件增长模拟）。
        /// <param name="passwordFileHash">与 CryptoService v2 一致：所选 .pwd 文件的 SHA256 原始字节；无密码文件绑定则 null。</param>
        /// </summary>
        public static async Task<bool> EncryptAsync(string inputPath, string outputPath, string password, IProgress<double> progress = null, Action<string> log = null, CancellationToken ct = default, byte[] passwordFileHash = null)
        {
            string cliDir = GetGcmCliDir();
            if (string.IsNullOrEmpty(cliDir))
                return false;
            string pwdFile = Path.Combine(Path.GetTempPath(), "encryptTools_pwd_" + Guid.NewGuid().ToString("N") + ".tmp");
            string pwdHashFile = null;
            string dllPath = Path.Combine(cliDir, DllName);
            bool isTempDir = cliDir.IndexOf(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) >= 0;
            try
            {
                File.WriteAllText(pwdFile, password ?? "");
                if (passwordFileHash != null && passwordFileHash.Length > 0)
                {
                    pwdHashFile = Path.Combine(Path.GetTempPath(), "encryptTools_pwdhash_" + Guid.NewGuid().ToString("N") + ".bin");
                    File.WriteAllBytes(pwdHashFile, passwordFileHash);
                }
                string argHash = string.IsNullOrEmpty(pwdHashFile) ? "" : (" --pwd-hash-file \"" + pwdHashFile + "\"");
                var psi = new ProcessStartInfo("dotnet")
                {
                    Arguments = "\"" + dllPath + "\" --encrypt --input \"" + inputPath + "\" --output \"" + outputPath + "\" --password-file \"" + pwdFile + "\"" + argHash,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = cliDir
                };
                using (var p = Process.Start(psi))
                {
                    if (p == null) return false;
                    if (progress != null)
                    {
                        long inputLen = new FileInfo(inputPath).Length;
                        long estimatedOut = inputLen + 1024 + (int)((inputLen / (4 * 1024 * 1024L) + 1) * 16);
                        while (!p.HasExited)
                        {
                            try
                            {
                                if (File.Exists(outputPath))
                                {
                                    long cur = new FileInfo(outputPath).Length;
                                    progress.Report(Math.Min(1.0, (double)cur / Math.Max(1, estimatedOut)));
                                }
                            }
                            catch { }
                            try { await Task.Delay(80, ct).ConfigureAwait(false); } catch (OperationCanceledException) { }
                        }
                        progress.Report(1.0);
                    }
                    else
                        await Task.Run(() => p.WaitForExit(120000), ct).ConfigureAwait(false);
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
                try { if (!string.IsNullOrEmpty(pwdHashFile) && File.Exists(pwdHashFile)) File.Delete(pwdHashFile); } catch { }
                if (isTempDir) try { Directory.Delete(cliDir, true); } catch { }
            }
        }

        /// <summary>
        /// 使用 GcmCli 解密。返回 true 表示成功。
        /// 若传入 progress，则按输出文件大小轮询上报进度（GCM 子进程无回调，用输出文件增长模拟）。
        /// </summary>
        public static async Task<bool> DecryptAsync(string inputPath, string outputPath, string password, IProgress<double> progress = null, Action<string> log = null, CancellationToken ct = default)
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
                    if (progress != null)
                    {
                        long inputLen = new FileInfo(inputPath).Length;
                        while (!p.HasExited)
                        {
                            try
                            {
                                if (File.Exists(outputPath))
                                {
                                    long cur = new FileInfo(outputPath).Length;
                                    progress.Report(Math.Min(1.0, (double)cur / Math.Max(1, inputLen)));
                                }
                            }
                            catch { }
                            try { await Task.Delay(80, ct).ConfigureAwait(false); } catch (OperationCanceledException) { }
                        }
                        progress.Report(1.0);
                    }
                    else
                        await Task.Run(() => p.WaitForExit(120000), ct).ConfigureAwait(false);
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
