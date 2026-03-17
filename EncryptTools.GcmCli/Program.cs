using System;
using System.IO;
using System.Security.Cryptography;

namespace EncryptTools.GcmCli
{
    internal static class Program
    {
        private const int BufferSize = 4 * 1024 * 1024;
        private static readonly byte[] HeaderMagic = System.Text.Encoding.ASCII.GetBytes("WXENC001");
        private const int HeaderSize = 16;
        private const byte HeaderVersion = 1;
        private const byte EncryptTypeGcm = 2;

        private const byte PwdFormatGcm = 0x01;
        private const byte PwdFormatCbc = 0x02;

        static int Main(string[] args)
        {
            try
            {
                // 解密 .pwd 文件（GCM 格式）：--decrypt-pwd --input <path>，密码输出到 stdout
                string decryptPwdInput = null;
                string encryptPwdOutput = null;
                string encryptPwdPasswordFile = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--decrypt-pwd" && i + 2 < args.Length && args[i + 1] == "--input")
                    {
                        decryptPwdInput = args[i + 2];
                        break;
                    }
                    if (args[i] == "--encrypt-pwd")
                    {
                        // continue parsing; handled below
                        continue;
                    }
                }
                if (decryptPwdInput != null)
                {
                    string pwd = DoDecryptPasswordFile(decryptPwdInput);
                    if (pwd == null) return 1;
                    Console.Out.Write(pwd);
                    return 0;
                }

                // 加密 .pwd 文件（旧版无格式字节的 GCM）：--encrypt-pwd --output <path> --password-file <path>
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--encrypt-pwd") { /* flag */ }
                    else if (args[i] == "--output" && i + 1 < args.Length) encryptPwdOutput = args[++i];
                    else if (args[i] == "--password-file" && i + 1 < args.Length) encryptPwdPasswordFile = args[++i];
                }
                if (Array.IndexOf(args, "--encrypt-pwd") >= 0)
                {
                    if (string.IsNullOrEmpty(encryptPwdOutput) || string.IsNullOrEmpty(encryptPwdPasswordFile))
                    {
                        Console.Error.WriteLine("Usage: --encrypt-pwd --output <path> --password-file <path>");
                        return 1;
                    }
                    string pwdText = File.Exists(encryptPwdPasswordFile) ? File.ReadAllText(encryptPwdPasswordFile).Trim() : "";
                    if (string.IsNullOrEmpty(pwdText))
                    {
                        Console.Error.WriteLine("Empty password.");
                        return 1;
                    }
                    DoEncryptPasswordFileLegacyGcmNoFormat(encryptPwdOutput, pwdText);
                    return 0;
                }

                bool? encrypt = null;
                string input = null;
                string output = null;
                string passwordFile = null;
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--encrypt") encrypt = true;
                    else if (args[i] == "--decrypt") encrypt = false;
                    else if (args[i] == "--input" && i + 1 < args.Length) input = args[++i];
                    else if (args[i] == "--output" && i + 1 < args.Length) output = args[++i];
                    else if (args[i] == "--password-file" && i + 1 < args.Length) passwordFile = args[++i];
                }
                if (!encrypt.HasValue || string.IsNullOrEmpty(input) || string.IsNullOrEmpty(output) || string.IsNullOrEmpty(passwordFile))
                {
                    Console.Error.WriteLine("Usage: --encrypt|--decrypt --input <path> --output <path> --password-file <path>");
                    return 1;
                }
                string password = File.Exists(passwordFile) ? File.ReadAllText(passwordFile).Trim() : "";
                if (string.IsNullOrEmpty(password))
                {
                    Console.Error.WriteLine("Empty password.");
                    return 1;
                }
                if (encrypt == true)
                    DoEncrypt(input, output, password);
                else
                    DoDecrypt(input, output, password);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        /// <summary>解密 .pwd 文件（GCM 或 CBC 格式），返回明文密码；失败返回 null。</summary>
        static string DoDecryptPasswordFile(string filePath)
        {
            byte[] data;
            try { data = File.ReadAllBytes(filePath); }
            catch { return null; }
            if (data == null || data.Length < 2) return null;

            byte format = data[0];

            // 带格式字节：CBC
            if (format == PwdFormatCbc)
            {
                if (data.Length < 1 + 32 + 16) return null;
                byte[] key = new byte[32];
                byte[] iv = new byte[16];
                Buffer.BlockCopy(data, 1, key, 0, 32);
                Buffer.BlockCopy(data, 1 + 32, iv, 0, 16);
                int cipherLen = data.Length - (1 + 32 + 16);
                if (cipherLen <= 0) return null;
                byte[] ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(data, 1 + 32 + 16, ciphertext, 0, cipherLen);
                using (var aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                    {
                        byte[] plain = dec.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
                        return System.Text.Encoding.UTF8.GetString(plain);
                    }
                }
            }

            // 带格式字节：GCM
            if (format == PwdFormatGcm)
            {
                if (data.Length < 1 + 32 + 12 + 16) return null;
                byte[] key = new byte[32];
                byte[] nonce = new byte[12];
                byte[] tag = new byte[16];
                Buffer.BlockCopy(data, 1, key, 0, 32);
                Buffer.BlockCopy(data, 1 + 32, nonce, 0, 12);
                Buffer.BlockCopy(data, 1 + 32 + 12, tag, 0, 16);
                int cipherLen = data.Length - (1 + 32 + 12 + 16);
                if (cipherLen < 0) return null;
                byte[] ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(data, 1 + 32 + 12 + 16, ciphertext, 0, cipherLen);
                byte[] plaintext = new byte[cipherLen];
                using (var aesGcm = new AesGcm(key, 16))
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return System.Text.Encoding.UTF8.GetString(plaintext);
            }

            // 旧版（无格式字节）：Key(32)+Nonce(12)+Tag(16)+Ciphertext（AES-GCM）
            if (data.Length >= 60)
            {
                byte[] key = new byte[32];
                byte[] nonce = new byte[12];
                byte[] tag = new byte[16];
                Buffer.BlockCopy(data, 0, key, 0, 32);
                Buffer.BlockCopy(data, 32, nonce, 0, 12);
                Buffer.BlockCopy(data, 44, tag, 0, 16);
                int cipherLen = data.Length - 60;
                if (cipherLen < 0) return null;
                byte[] ciphertext = new byte[cipherLen];
                Buffer.BlockCopy(data, 60, ciphertext, 0, cipherLen);
                byte[] plaintext = new byte[cipherLen];
                using (var aesGcm = new AesGcm(key, 16))
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return System.Text.Encoding.UTF8.GetString(plaintext);
            }

            return null;
        }

        static void DoEncryptPasswordFileLegacyGcmNoFormat(string outputPath, string password)
        {
            byte[] key = RandomNumberGenerator.GetBytes(32);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] passwordBytes = System.Text.Encoding.UTF8.GetBytes(password);
            byte[] ciphertext = new byte[passwordBytes.Length];
            byte[] tag = new byte[16];
            using (var aesGcm = new AesGcm(key, 16))
                aesGcm.Encrypt(nonce, passwordBytes, ciphertext, tag);
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                fs.Write(key, 0, 32);
                fs.Write(nonce, 0, 12);
                fs.Write(tag, 0, 16);
                fs.Write(ciphertext, 0, ciphertext.Length);
            }
        }

        static byte[] DeriveKey(string password, byte[] salt, int iterations, int keySize)
        {
            using (var kdf = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
                return kdf.GetBytes(keySize);
        }

        static void DoEncrypt(string inputPath, string outputPath, string password)
        {
            int iterations = 200_000;
            int keySizeBits = 256;
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            byte[] key = DeriveKey(password, salt, iterations, keySizeBits / 8);
            string originalName = Path.GetFileName(inputPath);
            byte[] nameBytes = System.Text.Encoding.UTF8.GetBytes(originalName);

            using (var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            using (var bw = new BinaryWriter(outFs))
            {
                bw.Write(HeaderMagic);
                bw.Write(HeaderVersion);
                bw.Write(EncryptTypeGcm);
                for (int i = 0; i < 6; i++) bw.Write((byte)0);

                bw.Write(iterations);
                bw.Write(salt.Length);
                bw.Write(salt);
                bw.Write(keySizeBits);
                bw.Write(nameBytes.Length);
                bw.Write(nameBytes);

                byte[] nonce = RandomNumberGenerator.GetBytes(12);
                outFs.Write(nonce, 0, nonce.Length);

                using (var aesGcm = new AesGcm(key, 16))
                using (var inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                {
                    byte[] buffer = new byte[BufferSize];
                    int bytesRead;
                    while ((bytesRead = inFs.Read(buffer, 0, BufferSize)) > 0)
                    {
                        byte[] plaintext = new byte[bytesRead];
                        byte[] ciphertext = new byte[bytesRead];
                        byte[] tag = new byte[16];
                        Array.Copy(buffer, 0, plaintext, 0, bytesRead);
                        aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
                        outFs.Write(ciphertext, 0, bytesRead);
                        outFs.Write(tag, 0, 16);
                    }
                }
            }
        }

        static void DoDecrypt(string inputPath, string outputPath, string password)
        {
            using (var inFs = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var br = new BinaryReader(inFs))
            {
                byte[] header = br.ReadBytes(HeaderSize);
                if (header.Length < HeaderSize)
                    throw new InvalidDataException("不是有效加密文件");
                for (int i = 0; i < HeaderMagic.Length; i++)
                    if (header[i] != HeaderMagic[i])
                        throw new InvalidDataException("不是有效加密文件");
                if (header[9] != EncryptTypeGcm)
                    throw new InvalidDataException("解密类型未知，跳过文件");

                int iterations = br.ReadInt32();
                int saltLen = br.ReadInt32();
                if (saltLen < 0 || saltLen > 256) throw new InvalidDataException("文件头损坏");
                byte[] salt = br.ReadBytes(saltLen);
                int keySizeBits = br.ReadInt32();
                if (keySizeBits == 0) keySizeBits = 256;
                int nameLen = br.ReadInt32();
                if (nameLen > 0 && nameLen < 4096)
                    br.ReadBytes(nameLen);

                byte[] key = DeriveKey(password, salt, iterations, keySizeBits / 8);
                byte[] nonce = new byte[12];
                if (inFs.Read(nonce, 0, 12) != 12)
                    throw new InvalidDataException("Missing nonce.");

                using (var aesGcm = new AesGcm(key, 16))
                using (var outFs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
                {
                    byte[] buffer = new byte[BufferSize + 16];
                    int bytesRead;
                    while ((bytesRead = inFs.Read(buffer, 0, BufferSize + 16)) > 0)
                    {
                        if (bytesRead < 16) break;
                        int cipherLength = bytesRead - 16;
                        byte[] tag = new byte[16];
                        byte[] ciphertext = new byte[cipherLength];
                        byte[] plaintext = new byte[cipherLength];
                        Array.Copy(buffer, cipherLength, tag, 0, 16);
                        Array.Copy(buffer, 0, ciphertext, 0, cipherLength);
                        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                        outFs.Write(plaintext, 0, cipherLength);
                    }
                }
            }
        }
    }
}
