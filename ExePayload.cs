using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace EncryptTools
{
    internal static class ExePayload
    {
        // Footer: [magic(8)][payloadOffset(Int64)][payloadLength(Int32)][reserved(Int32)]
        private static readonly byte[] FooterMagic = Encoding.ASCII.GetBytes("ETPKv001"); // 8 bytes
        private const int FooterSize = 8 + 8 + 4 + 4;

        internal sealed class PayloadMeta
        {
            public int Version { get; set; } = 1;
            public string Type { get; set; } = "file";
            public string? Note { get; set; }
        }

        public static bool HasPayload(string exePath)
        {
            try
            {
                using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < FooterSize) return false;
                fs.Seek(-FooterSize, SeekOrigin.End);
                var footer = new byte[FooterSize];
                if (fs.Read(footer, 0, footer.Length) != footer.Length) return false;
                for (int i = 0; i < FooterMagic.Length; i++)
                    if (footer[i] != FooterMagic[i]) return false;
                return true;
            }
            catch { return false; }
        }

        public static void WritePackedExe(string templateExePath, string outputExePath, PayloadMeta meta, byte[] encryptedBytes)
        {
            if (string.IsNullOrWhiteSpace(templateExePath)) throw new ArgumentException(nameof(templateExePath));
            if (string.IsNullOrWhiteSpace(outputExePath)) throw new ArgumentException(nameof(outputExePath));
            if (meta == null) throw new ArgumentNullException(nameof(meta));
            if (encryptedBytes == null) throw new ArgumentNullException(nameof(encryptedBytes));

            var json = JsonSerializer.SerializeToUtf8Bytes(meta);
            using var inFs = new FileStream(templateExePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var outFs = new FileStream(outputExePath, FileMode.Create, FileAccess.Write, FileShare.None);

            inFs.CopyTo(outFs);
            long payloadOffset = outFs.Position;

            using (var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(json.Length);
                bw.Write(json);
                bw.Write(encryptedBytes.Length);
                bw.Write(encryptedBytes);
            }
            long payloadEnd = outFs.Position;
            int payloadLen = checked((int)(payloadEnd - payloadOffset));

            using (var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(FooterMagic);
                bw.Write(payloadOffset);
                bw.Write(payloadLen);
                bw.Write(0);
            }
        }

        public static bool TryReadPayload(string exePath, out PayloadMeta? meta, out byte[]? encryptedBytes)
        {
            meta = null;
            encryptedBytes = null;
            try
            {
                using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < FooterSize) return false;
                fs.Seek(-FooterSize, SeekOrigin.End);
                using var brFooter = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
                var magic = brFooter.ReadBytes(8);
                for (int i = 0; i < FooterMagic.Length; i++)
                    if (magic[i] != FooterMagic[i]) return false;
                long payloadOffset = brFooter.ReadInt64();
                int payloadLen = brFooter.ReadInt32();
                _ = brFooter.ReadInt32(); // reserved

                if (payloadOffset < 0 || payloadLen <= 0) return false;
                if (payloadOffset + payloadLen > fs.Length - FooterSize) return false;

                fs.Seek(payloadOffset, SeekOrigin.Begin);
                using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);
                int jsonLen = br.ReadInt32();
                if (jsonLen <= 0 || jsonLen > 1024 * 1024) return false;
                var json = br.ReadBytes(jsonLen);
                meta = JsonSerializer.Deserialize<PayloadMeta>(json);
                int dataLen = br.ReadInt32();
                if (dataLen <= 0 || dataLen > 1024 * 1024 * 1024) return false;
                encryptedBytes = br.ReadBytes(dataLen);
                return encryptedBytes.Length == dataLen;
            }
            catch { return false; }
        }
    }
}

