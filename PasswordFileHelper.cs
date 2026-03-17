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

            // 优先生成“旧版 AES-GCM 无格式字节” pwd：key(32)+nonce(12)+tag(16)+ciphertext
#if NET46 || NET48
            if (RuntimeHelper.IsNet8InstalledOnMachine)
            {
                if (GcmRunner.EncryptPasswordFile(filePath, password))
                    return;
            }
            // 未安装 .NET 8 时无法生成旧版 GCM 格式；退回为 CBC 带格式字节，至少保证本程序可读
            SavePasswordToFileCbcWithFormat(password, filePath);
#else
            SavePasswordToFileLegacyGcmNoFormat(password, filePath);
#endif
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
                if (RuntimeHelper.IsNet8InstalledOnMachine)
                {
                    string pwd = GcmRunner.DecryptPasswordFile(filePath);
                    if (!string.IsNullOrEmpty(pwd))
                        return pwd;
                }
                throw new NotSupportedException("此密码文件为 GCM 格式，请安装 .NET 8 后使用；若已安装 .NET 8，请确保程序目录下存在 EncryptTools.GcmCli.dll。");
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
                if (RuntimeHelper.IsNet8InstalledOnMachine)
                {
                    string pwd = GcmRunner.DecryptPasswordFile(filePath);
                    if (!string.IsNullOrEmpty(pwd))
                        return pwd;
                }
                throw new NotSupportedException("此密码文件为旧版 GCM 格式，请安装 .NET 8 后使用；若已安装 .NET 8，请确保程序目录下存在 EncryptTools.GcmCli.dll。");
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

