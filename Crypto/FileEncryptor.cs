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
        public string SourcePath { get; set; }
        public string OutputRoot { get; set; }
        public bool InPlace { get; set; }
        public bool Recursive { get; set; }
        public CryptoAlgorithm Algorithm { get; set; }
        public string Password { get; set; }
        public int Iterations { get; set; } = 200_000;
        public int AesKeySizeBits { get; set; } = 256;
        public Action<string> Log { get; set; }
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
                await _crypto.DecryptFileAsync(file, outFile, _options.Password, new Progress<long>(bytes =>
                {
                    processed += bytes;
                    progress?.Report(totalBytes == 0 ? 1.0 : (double)processed / totalBytes);
                }), ct);

                // 原地解密：仅在成功生成非空输出文件时删除源 .enc 文件
                if (_options.InPlace)
                {
                    try
                    {
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
                return encrypt ? source + GetEncryptedExtension(_options.Algorithm) : DeriveDecryptedName(source);
            }
            else
            {
                var root = _options.OutputRoot ?? Path.Combine(Path.GetDirectoryName(source)!, "output");
                var relative = MakeRelativeToRoot(source);
                var targetDir = Path.Combine(root, Path.GetDirectoryName(relative) ?? string.Empty);
                var fileName = Path.GetFileName(source);
                var outName = encrypt ? fileName + GetEncryptedExtension(_options.Algorithm) : DeriveDecryptedName(fileName);
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