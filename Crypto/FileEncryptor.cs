using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        public bool RandomizeFileName { get; set; } // 新增：加密输出使用随机文件名
        public CryptoAlgorithm Algorithm { get; set; }
        public required string Password { get; set; }
        public int Iterations { get; set; } = 200_000;
        public int AesKeySizeBits { get; set; } = 256;
        public required Action<string> Log { get; set; }
    }

    public class FileEncryptor
    {
        private readonly FileEncryptorOptions _options;
        private readonly CryptoService _crypto;
        private static readonly string[] KnownEncryptedExtensions = new[] { ".enc", ".aes", ".aesgcm", ".3des", ".xor" };

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

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var outFile = GetOutputFilePath(file, encrypt: true);
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                _options.Log?.Invoke($"加密: {file} -> {outFile}");
                await _crypto.EncryptFileAsync(file, outFile, _options.Algorithm, _options.Password, _options.Iterations, _options.AesKeySizeBits, new Progress<long>(bytes =>
                {
                    processed += bytes;
                    progress?.Report(totalBytes == 0 ? 1.0 : (double)processed / totalBytes);
                }), ct);

                // 原地加密：仅在成功生成非空输出文件时删除源文件
                if (_options.InPlace)
                {
                    try
                    {
                        if (File.Exists(outFile) && new FileInfo(outFile).Length > 0)
                        {
                            File.Delete(file);
                            _options.Log?.Invoke($"已删除源文件: {file}");
                        }
                        else
                        {
                            _options.Log?.Invoke($"输出文件缺失或为空，已保留源文件: {file}");
                        }
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
                .Where(f => KnownEncryptedExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) || IsEncryptedFile(f))
                .ToList();

            long totalBytes = files.Sum(f => new FileInfo(f).Length);
            long processed = 0;

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                var outFile = GetOutputFilePath(file, encrypt: false);
                Directory.CreateDirectory(Path.GetDirectoryName(outFile)!);
                _options.Log?.Invoke($"解密: {file} -> {outFile}");
                var result = await _crypto.DecryptFileAsync(file, outFile, _options.Password, new Progress<long>(bytes =>
                {
                    processed += bytes;
                    progress?.Report(totalBytes == 0 ? 1.0 : (double)processed / totalBytes);
                }), ct);

                // 如果密文中包含原始文件名，解密后恢复文件名
                if (!string.IsNullOrWhiteSpace(result?.OriginalFileName))
                {
                    try
                    {
                        var targetDir = Path.GetDirectoryName(outFile)!;
                        // 清洗原始文件名，去除Windows非法字符、保留扩展名、避免保留字
                        var restoredName = SanitizeFileName(result!.OriginalFileName);
                        if (!string.Equals(restoredName, result!.OriginalFileName, StringComparison.Ordinal))
                        {
                            _options.Log?.Invoke($"原始文件名包含空格或特殊字符，已更正为: {restoredName}");
                        }
                        var desired = Path.Combine(targetDir, restoredName);
                        var finalPath = desired;
                        if (File.Exists(desired))
                        {
                            var baseName = Path.GetFileNameWithoutExtension(desired);
                            var ext = Path.GetExtension(desired);
                            var stamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                            finalPath = Path.Combine(targetDir, $"{baseName}-restored-{stamp}{ext}");
                        }
                        if (outFile != finalPath)
                        {
                            File.Move(outFile, finalPath);
                            _options.Log?.Invoke($"已恢复原始文件名: {Path.GetFileName(finalPath)}");
                            // 重命名后更新 outFile 变量，后续删除源加密文件时才能正确检测输出文件是否存在
                            outFile = finalPath;
                            _options.Log?.Invoke($"最终输出文件: {finalPath}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _options.Log?.Invoke($"恢复原始文件名失败: {ex.Message}");
                    }
                }

                // 原地解密：仅在成功生成非空输出文件时删除源 .enc 文件
                if (_options.InPlace)
                {
                    try
                    {
                        // 注意：如果发生了文件名恢复（重命名），outFile 已被更新为最终路径
                        if (File.Exists(outFile) && new FileInfo(outFile).Length > 0)
                        {
                            File.Delete(file);
                            _options.Log?.Invoke($"已删除源加密文件: {file}");
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
                    var ext = GetEncryptedExtension(_options.Algorithm);
                    var name = _options.RandomizeFileName ? GenerateRandomName(16) : Path.GetFileName(source);
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
                var outName = encrypt
                    ? (_options.RandomizeFileName ? GenerateRandomName(16) : fileName) + GetEncryptedExtension(_options.Algorithm)
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
            foreach (var ext in KnownEncryptedExtensions)
            {
                if (encryptedName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                {
                    return encryptedName.Substring(0, encryptedName.Length - ext.Length);
                }
            }
            // If encrypted file contains original extension inside header, CryptoService will restore it; fallback:
            return encryptedName + ".dec";
        }

        private static string GetEncryptedExtension(CryptoAlgorithm alg)
        {
            return alg switch
            {
                CryptoAlgorithm.AesCbc => ".aes",
                CryptoAlgorithm.AesGcm => ".aesgcm",
                CryptoAlgorithm.TripleDes => ".3des",
                CryptoAlgorithm.Xor => ".xor",
                _ => ".enc"
            };
        }

        private static string GenerateRandomName(int length)
        {
            // 使用GUID生成随机名称，截断到指定长度
            var guid = Guid.NewGuid().ToString("N");
            return guid.Length > length ? guid.Substring(0, length) : guid;
        }

        private static string SanitizeFileName(string name)
        {
            // 替换非法字符为下划线
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (var ch in name)
            {
                sb.Append(invalid.Contains(ch) ? '_' : ch);
            }

            var cleaned = sb.ToString();
            // 去除结尾的空格和点，这些在Windows文件系统中非法
            cleaned = cleaned.TrimEnd(' ', '.');
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                cleaned = $"file_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
            }

            // 处理Windows保留文件名（不区分大小写）
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

            // 限制文件名长度，避免路径过长（保留扩展名）
            const int maxBaseLen = 150; // 适度保守
            if (baseName.Length > maxBaseLen)
            {
                baseName = baseName.Substring(0, maxBaseLen);
            }

            return baseName + ext;
        }

        private bool IsEncryptedFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var magic = new byte[4];
                if (fs.Length < 4) return false;
                var read = fs.Read(magic, 0, 4);
                if (read != 4) return false;
                return magic[0] == (byte)'E' && magic[1] == (byte)'N' && magic[2] == (byte)'C' && (magic[3] == (byte)'1' || magic[3] == (byte)'2');
            }
            catch { return false; }
        }
    }
}