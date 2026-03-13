using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EncryptTools
{
    internal static class PasswordFileHelper
    {
        private const byte FormatGcm = 0x01;
        private const byte FormatCbc = 0x02;

        public static void SavePasswordToFile(string password, string filePath)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("password is empty", nameof(password));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is empty", nameof(filePath));

            var key = new byte[32];
            Compat.RngFill(key);

#if NET48
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.GenerateIV();
                var iv = aes.IV;
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] ciphertext;
                using (var enc = aes.CreateEncryptor())
                    ciphertext = enc.TransformFinalBlock(passwordBytes, 0, passwordBytes.Length);
                using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                fs.WriteByte(FormatCbc);
                fs.Write(key, 0, 32);
                fs.Write(iv, 0, 16);
                fs.Write(ciphertext, 0, ciphertext.Length);
            }
#else
            var nonce = new byte[12];
            Compat.RngFill(nonce);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var ciphertext = new byte[passwordBytes.Length];
            var tag = new byte[16];
            using (var aesGcm = new AesGcm(key, 16))
                aesGcm.Encrypt(nonce, passwordBytes, ciphertext, tag);
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.WriteByte(FormatGcm);
                fs.Write(key, 0, 32);
                fs.Write(nonce, 0, 12);
                fs.Write(tag, 0, 16);
                fs.Write(ciphertext, 0, ciphertext.Length);
            }
#endif
        }

        public static string LoadPasswordFromFile(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 2) throw new InvalidDataException("密码文件格式不正确");
            var format = (byte)fs.ReadByte();
            if (format == FormatCbc)
            {
#if NET48
                var key = new byte[32];
                var iv = new byte[16];
                if (fs.Read(key, 0, 32) != 32 || fs.Read(iv, 0, 16) != 16) throw new InvalidDataException("密码文件格式不正确");
                var ciphertext = new byte[fs.Length - 1 - 32 - 16];
                if (fs.Read(ciphertext, 0, ciphertext.Length) != ciphertext.Length) throw new InvalidDataException("密码文件格式不正确");
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                    {
                        var plaintext = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                        return Encoding.UTF8.GetString(plaintext);
                    }
                }
#else
                var key = new byte[32];
                var iv = new byte[16];
                if (fs.Read(key, 0, 32) != 32 || fs.Read(iv, 0, 16) != 16) throw new InvalidDataException("密码文件格式不正确");
                var ciphertext = new byte[fs.Length - 1 - 32 - 16];
                if (fs.Read(ciphertext, 0, ciphertext.Length) != ciphertext.Length) throw new InvalidDataException("密码文件格式不正确");
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                    {
                        var plaintext = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                        return Encoding.UTF8.GetString(plaintext);
                    }
                }
#endif
            }
            if (format == FormatGcm)
            {
#if NET48
                throw new NotSupportedException("此密码文件由 .NET 8 版本创建，请安装 .NET 8 后使用基于 .NET 8 的本程序打开。");
#else
                if (fs.Length < 1 + 32 + 12 + 16) throw new InvalidDataException("密码文件格式不正确");
                var key = new byte[32];
                var nonce = new byte[12];
                var tag = new byte[16];
                if (fs.Read(key, 0, 32) != 32 || fs.Read(nonce, 0, 12) != 12 || fs.Read(tag, 0, 16) != 16) throw new InvalidDataException("密码文件格式不正确");
                var ciphertext = new byte[fs.Length - 1 - 32 - 12 - 16];
                if (fs.Read(ciphertext, 0, ciphertext.Length) != ciphertext.Length) throw new InvalidDataException("密码文件格式不正确");
                var plaintext = new byte[ciphertext.Length];
                using (var aesGcm = new AesGcm(key, 16))
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
#endif
            }
            // 兼容旧格式：无版本字节，直接 key(32)+nonce(12)+tag(16)+ciphertext；当前已读的 1 字节视为 key 的第 1 字节
            if (fs.Length < 60) throw new InvalidDataException("密码文件格式不正确");
            var keyLegacy = new byte[32];
            keyLegacy[0] = format;
            if (fs.Read(keyLegacy, 1, 31) != 31) throw new InvalidDataException("密码文件格式不正确");
            var nonceLegacy = new byte[12];
            var tagLegacy = new byte[16];
            if (fs.Read(nonceLegacy, 0, 12) != 12 || fs.Read(tagLegacy, 0, 16) != 16) throw new InvalidDataException("密码文件格式不正确");
            var ciphertextLegacy = new byte[fs.Length - 60];
            if (fs.Read(ciphertextLegacy, 0, ciphertextLegacy.Length) != ciphertextLegacy.Length) throw new InvalidDataException("密码文件格式不正确");
#if NET48
            throw new NotSupportedException("此密码文件为 GCM 格式，请安装 .NET 8 后使用基于 .NET 8 的本程序打开。");
#else
            var plaintextLegacy = new byte[ciphertextLegacy.Length];
            using (var aesGcm = new AesGcm(keyLegacy, 16))
                aesGcm.Decrypt(nonceLegacy, ciphertextLegacy, tagLegacy, plaintextLegacy);
            return Encoding.UTF8.GetString(plaintextLegacy);
#endif
        }
    }
}

