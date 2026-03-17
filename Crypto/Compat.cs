using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace EncryptTools
{
#if NET46 || NET48 || NET461
    /// <summary>.NET Framework 4.x 的 CryptoStream 无 leaveOpen，用此包装流避免关闭底层流。</summary>
    internal sealed class LeaveOpenStream : Stream
    {
        private readonly Stream _inner;
        public LeaveOpenStream(Stream inner) { _inner = inner ?? throw new ArgumentNullException(nameof(inner)); }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
        protected override void Dispose(bool disposing) { /* 不关闭 _inner */ }
    }
#endif

    /// <summary>
    /// .NET Framework 4.6/4.8 与 .NET 8 的 API 兼容层。
    /// </summary>
    internal static class Compat
    {
        /// <summary>
        /// 用加密安全随机数填充缓冲区。.NET 4.6 无 Fill，用 GetBytes 替代。
        /// </summary>
        public static void RngFill(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return;
#if NET46 || NET48
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(buffer);
#else
            RandomNumberGenerator.Fill(buffer);
#endif
        }

        /// <summary>
        /// SHA256 哈希。.NET 4.6 无 HashData(byte[])，用 Create().ComputeHash 替代。
        /// </summary>
        public static byte[] Sha256Hash(byte[] data)
        {
            if (data == null) return null;
#if NET46 || NET48
            using (var sha = SHA256.Create())
                return sha.ComputeHash(data);
#else
            return SHA256.HashData(data);
#endif
        }

        /// <summary>
        /// 将字节数组转为十六进制小写字符串。.NET 4.6 无 Convert.ToHexString。
        /// </summary>
        public static string ToHexString(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
#if NET46 || NET48
            var s = BitConverter.ToString(bytes);
            return s.Replace("-", "").ToLowerInvariant();
#else
            return Convert.ToHexString(bytes).ToLowerInvariant();
#endif
        }

        public static Task FileWriteAllBytesAsync(string path, byte[] bytes)
        {
#if NET46 || NET48 || NET461
            File.WriteAllBytes(path, bytes);
            return Task.FromResult(0);
#else
            return File.WriteAllBytesAsync(path, bytes);
#endif
        }

        public static void FileMoveOverwrite(string sourceFileName, string destFileName)
        {
#if NET46 || NET48
            if (File.Exists(destFileName)) File.Delete(destFileName);
            File.Move(sourceFileName, destFileName);
#else
            File.Move(sourceFileName, destFileName, overwrite: true);
#endif
        }

        /// <summary>当前是否为 Windows（.NET 4.x 下本程序仅支持 Windows 故恒为 true）。</summary>
        public static bool IsWindows()
        {
#if NET46 || NET48 || NET461
            return true;
#else
            return System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
#endif
        }

#if NET46 || NET48
        /// <summary>.NET 4.6 无 Stream.CopyToAsync(Stream, int, CancellationToken)，用两参数版本。</summary>
        public static Task CopyToAsync(Stream source, Stream destination, int bufferSize, CancellationToken ct)
        {
            return source.CopyToAsync(destination, bufferSize);
        }

        /// <summary>.NET 4.6/4.8 无 Rfc2898DeriveBytes(string, byte[], int, HashAlgorithmName)，提供 PBKDF2-SHA256 派生。</summary>
        public static byte[] DeriveKeyPbkdf2Sha256(string password, byte[] salt, int iterations, int keySizeBytes)
        {
            var passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            using (var hmac = new HMACSHA256(passwordBytes))
            {
                var result = new List<byte>();
                for (int i = 1; i <= (keySizeBytes + 31) / 32; i++)
                {
                    var block = new byte[salt.Length + 4];
                    Buffer.BlockCopy(salt, 0, block, 0, salt.Length);
                    block[salt.Length] = (byte)(i >> 24);
                    block[salt.Length + 1] = (byte)(i >> 16);
                    block[salt.Length + 2] = (byte)(i >> 8);
                    block[salt.Length + 3] = (byte)i;
                    var u = hmac.ComputeHash(block);
                    var t = (byte[])u.Clone();
                    for (int j = 1; j < iterations; j++)
                    {
                        u = hmac.ComputeHash(u);
                        for (int k = 0; k < t.Length; k++) t[k] ^= u[k];
                    }
                    result.AddRange(t);
                }
                var key = new byte[keySizeBytes];
                for (int i = 0; i < keySizeBytes; i++) key[i] = result[i];
                return key;
            }
        }
#else
        /// <summary>.NET Core/8 使用带 CancellationToken 的 CopyToAsync。</summary>
        public static Task CopyToAsync(Stream source, Stream destination, int bufferSize, CancellationToken ct)
        {
            return source.CopyToAsync(destination, bufferSize, ct);
        }
#endif
    }
}
