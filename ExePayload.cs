using System;
using System.IO;
using System.Text;

namespace EncryptTools
{
    /// <summary>
    /// 封装/读取 exe 尾部载荷。使用纯二进制格式，避免 System.Text.Json 带来的 System.Memory 等依赖，确保打包 exe 在目标机解密时无需额外程序集。
    /// </summary>
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

            var typeBytes = Encoding.UTF8.GetBytes(meta.Type ?? "file");
            var noteBytes = Encoding.UTF8.GetBytes(meta.Note ?? "");

            using var inFs = new FileStream(templateExePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var outFs = new FileStream(outputExePath, FileMode.Create, FileAccess.Write, FileShare.None);

            inFs.CopyTo(outFs);
            long payloadOffset = outFs.Position;

            using (var bw = new BinaryWriter(outFs, Encoding.UTF8, leaveOpen: true))
            {
                bw.Write(meta.Version);
                bw.Write(typeBytes.Length);
                bw.Write(typeBytes);
                bw.Write(noteBytes.Length);
                bw.Write(noteBytes);
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
            return TryReadPayload(exePath, out meta, out encryptedBytes, out _);
        }

        public static bool TryReadPayload(string exePath, out PayloadMeta? meta, out byte[]? encryptedBytes, out string? errorReason)
        {
            meta = null;
            encryptedBytes = null;
            errorReason = null;
            try
            {
                using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length < FooterSize) { errorReason = "文件过短"; return false; }
                fs.Seek(-FooterSize, SeekOrigin.End);
                var footer = new byte[FooterSize];
                if (fs.Read(footer, 0, footer.Length) != footer.Length) { errorReason = "无法读取尾部"; return false; }
                for (int i = 0; i < FooterMagic.Length; i++)
                    if (footer[i] != FooterMagic[i]) { errorReason = "尾部魔数不匹配"; return false; }
                long payloadOffset = BitConverter.ToInt64(footer, 8);
                int payloadLen = BitConverter.ToInt32(footer, 16);

                if (payloadOffset < 0 || payloadLen <= 0) { errorReason = "尾部偏移或长度无效"; return false; }
                long maxPayloadEnd = fs.Length - FooterSize;
                if (payloadOffset + payloadLen > maxPayloadEnd) { errorReason = $"载荷范围越界(offset={payloadOffset} len={payloadLen} fileEnd={maxPayloadEnd})"; return false; }

                fs.Seek(payloadOffset, SeekOrigin.Begin);
                int toRead = payloadLen > 8192 ? 8192 : payloadLen;
                var headerBuf = new byte[toRead];
                int got = fs.Read(headerBuf, 0, headerBuf.Length);
                if (got < 24) { errorReason = "载荷头过短"; return false; }

                int version = BitConverter.ToInt32(headerBuf, 0);
                int typeLen = BitConverter.ToInt32(headerBuf, 4);
                if (typeLen >= 0 && typeLen <= 256 && 8 + typeLen <= got)
                {
                    int noteOff = 8 + typeLen;
                    if (noteOff + 4 <= got)
                    {
                        int noteLen = BitConverter.ToInt32(headerBuf, noteOff);
                        if (noteLen >= 0 && noteLen <= 4096)
                        {
                            int dataOff = noteOff + 4 + noteLen;
                            if (dataOff + 4 <= got)
                            {
                                int dataLen = BitConverter.ToInt32(headerBuf, dataOff);
                                if (dataLen >= 0 && dataLen <= 1024 * 1024 * 1024 && (long)dataOff + 4 + dataLen <= payloadLen)
                                {
                                    if (dataLen == 0) { errorReason = "载荷数据长度为0"; return false; }
                                    string type = typeLen > 0 ? Encoding.UTF8.GetString(headerBuf, 8, typeLen) : "file";
                                    string note = (noteLen > 0 && noteOff + 4 + noteLen <= got)
                                        ? Encoding.UTF8.GetString(headerBuf, noteOff + 4, noteLen) : "";
                                    encryptedBytes = new byte[dataLen];
                                    fs.Seek(payloadOffset + dataOff + 4, SeekOrigin.Begin);
                                    int read = 0;
                                    while (read < dataLen)
                                    {
                                        int n = fs.Read(encryptedBytes, read, dataLen - read);
                                        if (n <= 0) { errorReason = "读取密文不完整"; return false; }
                                        read += n;
                                    }
                                    meta = new PayloadMeta { Version = version, Type = type, Note = note };
                                    return true;
                                }
                            }
                        }
                    }
                }
                // 旧格式（JSON）：[jsonLen:4][json][dataLen:4][data]
                int jsonLen = BitConverter.ToInt32(headerBuf, 0);
                if (jsonLen >= 20 && jsonLen <= 1024 && 4 + jsonLen + 4 <= payloadLen)
                {
                    fs.Seek(payloadOffset + 4 + jsonLen, SeekOrigin.Begin);
                    var lenBuf = new byte[4];
                    if (fs.Read(lenBuf, 0, 4) != 4) { errorReason = "旧格式读取dataLen失败"; return false; }
                    int dataLen = BitConverter.ToInt32(lenBuf, 0);
                    if (dataLen > 0 && dataLen <= 1024 * 1024 * 1024 && 4L + jsonLen + 4 + dataLen <= payloadLen)
                    {
                        encryptedBytes = new byte[dataLen];
                        int read = 0;
                        while (read < dataLen)
                        {
                            int n = fs.Read(encryptedBytes, read, dataLen - read);
                            if (n <= 0) { errorReason = "旧格式读取密文不完整"; return false; }
                            read += n;
                        }
                        meta = new PayloadMeta { Version = 1, Type = "file", Note = "" };
                        return true;
                    }
                }
                errorReason = "无法识别载荷格式(version=" + version + " typeLen=" + typeLen + ")";
                return false;
            }
            catch (Exception ex)
            {
                errorReason = ex.Message ?? "读取异常";
                return false;
            }
        }
    }
}

