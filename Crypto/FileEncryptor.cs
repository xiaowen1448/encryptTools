using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace EncryptTools
{
    public class FileEncryptorOptions
    {
        public required string SourcePath { get; set; }
        public required string OutputRoot { get; set; }
        public bool InPlace { get; set; }
        public bool Recursive { get; set; }
        public bool RandomizeFileName { get; set; }
        public int RandomFileNameLength { get; set; } = 36;  // ← 新增：可配置长度

        // 说明：随机文件名长度
        // 默认值：16
        // 类型：int
        // 有效范围：1-255 (Windows 文件名限制)
        // 推荐值：
        // 8 - 短名，低安全性
        // 16 - 中等，推荐默认
        // 24 - 较长，高安全性
        // 32 - 完整GUID十六进制  
        //36-完整GUID格式（含连字符）✅ 可以用
        //64- 超长名气，最高安全性    
        public string RandomFileNameFormat { get; set; } = "hex";  // ← 新增：格式选择 (hex/alphanumeric/guid)
        public CryptoAlgorithm Algorithm { get; set; }
        public required string Password { get; set; }
        public int Iterations { get; set; } = 200_000;
        public int AesKeySizeBits { get; set; } = 256;
        public required Action<string> Log { get; set; }
        /// <summary>自定义加密后缀名（例如 .enc1 / .enc2），若为空则根据算法自动推导。</summary>
        public string? EncryptedExtension { get; set; }
    }

    public class FileEncryptor
    {
        private readonly FileEncryptorOptions _options;
        private readonly CryptoService _crypto;
        private static readonly string[] KnownEncryptedExtensions = new[] { ".enc1", ".enc2" };

        public FileEncryptor(FileEncryptorOptions options)
        {
            _options = options;
            _crypto = new CryptoService();
        }

        public async Task EncryptAsync(IProgress<double> progress, CancellationToken ct)
        {
            var files = CollectFiles(_options.SourcePath, _options.Recursive);
            long totalBytes = files.Sum(f => new FileInfo(f).Length);
            long processed = 0;

            // 单 exe 兼容：选 GCM 时，未装 .NET 8 自动用 CBC；已装 .NET 8 则用 GcmRunner（或本进程 net8）
            CryptoAlgorithm effectiveAlgo = _options.Algorithm;
            bool useGcmRunner = false;
            if (_options.Algorithm == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher)
            {
                if (RuntimeHelper.IsNet8InstalledOnMachine)
                    useGcmRunner = true;
                else
                {
                    effectiveAlgo = CryptoAlgorithm.AesCbc;
                    _options.Log?.Invoke("本机未安装 .NET 8，已自动使用 AES-128-CBC。");
                }
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (CryptoService.IsWxEncryptedFile(file))
                {
                    _options.Log?.Invoke($"已加密，跳过: {file}");
                    continue;
                }
                var outFile = GetOutputFilePath(file, encrypt: true);
                if (!_options.InPlace && File.Exists(outFile))
                {
                    _options.Log?.Invoke($"已加密，跳过: {file}");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                _options.Log?.Invoke($"加密: {file} -> {outFile}");
                try
                {
                    if (useGcmRunner)
                    {
                        long currentFileLen = new FileInfo(file).Length;
                        IProgress<double> gcmProgress = progress == null ? null : new GcmToOverallProgress(progress, () => processed, totalBytes, currentFileLen);
                        bool ok = await GcmRunner.EncryptAsync(file, outFile, _options.Password, gcmProgress, _options.Log, ct).ConfigureAwait(false);
                        if (!ok)
                        {
                            _options.Log?.Invoke($"加密失败，跳过: {file}（GCM 执行失败，可改用 AES-128-CBC）");
                            continue;
                        }
                        processed += currentFileLen;
                        progress?.Report(totalBytes == 0 ? 1.0 : (double)processed / totalBytes);
                    }
                    else
                    {
                        int lastPct = -1;
                        await _crypto.EncryptFileAsync(file, outFile, effectiveAlgo, _options.Password, _options.Iterations, _options.AesKeySizeBits, new Progress<long>(bytes =>
                        {
                            processed += bytes;
                            int pct = totalBytes == 0 ? 0 : Math.Min(100, (int)((double)processed / totalBytes * 100));
                            if (pct != lastPct) { lastPct = pct; progress?.Report(pct / 100.0); }
                        }), ct);
                    }
                }
                catch (IOException ioEx) when (IsSharingViolation(ioEx))
                {
                    if (TryForceUnlockFile(file, ioEx))
                    {
                        if (useGcmRunner)
                        {
                            long retryFileLen = new FileInfo(file).Length;
                            IProgress<double> gcmProgressRetry = progress == null ? null : new GcmToOverallProgress(progress, () => processed, totalBytes, retryFileLen);
                            bool ok = await GcmRunner.EncryptAsync(file, outFile, _options.Password, gcmProgressRetry, _options.Log, ct).ConfigureAwait(false);
                            if (!ok) { _options.Log?.Invoke($"仍被占用或失败，跳过: {file}"); continue; }
                            processed += retryFileLen;
                            progress?.Report(totalBytes == 0 ? 1.0 : (double)processed / totalBytes);
                        }
                        else
                        {
                            int lastPctRetry = -1;
                            await _crypto.EncryptFileAsync(file, outFile, effectiveAlgo, _options.Password, _options.Iterations, _options.AesKeySizeBits, new Progress<long>(bytes =>
                            {
                                processed += bytes;
                                int pct = totalBytes == 0 ? 0 : Math.Min(100, (int)((double)processed / totalBytes * 100));
                                if (pct != lastPctRetry) { lastPctRetry = pct; progress?.Report(pct / 100.0); }
                            }), ct);
                        }
                    }
                    else
                    {
                        _options.Log?.Invoke($"仍被占用，跳过: {file}");
                        continue;
                    }
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    _options.Log?.Invoke($"无权限访问，跳过: {file} - {uaEx.Message}");
                    continue;
                }
                catch (Exception ex)
                {
                    _options.Log?.Invoke($"加密失败，跳过: {file} - {ex.Message}");
                    continue;
                }

                if (_options.InPlace)
                {
                    try
                    {
                        TryDeleteSourceFileWithForce(file);
                    }
                    catch (Exception ex)
                    {
                        _options.Log?.Invoke($"删除源文件失败: {file} - {ex.Message}");
                    }
                }
            }
        }

        public async Task DecryptAsync(IProgress<double> progress, CancellationToken ct)
        {
            var files = CollectFiles(_options.SourcePath, _options.Recursive)
                .Where(f => CryptoService.IsWxEncryptedFile(f))
                .ToList();

            long totalBytes = files.Sum(f => new FileInfo(f).Length);
            long processed = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    CryptoService.PeekEncryptedFileInfo(file);
                }
                catch (InvalidDataException)
                {
                    _options.Log?.Invoke($"跳过（不是有效加密文件）: {file}");
                    continue;
                }
                catch (Exception)
                {
                    _options.Log?.Invoke($"跳过（文件未加密或无法解密）: {file}");
                    continue;
                }

                var outFile = GetOutputFilePath(file, encrypt: false);
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                _options.Log?.Invoke($"解密: {file} -> {outFile}");
                CryptoService.DecryptResult? result = null;
                string? peekedOriginalName = null;
                bool usedGcmRunner = false;

                try
                {
                    CryptoAlgorithm peekAlg = CryptoAlgorithm.AesCbc;
                    try
                    {
                        var (alg, origName) = CryptoService.PeekEncryptedFileInfo(file);
                        peekAlg = alg;
                        peekedOriginalName = origName;
                    }
                    catch { }

                    if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && RuntimeHelper.IsNet8InstalledOnMachine)
                    {
                        long decFileLen = new FileInfo(file).Length;
                        IProgress<double> gcmDecProgress = progress == null ? null : new GcmToOverallProgress(progress, () => processed, totalBytes, decFileLen);
                        bool ok = await GcmRunner.DecryptAsync(file, outFile, _options.Password, gcmDecProgress, _options.Log, ct).ConfigureAwait(false);
                        if (ok)
                        {
                            result = new CryptoService.DecryptResult { OriginalFileName = peekedOriginalName };
                            usedGcmRunner = true;
                            processed += decFileLen;
                        }
                        else
                        {
                            _options.Log?.Invoke($"解密失败，跳过: {file}（GCM 执行失败）");
                            continue;
                        }
                    }
                    else if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && !RuntimeHelper.IsNet8InstalledOnMachine)
                    {
                        _options.Log?.Invoke($"跳过: {file} - 该文件为 AES-GCM 加密，本机未安装 .NET 8 无法解密。");
                        continue;
                    }

                    if (!usedGcmRunner)
                    {
                        int lastPct = -1;
                        result = await _crypto.DecryptFileAsync(file, outFile, _options.Password, new Progress<long>(bytes =>
                        {
                            processed += bytes;
                            int pct = totalBytes == 0 ? 0 : Math.Min(100, (int)((double)processed / totalBytes * 100));
                            if (pct != lastPct) { lastPct = pct; progress?.Report(pct / 100.0); }
                        }), ct);
                    }
                }
                catch (IOException ioEx) when (IsSharingViolation(ioEx))
                {
                    if (TryForceUnlockFile(file, ioEx))
                    {
                        int lastPctRetry = -1;
                        result = await _crypto.DecryptFileAsync(file, outFile, _options.Password, new Progress<long>(bytes =>
                        {
                            processed += bytes;
                            int pct = totalBytes == 0 ? 0 : Math.Min(100, (int)((double)processed / totalBytes * 100));
                            if (pct != lastPctRetry) { lastPctRetry = pct; progress?.Report(pct / 100.0); }
                        }), ct);
                    }
                    else
                    {
                        _options.Log?.Invoke($"仍被占用，跳过: {file}");
                        continue;
                    }
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    _options.Log?.Invoke($"无权限访问，跳过: {file} - {uaEx.Message}");
                    continue;
                }
                catch (Exception ex)
                {
                    _options.Log?.Invoke($"解密失败，跳过: {file} - {ex.Message}");
                    continue;
                }

                // 自动还原原始文件名
                if (!string.IsNullOrWhiteSpace(result?.OriginalFileName))
                {
                    try
                    {
                        var targetDir = Path.GetDirectoryName(outFile)!;
                        var restoredName = SanitizeFileName(result!.OriginalFileName);
                        if (!string.Equals(restoredName, result!.OriginalFileName, StringComparison.Ordinal))
                        {
                            _options.Log?.Invoke($"原始文件名包含空格或特殊字符，已更正为: {restoredName}");
                        }
                        var desired = Path.Combine(targetDir, restoredName);
                        // 如果未勾选随机文件名，直接覆盖原文件（如有重名则覆盖）
                        if (outFile != desired)
                        {
                            if (File.Exists(desired))
                            {
                                File.Delete(desired);
                            }
                            File.Move(outFile, desired);
                            _options.Log?.Invoke($"已恢复原始文件名: {Path.GetFileName(desired)}");
                            outFile = desired;
                            _options.Log?.Invoke($"最终输出文件: {desired}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _options.Log?.Invoke($"恢复原始文件名失败: {ex.Message}");
                    }
                }

                if (_options.InPlace)
                {
                    try
                    {
                        if (File.Exists(outFile) && new FileInfo(outFile).Length > 0)
                        {
                            TryDeleteSourceFileWithForce(file, isEncryptedSource: true);
                        }
                        else
                        {
                            _options.Log?.Invoke($"解密输出文件缺失或为空，已保留源加密文件: {file}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _options.Log?.Invoke($"删除源加密文件失败: {file} - {ex.Message}");
                    }
                }
                else
                {
                    try
                    {
                        if (File.Exists(outFile) && new FileInfo(outFile).Length > 0 && File.Exists(file))
                        {
                            TryDeleteSourceFileWithForce(file, isEncryptedSource: true);
                        }
                    }
                    catch (Exception ex)
                    {
                        _options.Log?.Invoke($"删除源加密文件失败: {file} - {ex.Message}");
                    }
                }
            }
        }

        private List<string> CollectFiles(string path, bool recursive)
        {
            var files = new List<string>();
            if (File.Exists(path))
            {
                files.Add(path);
                return files;
            }

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            files.AddRange(Directory.EnumerateFiles(path, "*", option));
            return files;
        }

        private string GetOutputFilePath(string source, bool encrypt)
        {
            if (_options.InPlace)
            {
                if (encrypt)
                {
                    var dir = Path.GetDirectoryName(source)!;
                    var ext = string.IsNullOrWhiteSpace(_options.EncryptedExtension)
                        ? GetEncryptedExtension(_options.Algorithm)
                        : _options.EncryptedExtension!;
                    // ← 使用可配置的长度和格式
                    var name = _options.RandomizeFileName 
                        ? GenerateRandomName(_options.RandomFileNameLength, _options.RandomFileNameFormat)
                        : Path.GetFileName(source);
                    return Path.Combine(dir, name + ext);
                }
                else
                {
                    return DeriveDecryptedName(source);
                }
            }
            else
            {
                var root = _options.OutputRoot ?? Path.Combine(Path.GetDirectoryName(source)!, "output");
                var relative = MakeRelativeToRoot(source);
                var targetDir = Path.Combine(root, Path.GetDirectoryName(relative) ?? string.Empty);
                var fileName = Path.GetFileName(source);
                var ext = string.IsNullOrWhiteSpace(_options.EncryptedExtension)
                    ? GetEncryptedExtension(_options.Algorithm)
                    : _options.EncryptedExtension!;
                var outName = encrypt
                    ? (_options.RandomizeFileName 
                        ? GenerateRandomName(_options.RandomFileNameLength, _options.RandomFileNameFormat)
                        : fileName) + ext
                    : DeriveDecryptedName(fileName);
                return Path.Combine(targetDir, outName);
            }
        }

        private string MakeRelativeToRoot(string path)
        {
            if (File.Exists(_options.SourcePath))
            {
                return Path.GetFileName(path);
            }
            var root = Path.GetFullPath(_options.SourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(path);
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                return full.Substring(root.Length);
            }
            return Path.GetFileName(path);
        }

        private string DeriveDecryptedName(string encryptedName)
        {
            if (!string.IsNullOrWhiteSpace(_options.EncryptedExtension))
            {
                var ext = _options.EncryptedExtension!.Trim();
                if (ext.Length > 0 && encryptedName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return encryptedName.Substring(0, encryptedName.Length - ext.Length);
            }
            if (encryptedName.EndsWith(".enc2", StringComparison.OrdinalIgnoreCase))
                return encryptedName.Substring(0, encryptedName.Length - 5);
            if (encryptedName.EndsWith(".enc1", StringComparison.OrdinalIgnoreCase))
                return encryptedName.Substring(0, encryptedName.Length - 5);
            return encryptedName;
        }

        private static string GetEncryptedExtension(CryptoAlgorithm alg)
        {
            return alg == CryptoAlgorithm.AesGcm ? ".enc2" : ".enc1";
        }

        // ====== 改进的随机文件名生成方法 ======
        private static string GenerateRandomName(int length, string format = "hex")
        {
            // 验证长度
            if (length <= 0) length = 16; // 默认长度

            // 支持特殊长度
            switch (length)
            {
                case 8:
                    // 短名，低安全性
                    return format.ToLower() == "alphanumeric" ? GenerateAlphanumericName(8) : GenerateHexName(8);
                case 16:
                    // 推荐默认
                    return format.ToLower() == "alphanumeric" ? GenerateAlphanumericName(16) : GenerateHexName(16);
                case 24:
                    // 高安全性
                    return format.ToLower() == "alphanumeric" ? GenerateAlphanumericName(24) : GenerateHexName(24);
                case 32:
                    // 完整GUID十六进制
                    return GenerateHexName(32);
                case 36:
                    // 完整GUID格式（含连字符）
                    return Guid.NewGuid().ToString();
                case 64:
                    // 超长名气，最高安全性
                    return format.ToLower() == "alphanumeric" ? GenerateAlphanumericName(64) : GenerateHexName(64);
                default:
                    // 其它长度按格式生成
                    if (format.ToLower() == "guid" && length >= 36)
                        return Guid.NewGuid().ToString();
                    return format.ToLower() == "alphanumeric" ? GenerateAlphanumericName(length) : GenerateHexName(length);
            }
        }

        /// <summary>
        /// 生成十六进制随机名称（最安全）
        /// 字符集：0-9, a-f
        /// </summary>
        private static string GenerateHexName(int length)
        {
            // 生成任意长度的十六进制字符串
            var bytesLen = (length + 1) / 2; // 每字节2位hex
            var bytes = new byte[bytesLen];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            var hex = BitConverter.ToString(bytes).Replace("-", string.Empty);
            return hex.Length > length ? hex.Substring(0, length) : hex;
        }

        /// <summary>
        /// 生成字母数字随机名称
        /// 字符集：A-Z, a-z, 0-9
        /// </summary>
        private static string GenerateAlphanumericName(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            return new string(Enumerable.Range(0, length)
                .Select(_ => chars[random.Next(chars.Length)])
                .ToArray());
        }

        /// <summary>
        /// 生成GUID格式的随机名称
        /// 如果长度 >= 36，返回完整 GUID 格式（含连字符）
        /// 否则返回十六进制名称
        /// </summary>
        private static string GenerateGuidName(int length)
        {
            if (length >= 36)
            {
                return Guid.NewGuid().ToString();  // xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
            }
            return GenerateHexName(length);
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
            {
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            }

            var cleaned = sb.ToString();
            cleaned = cleaned.TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = $"file_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            }

            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "CON","PRN","AUX","NUL",
                "COM1","COM2","COM3","COM4","COM5","COM6","COM7","COM8","COM9",
                "LPT1","LPT2","LPT3","LPT4","LPT5","LPT6","LPT7","LPT8","LPT9"
            };
            var baseName = Path.GetFileNameWithoutExtension(cleaned);
            var ext = Path.GetExtension(cleaned);
            if (reserved.Contains(baseName))
            {
                baseName = baseName + "_";
            }

            const int maxBaseLen = 150;
            if (baseName.Length > maxBaseLen)
            {
                baseName = baseName.Substring(0, maxBaseLen);
            }

            return baseName + ext;
        }

        private bool IsEncryptedFile(string path)
        {
            return CryptoService.IsWxEncryptedFile(path);
        }

        private static bool IsSharingViolation(Exception ex)
        {
            try
            {
                int hr = Marshal.GetHRForException(ex);
                // 0x20: ERROR_SHARING_VIOLATION, 0x21: ERROR_LOCK_VIOLATION
                const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
                const int ERROR_LOCK_VIOLATION = unchecked((int)0x80070021);
                return hr == ERROR_SHARING_VIOLATION || hr == ERROR_LOCK_VIOLATION;
            }
            catch { return false; }
        }

        private bool TryForceUnlockFile(string file, Exception ex)
        {
            _options.Log?.Invoke($"源文件被占用：{ex.Message}");
            try
            {
                if (Compat.IsWindows())
                {
                    if (WindowsFileLockKiller.TryKillLockingProcesses(file, _options.Log, out var killed))
                    {
                        _options.Log?.Invoke($"已结束占用进程数量: {killed.Count}，重试处理中…");
                        return true;
                    }
                    _options.Log?.Invoke("未找到占用进程或无权限结束，占用仍存在。");
                    return false;
                }
            }
            catch (Exception e)
            {
                _options.Log?.Invoke("强制解锁失败: " + e.Message);
            }
            return false;
        }

        private void TryDeleteSourceFileWithForce(string file, bool isEncryptedSource = false)
        {
            string label = isEncryptedSource ? "源加密文件" : "源文件";
            try
            {
                // 先去只读属性（常见导致 Access denied）
                try
                {
                    var attr = File.GetAttributes(file);
                    if ((attr & FileAttributes.ReadOnly) != 0)
                    {
                        File.SetAttributes(file, attr & ~FileAttributes.ReadOnly);
                    }
                }
                catch { }

                File.Delete(file);
                _options.Log?.Invoke($"已删除{label}: {file}");
                return;
            }
            catch (IOException ioEx) when (IsSharingViolation(ioEx))
            {
                // 被占用：尝试强制结束占用进程后再删
                if (TryForceUnlockFile(file, ioEx))
                {
                    try
                    {
                        File.Delete(file);
                        _options.Log?.Invoke($"已删除{label}: {file}");
                        return;
                    }
                    catch (Exception ex2)
                    {
                        _options.Log?.Invoke($"结束占用进程后仍无法删除{label}: {file} - {ex2.Message}");
                    }
                }
                _options.Log?.Invoke($"无法删除{label}（被占用），已保留: {file}");
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // 权限不足/系统保护：也尝试结束占用进程再删一次
                _options.Log?.Invoke($"删除{label}被拒绝访问: {file} - {uaEx.Message}");
                if (Compat.IsWindows())
                {
                    try { WindowsFileLockKiller.TryKillLockingProcesses(file, _options.Log, out _); } catch { }
                }
                try
                {
                    File.Delete(file);
                    _options.Log?.Invoke($"已删除{label}: {file}");
                }
                catch (Exception ex2)
                {
                    _options.Log?.Invoke($"仍无法删除{label}，已保留: {file} - {ex2.Message}");
                }
            }
            catch (Exception ex)
            {
                _options.Log?.Invoke($"删除{label}失败，已保留: {file} - {ex.Message}");
            }
        }

        /// <summary>将 GcmRunner 的单文件进度 (0..1) 转为整批进度并上报，供 GCM 子进程轮询进度时使用。</summary>
        private sealed class GcmToOverallProgress : IProgress<double>
        {
            private readonly IProgress<double> _inner;
            private readonly Func<long> _getProcessed;
            private readonly long _totalBytes;
            private readonly long _currentFileLen;

            internal GcmToOverallProgress(IProgress<double> inner, Func<long> getProcessed, long totalBytes, long currentFileLen)
            {
                _inner = inner;
                _getProcessed = getProcessed;
                _totalBytes = totalBytes;
                _currentFileLen = currentFileLen;
            }

            public void Report(double value)
            {
                double overall = _totalBytes == 0 ? value : (_getProcessed() + value * _currentFileLen) / (double)_totalBytes;
                _inner.Report(Math.Min(1.0, overall));
            }
        }
    }
}