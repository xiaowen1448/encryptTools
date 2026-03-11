using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace EncryptTools
{
    internal static class PasswordFileHelper
    {
        public static void SavePasswordToFile(string password, string filePath)
        {
            if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("password is empty", nameof(password));
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentException("filePath is empty", nameof(filePath));

            var key = new byte[32]; // 256-bit key
            var nonce = new byte[12];
            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(nonce);

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var ciphertext = new byte[passwordBytes.Length];
            var tag = new byte[16];

            using (var aesGcm = new AesGcm(key, 16))
            {
                aesGcm.Encrypt(nonce, passwordBytes, ciphertext, tag);
            }

            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            fs.Write(key, 0, key.Length);
            fs.Write(nonce, 0, nonce.Length);
            fs.Write(tag, 0, tag.Length);
            fs.Write(ciphertext, 0, ciphertext.Length);
        }

        public static string LoadPasswordFromFile(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length < 60) throw new InvalidDataException("密码文件格式不正确");

            var key = new byte[32];
            var nonce = new byte[12];
            var tag = new byte[16];
            fs.Read(key, 0, 32);
            fs.Read(nonce, 0, 12);
            fs.Read(tag, 0, 16);
            var ciphertext = new byte[fs.Length - 60];
            fs.Read(ciphertext, 0, ciphertext.Length);

            var plaintext = new byte[ciphertext.Length];
            using (var aesGcm = new AesGcm(key, 16))
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            }
            return Encoding.UTF8.GetString(plaintext);
        }
    }
}

