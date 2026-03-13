using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace EncryptTools
{
    /// <summary>
    /// .NET Framework 4.8 与 .NET 8 的 API 兼容层。
    /// </summary>
    internal static class Compat
    {
        /// <summary>
        /// 用加密安全随机数填充缓冲区。.NET 4.6 无 Fill，用 GetBytes 替代。
        /// </summary>
        public static void RngFill(byte[] buffer)
        {
            if (buffer == null || buffer.Length == 0) return;
#if NET48
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
#if NET48
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
#if NET48
            var s = BitConverter.ToString(bytes);
            return s.Replace("-", "").ToLowerInvariant();
#else
            return Convert.ToHexString(bytes).ToLowerInvariant();
#endif
        }

        public static Task FileWriteAllBytesAsync(string path, byte[] bytes)
        {
#if NET48
            File.WriteAllBytes(path, bytes);
            return Task.CompletedTask;
#else
            return File.WriteAllBytesAsync(path, bytes);
#endif
        }

        public static void FileMoveOverwrite(string sourceFileName, string destFileName)
        {
#if NET48
            if (File.Exists(destFileName)) File.Delete(destFileName);
            File.Move(sourceFileName, destFileName);
#else
            File.Move(sourceFileName, destFileName, overwrite: true);
#endif
        }
    }
}
