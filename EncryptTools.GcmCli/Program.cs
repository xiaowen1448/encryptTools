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

        static int Main(string[] args)
        {
            try
            {
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
