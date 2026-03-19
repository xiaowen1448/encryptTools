using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace EncryptTools.PasswordFile
{
    /// <summary>
    /// 密码文件目录与列表、随机密码/文件名生成、复杂度校验。
    /// </summary>
    internal static class PasswordFileService
    {
        public static string GetPwdDirectory()
        {
            return Path.Combine(AppContext.BaseDirectory, "pwd");
        }

        public static void EnsurePwdDirectory()
        {
            var dir = GetPwdDirectory();
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }

        public static string[] ListPwdFiles()
        {
            EnsurePwdDirectory();
            var dir = GetPwdDirectory();
            return Directory.GetFiles(dir, "*.pwd", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        /// <summary>
        /// 至少12位，包含大小写字母、数字、特殊字符。
        /// </summary>
        public static bool ValidateComplexity(string? password)
        {
            if (string.IsNullOrEmpty(password) || password.Length < 12) return false;
            return password.Any(char.IsLower)
                   && password.Any(char.IsUpper)
                   && password.Any(char.IsDigit)
                   && password.Any(ch => !char.IsLetterOrDigit(ch));
        }

        /// <summary>
        /// 生成符合复杂度要求的随机密码（默认 32 位以上，含大小写/数字/符号）。
        /// </summary>
        public static string GenerateRandomPassword(int length = 32)
        {
            if (length < 12) length = 32;
            const string lower = "abcdefghijklmnopqrstuvwxyz";
            const string upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string digits = "0123456789";
            const string symbols = "!@#$%^&*()-_=+[]{};:,.<>?";
            var all = lower + upper + digits + symbols;
            var buf = new byte[length];
            EncryptTools.Compat.RngFill(buf);
            var sb = new StringBuilder(length);
            sb.Append(lower[buf[0] % lower.Length]);
            sb.Append(upper[buf[1] % upper.Length]);
            sb.Append(digits[buf[2] % digits.Length]);
            sb.Append(symbols[buf[3] % symbols.Length]);
            for (int i = 4; i < length; i++)
                sb.Append(all[buf[i] % all.Length]);
            return sb.ToString();
        }

        /// <summary>
        /// 系统派生密码：至少 20 位，基于机器名+用户名+固定盐的简单派生（仅用于“系统派生”选项，非加密强度）。
        /// </summary>
        public static string GenerateSystemDerivedPassword()
        {
            var raw = Environment.MachineName + "|" + Environment.UserName + "|encryptTools.pwd.salt";
            var bytes = Encoding.UTF8.GetBytes(raw);
            var hash = EncryptTools.Compat.Sha256Hash(bytes);
            var s = Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            return s.Length >= 20 ? s : s + GenerateRandomPassword(20 - s.Length);
        }

        /// <summary>
        /// 随机文件名（不含路径），扩展名 .pwd。
        /// </summary>
        public static string GenerateRandomFileName()
        {
            var buf = new byte[12];
            EncryptTools.Compat.RngFill(buf);
            return EncryptTools.Compat.ToHexString(buf) + ".pwd";
        }
    }
}
