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

        /// <summary>
        /// 与旧版程序兼容：
        /// - 旧版保存逻辑为：Key(32)+Nonce(12)+Tag(16)+Ciphertext（AES-GCM，且不写格式字节）
        /// - net48 进程内无 AesGcm 时，若本机已安装 .NET 8，则通过 GcmCli 生成/解析该格式
        /// </summary>
        public static void SavePasswordToFile(string password, string filePath)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("password is empty", nameof(password));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is empty", nameof(filePath));

            // 兼容性优先：统一保存为 CBC（带格式字节 0x02）。
            // 这样在 net461/net48/无 .NET8 的环境里也能直接读取，不依赖 EncryptTools.GcmCli.dll。
            SavePasswordToFileCbcWithFormat(password, filePath);
        }

        /// <summary>
        /// 封装为可运行 exe 时使用：若当前 .pwd 为 GCM/旧版格式，则就地改写为带 0x02 的 AES-CBC 格式。
        /// 便于目标机在仅 net48、未装 .NET 8 时也能用托管代码解密密码文件；若已是 CBC 格式则不变。
        /// 改写后需重新计算文件字节 SHA256 再写入加密包头（调用方在改写之后取哈希）。
        /// </summary>
        /// <returns>true 表示已升级为 CBC；false 表示无需改动或升级失败。</returns>
        public static bool EnsurePwdFileCbcForPortableExe(string? pwdFilePath)
        {
            if (string.IsNullOrWhiteSpace(pwdFilePath) || !File.Exists(pwdFilePath)) return false;
            byte[] data;
            try { data = File.ReadAllBytes(pwdFilePath); } catch { return false; }
            if (data.Length >= 1 && data[0] == FormatCbc) return false;
            try
            {
                string plain = LoadPasswordFromFile(pwdFilePath);
                SavePasswordToFile(plain, pwdFilePath);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string LoadPasswordFromFile(string filePath)
        {
            // 为了同时兼容“带格式字节”和“旧版无格式字节”，这里统一按字节数组解析
            var data = File.ReadAllBytes(filePath);
            if (data.Length < 2) throw new InvalidDataException("密码文件格式不正确");

            byte first = data[0];

            // 带格式字节：0x02 CBC
            if (first == FormatCbc)
            {
                if (data.Length < 1 + 32 + 16) throw new InvalidDataException("密码文件格式不正确");
                var key = new byte[32];
                var iv = new byte[16];
                Buffer.BlockCopy(data, 1, key, 0, 32);
                Buffer.BlockCopy(data, 1 + 32, iv, 0, 16);
                int cipherLen = data.Length - (1 + 32 + 16);
                if (cipherLen <= 0) throw new InvalidDataException("密码文件格式不正确");
                var ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(data, 1 + 32 + 16, ciphertext, 0, cipherLen);
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
            }

            // 带格式字节：0x01 GCM
            if (first == FormatGcm)
            {
#if NET46 || NET48
                // 由 GcmCli 解密，避免当前进程缺少 AesGcm。
                // 若环境无法执行 GcmCli，则抛出明确提示。
                string pwd = GcmRunner.DecryptPasswordFile(filePath);
                if (!string.IsNullOrEmpty(pwd))
                    return pwd;
                throw new NotSupportedException("此密码文件为 GCM 格式：需要可执行 EncryptTools.GcmCli.dll（dotnet 运行时环境）。");
#else
                if (data.Length < 1 + 32 + 12 + 16) throw new InvalidDataException("密码文件格式不正确");
                var key = new byte[32];
                var nonce = new byte[12];
                var tag = new byte[16];
                Buffer.BlockCopy(data, 1, key, 0, 32);
                Buffer.BlockCopy(data, 1 + 32, nonce, 0, 12);
                Buffer.BlockCopy(data, 1 + 32 + 12, tag, 0, 16);
                int cipherLen = data.Length - (1 + 32 + 12 + 16);
                if (cipherLen < 0) throw new InvalidDataException("密码文件格式不正确");
                var ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(data, 1 + 32 + 12 + 16, ciphertext, 0, cipherLen);
                var plaintext = new byte[cipherLen];
                using (var aesGcm = new AesGcm(key, 16))
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
#endif
            }

            // 旧版（你贴的逻辑）：无格式字节，Key(32)+Nonce(12)+Tag(16)+Ciphertext（AES-GCM）
            if (data.Length >= 60)
            {
#if NET46 || NET48
                string pwd = GcmRunner.DecryptPasswordFile(filePath);
                if (!string.IsNullOrEmpty(pwd))
                    return pwd;
                throw new NotSupportedException("此密码文件为旧版 GCM 格式：需要可执行 EncryptTools.GcmCli.dll（dotnet 运行时环境）。");
#else
                var key = new byte[32];
                var nonce = new byte[12];
                var tag = new byte[16];
                Buffer.BlockCopy(data, 0, key, 0, 32);
                Buffer.BlockCopy(data, 32, nonce, 0, 12);
                Buffer.BlockCopy(data, 44, tag, 0, 16);
                int cipherLen = data.Length - 60;
                var ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(data, 60, ciphertext, 0, cipherLen);
                var plaintext = new byte[cipherLen];
                using (var aesGcm = new AesGcm(key, 16))
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
#endif
            }

            // 其它情况视为损坏或不支持
            throw new InvalidDataException("密码文件格式不正确");
        }

#if !(NET46 || NET48)
        private static void SavePasswordToFileLegacyGcmNoFormat(string password, string filePath)
        {
            var key = new byte[32];
            Compat.RngFill(key);
            var nonce = new byte[12];
            Compat.RngFill(nonce);
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var ciphertext = new byte[passwordBytes.Length];
            var tag = new byte[16];
            using (var aesGcm = new AesGcm(key, 16))
                aesGcm.Encrypt(nonce, passwordBytes, ciphertext, tag);
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(key, 0, 32);
                fs.Write(nonce, 0, 12);
                fs.Write(tag, 0, 16);
                fs.Write(ciphertext, 0, ciphertext.Length);
            }
        }
#endif

        private static void SavePasswordToFileCbcWithFormat(string password, string filePath)
        {
            var key = new byte[32];
            Compat.RngFill(key);
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.GenerateIV();
                var iv = aes.IV;
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                byte[] ciphertext;
                using (var enc = aes.CreateEncryptor())
                    ciphertext = enc.TransformFinalBlock(passwordBytes, 0, passwordBytes.Length);
                using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.WriteByte(FormatCbc);
                    fs.Write(key, 0, 32);
                    fs.Write(iv, 0, 16);
                    fs.Write(ciphertext, 0, ciphertext.Length);
                }
            }
        }
    }
}

