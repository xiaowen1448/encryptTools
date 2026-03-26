using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EncryptTools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Transforms;

namespace EncryptTools.Desktop.ImageWork;

/// <summary>
/// 与 Windows GDI+ 版像素管线一致：32bpp ARGB 字节序为 BGRA，与 Windows LockBits 一致。
/// </summary>
public static class ImageSharpPixelEffects
{
    private static int BitmapStride(int width) => ((width * 32 + 31) / 32) * 4;

    private static byte[] DeriveKey(string password, ImageEffectOptions options, int keyLen)
    {
        var salt = Convert.FromBase64String(options.SaltBase64);
        using var kdf = new Rfc2898DeriveBytes(password, salt, options.Iterations, HashAlgorithmName.SHA256);
        return kdf.GetBytes(keyLen);
    }

    public static Image<Rgba32> ApplyPixelEffect(Image<Rgba32> src, ImageEffectOptions options, string? password, bool encrypt)
    {
        var bmp = src.Clone();
        if (options.PixelationEnabled != true)
            return bmp;

        return options.Mode switch
        {
            ImageMode.Mosaic => ApplyMosaic(bmp, options.BlockSize),
            ImageMode.Permutation => ApplyPermutation(bmp, options, password, encrypt),
            ImageMode.XorStream => ApplyXorStream(bmp, options, password),
            ImageMode.BlockShuffle => ApplyBlockShuffle(bmp, options, password, encrypt),
            ImageMode.ArnoldCat => ApplyArnoldCat(bmp, options, password, encrypt),
            _ => bmp
        };
    }

    private static Image<Rgba32> ApplyMosaic(Image<Rgba32> bmp, int block)
    {
        int w = bmp.Width, h = bmp.Height;
        for (int y = 0; y < h; y += block)
        {
            for (int x = 0; x < w; x += block)
            {
                int r = 0, g = 0, b = 0, n = 0;
                for (int dy = 0; dy < block && y + dy < h; dy++)
                for (int dx = 0; dx < block && x + dx < w; dx++)
                {
                    var c = bmp[x + dx, y + dy];
                    r += c.R; g += c.G; b += c.B; n++;
                }
                if (n > 0)
                {
                    var avg = new Rgba32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)255);
                    for (int dy = 0; dy < block && y + dy < h; dy++)
                    for (int dx = 0; dx < block && x + dx < w; dx++)
                        bmp[x + dx, y + dy] = avg;
                }
            }
        }
        return bmp;
    }

    private static Image<Rgba32> ApplyPermutation(Image<Rgba32> bmp, ImageEffectOptions options, string? password, bool encrypt)
    {
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
        var key = DeriveKey(password, options, 32);
        int seed = BitConverter.ToInt32(Compat.Sha256Hash(key)!, 0);
        return PermutePixels(bmp, seed, encrypt);
    }

    private static Image<Rgba32> PermutePixels(Image<Rgba32> bmp, int seed, bool encrypt)
    {
        int w = bmp.Width, h = bmp.Height;
        int stride = BitmapStride(w);
        int len = stride * h;
        var buf = new byte[len];
        CopyImageToBgraBuffer(bmp, buf, stride);

        int nPixels = w * h;
        var perm = new int[nPixels];
        for (int i = 0; i < nPixels; i++) perm[i] = i;
        var rng = new Random(seed);
        for (int i = nPixels - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (perm[i], perm[j]) = (perm[j], perm[i]);
        }

        var outBuf = new byte[len];
        if (encrypt)
        {
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                int srcIdx = perm[idx];
                int srcX = srcIdx % w;
                int srcY = srcIdx / w;
                int srcOff = srcY * stride + srcX * 4;
                int dstOff = y * stride + x * 4;
                Buffer.BlockCopy(buf, srcOff, outBuf, dstOff, 4);
            }
        }
        else
        {
            var inv = new int[nPixels];
            for (int i = 0; i < nPixels; i++) inv[perm[i]] = i;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                int srcIdx = inv[idx];
                int srcX = srcIdx % w;
                int srcY = srcIdx / w;
                int srcOff = srcY * stride + srcX * 4;
                int dstOff = y * stride + x * 4;
                Buffer.BlockCopy(buf, srcOff, outBuf, dstOff, 4);
            }
        }

        CopyBgraBufferToImage(outBuf, stride, bmp);
        return bmp;
    }

    private static Image<Rgba32> ApplyXorStream(Image<Rgba32> bmp, ImageEffectOptions options, string? password)
    {
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
        var key = DeriveKey(password, options, 32);
        int ver = options.PixelXorVersion >= 2 ? 2 : 1;
        return XorPixels(bmp, key, ver);
    }

    private static Image<Rgba32> XorPixels(Image<Rgba32> bmp, byte[] key, int pixelXorVersion)
    {
        int w = bmp.Width, h = bmp.Height;
        int stride = BitmapStride(w);
        int len = stride * h;
        var buf = new byte[len];
        CopyImageToBgraBuffer(bmp, buf, stride);

        using var hmac = new HMACSHA256(key);
        ulong ctr = 0;
        Span<byte> counterSpan = stackalloc byte[8];

        if (pixelXorVersion >= 2)
        {
            byte[] macBlock = Array.Empty<byte>();
            int macPos = 0;
            for (int y = 0; y < h; y++)
            {
                int rowStart = y * stride;
                for (int x = 0; x < w; x++)
                {
                    for (int c = 0; c < 3; c++)
                    {
                        if (macPos >= macBlock.Length)
                        {
                            BitConverter.TryWriteBytes(counterSpan, ctr++);
                            macBlock = hmac.ComputeHash(counterSpan.ToArray());
                            macPos = 0;
                        }
                        buf[rowStart + x * 4 + c] ^= macBlock[macPos++];
                    }
                }
            }
        }
        else
        {
            int offset = 0;
            while (offset < len)
            {
                BitConverter.TryWriteBytes(counterSpan, ctr++);
                var mac = hmac.ComputeHash(counterSpan.ToArray());
                int take = Math.Min(mac.Length, len - offset);
                for (int i = 0; i < take; i++)
                    buf[offset + i] ^= mac[i];
                offset += take;
            }
        }

        CopyBgraBufferToImage(buf, stride, bmp);
        return bmp;
    }

    private static Image<Rgba32> ApplyBlockShuffle(Image<Rgba32> bmp, ImageEffectOptions options, string? password, bool encrypt)
    {
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
        var key = DeriveKey(password, options, 32);
        int seed = BitConverter.ToInt32(Compat.Sha256Hash(key)!, 0);
        return ShuffleBlocks(bmp, options.BlockSize, seed, encrypt);
    }

    private static Image<Rgba32> ShuffleBlocks(Image<Rgba32> bmp, int block, int seed, bool encrypt)
    {
        int w = bmp.Width, h = bmp.Height;
        int bx = (w + block - 1) / block;
        int by = (h + block - 1) / block;
        int n = bx * by;
        var perm = new int[n];
        for (int i = 0; i < n; i++) perm[i] = i;
        var groups = new Dictionary<(int bw, int bh), List<int>>();
        for (int bi = 0; bi < n; bi++)
        {
            int xb = bi % bx, yb = bi / bx;
            int x0 = xb * block, y0 = yb * block;
            var key = (Math.Min(block, w - x0), Math.Min(block, h - y0));
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<int>();
                groups[key] = list;
            }
            list.Add(bi);
        }
        foreach (var kv in groups)
        {
            var idxs = kv.Value.ToArray();
            int m = idxs.Length;
            if (m <= 1) continue;
            int subSeed = seed ^ (kv.Key.bw * 73856093 ^ kv.Key.bh * 19349663);
            var rng = new Random(subSeed);
            var shuffled = (int[])idxs.Clone();
            for (int i = m - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
            }
            for (int i = 0; i < m; i++)
                perm[idxs[i]] = shuffled[i];
        }

        int[] map;
        if (encrypt) map = perm;
        else
        {
            var inv = new int[n];
            for (int i = 0; i < n; i++) inv[perm[i]] = i;
            map = inv;
        }

        int stride = BitmapStride(w);
        int len = stride * h;
        var srcBuf = new byte[len];
        CopyImageToBgraBuffer(bmp, srcBuf, stride);
        var dstBuf = new byte[len];

        for (int yb = 0; yb < by; yb++)
        for (int xb = 0; xb < bx; xb++)
        {
            int idx = yb * bx + xb;
            int srcIdx = map[idx];
            int srcXb = srcIdx % bx;
            int srcYb = srcIdx / bx;

            int x0 = xb * block;
            int y0 = yb * block;
            int sx0 = srcXb * block;
            int sy0 = srcYb * block;
            int bw = Math.Min(block, w - x0);
            int bh = Math.Min(block, h - y0);

            for (int dy = 0; dy < bh; dy++)
            {
                int srcOff = (sy0 + dy) * stride + sx0 * 4;
                int dstOff = (y0 + dy) * stride + x0 * 4;
                Buffer.BlockCopy(srcBuf, srcOff, dstBuf, dstOff, bw * 4);
            }
        }

        CopyBgraBufferToImage(dstBuf, stride, bmp);
        return bmp;
    }

    private static Image<Rgba32> ApplyArnoldCat(Image<Rgba32> bmp, ImageEffectOptions options, string? password, bool encrypt)
    {
        if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
        if (bmp.Width != bmp.Height)
            throw new InvalidOperationException("Arnold 仅支持正方形图片。");
        _ = options;
        _ = password;
        return ArnoldScramble(bmp, encrypt, 10);
    }

    private static Image<Rgba32> ArnoldScramble(Image<Rgba32> bmp, bool encrypt, int iterations)
    {
        int N = bmp.Width;
        if (N != bmp.Height) return bmp;
        var outImg = bmp.Clone();
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            int nx = x, ny = y;
            for (int k = 0; k < iterations; k++)
            {
                if (encrypt)
                {
                    int tx = (nx + ny) % N;
                    int ty = (nx + 2 * ny) % N;
                    nx = tx; ny = ty;
                }
                else
                {
                    int tx = (2 * nx - ny + N * 2) % N;
                    int ty = (-nx + ny + N * 2) % N;
                    nx = tx; ny = ty;
                }
            }
            outImg[nx, ny] = bmp[x, y];
        }
        bmp.Dispose();
        return outImg;
    }

    private static void CopyImageToBgraBuffer(Image<Rgba32> img, byte[] buf, int stride)
    {
        int w = img.Width, h = img.Height;
        for (int y = 0; y < h; y++)
        {
            int off = y * stride;
            for (int x = 0; x < w; x++)
            {
                var p = img[x, y];
                buf[off + x * 4 + 0] = p.B;
                buf[off + x * 4 + 1] = p.G;
                buf[off + x * 4 + 2] = p.R;
                buf[off + x * 4 + 3] = p.A;
            }
        }
    }

    private static void CopyBgraBufferToImage(byte[] buf, int stride, Image<Rgba32> img)
    {
        int w = img.Width, h = img.Height;
        for (int y = 0; y < h; y++)
        {
            int off = y * stride;
            for (int x = 0; x < w; x++)
            {
                img[x, y] = new Rgba32(buf[off + x * 4 + 2], buf[off + x * 4 + 1], buf[off + x * 4 + 0], buf[off + x * 4 + 3]);
            }
        }
    }

    /// <summary>与 Windows 版相同的图标遮挡（先采集块再绘制），并写入 options 中的加密块元数据。</summary>
    public static void ApplyIconOverlay(
        Image<Rgba32> target,
        ImageEffectOptions options,
        IReadOnlyList<string> iconPaths,
        string? password)
    {
        if (target.Width <= 0 || target.Height <= 0 || !options.IconOverlayEnabled) return;
        var icons = new List<Image<Rgba32>>();
        foreach (var p in iconPaths)
        {
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
            try
            {
                var img = Image.Load<Rgba32>(p);
                icons.Add(img);
            }
            catch { }
        }
        if (icons.Count == 0) return;

        int w = target.Width, h = target.Height;
        int block = Math.Max(4, options.IconOverlayBlockSizeHint);
        block = ((block + 3) / 4) * 4;
        block = Math.Min(block, Math.Min(w, h));
        int bx = (w + block - 1) / block;
        int by = (h + block - 1) / block;
        int totalBlocks = bx * by;
        var blockBytes = new List<byte>();
        for (int idx = 0; idx < totalBlocks; idx++)
        {
            int xb = idx % bx, yb = idx / bx;
            int x0 = xb * block, y0 = yb * block;
            int bw = Math.Min(block, w - x0), bh = Math.Min(block, h - y0);
            if (bw <= 0 || bh <= 0) continue;
            for (int dy = 0; dy < bh; dy++)
            for (int dx = 0; dx < bw; dx++)
            {
                var c = target[x0 + dx, y0 + dy];
                blockBytes.Add(c.B);
                blockBytes.Add(c.G);
                blockBytes.Add(c.R);
                blockBytes.Add(c.A);
            }
        }

        float alpha = Math.Max(0.01f, Math.Min(1f, options.OverlayOpacityPercent / 100f));
        var rnd = new Random(unchecked(Environment.TickCount * 397) ^ w ^ (h << 16));
        bool randomize = options.IconRandomize;

        for (int idx = 0; idx < totalBlocks; idx++)
        {
            int xb = idx % bx, yb = idx / bx;
            int x0 = xb * block, y0 = yb * block;
            int bw = Math.Min(block, w - x0), bh = Math.Min(block, h - y0);
            if (bw <= 0 || bh <= 0) continue;
            var icon = icons[rnd.Next(icons.Count)];

            if (randomize)
            {
                float angle = (float)(rnd.NextDouble() * 360);
                float offX = (float)(rnd.NextDouble() - 0.5) * block * 0.6f;
                float offY = (float)(rnd.NextDouble() - 0.5) * block * 0.6f;
                float scale = 0.8f + (float)(rnd.NextDouble() * 0.6);
                int dw = Math.Max(1, (int)(bw * scale));
                int dh = Math.Max(1, (int)(bh * scale));

                using var transformed = icon.Clone(ctx =>
                {
                    ctx.Resize(dw, dh);
                    ctx.Rotate(angle);
                });
                int tw = transformed.Width, th = transformed.Height;
                int cx = x0 + bw / 2 + (int)offX;
                int cy = y0 + bh / 2 + (int)offY;
                int sx = cx - tw / 2, sy = cy - th / 2;
                for (int dy = 0; dy < th; dy++)
                for (int dx = 0; dx < tw; dx++)
                {
                    int px = sx + dx, py = sy + dy;
                    if (px < 0 || px >= w || py < 0 || py >= h) continue;
                    var p = transformed[dx, dy];
                    if (p.A == 0) continue;
                    var t = target[px, py];
                    float sa = (p.A / 255f) * alpha;
                    float inv2 = 1f - sa;
                    target[px, py] = new Rgba32(
                        (byte)Math.Clamp(t.R * inv2 + p.R * sa, 0, 255),
                        (byte)Math.Clamp(t.G * inv2 + p.G * sa, 0, 255),
                        (byte)Math.Clamp(t.B * inv2 + p.B * sa, 0, 255),
                        (byte)Math.Clamp(t.A * inv2 + p.A * sa, 0, 255));
                }
            }
            else
            {
                using var scaled = icon.Clone(ctx => ctx.Resize(bw, bh));
                for (int dy = 0; dy < bh; dy++)
                for (int dx = 0; dx < bw; dx++)
                {
                    var p = scaled[dx, dy];
                    var t = target[x0 + dx, y0 + dy];
                    float sa = (p.A / 255f) * alpha;
                    float inv2 = 1f - sa;
                    target[x0 + dx, y0 + dy] = new Rgba32(
                        (byte)Math.Clamp(t.R * inv2 + p.R * sa, 0, 255),
                        (byte)Math.Clamp(t.G * inv2 + p.G * sa, 0, 255),
                        (byte)Math.Clamp(t.B * inv2 + p.B * sa, 0, 255),
                        (byte)Math.Clamp(t.A * inv2 + p.A * sa, 0, 255));
                }
            }
        }

        foreach (var ic in icons) ic.Dispose();

        if (blockBytes.Count > 0 && !string.IsNullOrEmpty(password))
        {
            var encrypted = EncryptBlockData(password, options.SaltBase64 ?? "", blockBytes.ToArray());
            if (encrypted != null)
            {
                options.IconOverlayBlocksEncryptedBase64 = Convert.ToBase64String(encrypted);
                options.IconOverlayBlockSize = block;
            }
        }
    }

    public static byte[]? EncryptBlockData(string password, string saltBase64, byte[] data)
    {
        if (string.IsNullOrEmpty(password) || data == null || data.Length == 0) return null;
        try
        {
            var salt = Convert.FromBase64String(saltBase64 ?? "");
            if (salt.Length < 8) salt = Encoding.UTF8.GetBytes("IconOverlayBlocks");
            using var kdf = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            var key = kdf.GetBytes(32);
            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();
            using var enc = aes.CreateEncryptor();
            var iv = aes.IV;
            var encrypted = enc.TransformFinalBlock(data, 0, data.Length);
            var result = new byte[iv.Length + encrypted.Length];
            Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
            Buffer.BlockCopy(encrypted, 0, result, iv.Length, encrypted.Length);
            return result;
        }
        catch { return null; }
    }

    public static byte[]? DecryptBlockData(string password, string saltBase64, byte[] encryptedWithIv)
    {
        if (string.IsNullOrEmpty(password) || encryptedWithIv == null || encryptedWithIv.Length < 16) return null;
        try
        {
            var salt = Convert.FromBase64String(saltBase64 ?? "");
            if (salt.Length < 8) salt = Encoding.UTF8.GetBytes("IconOverlayBlocks");
            using var kdf = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
            var key = kdf.GetBytes(32);
            using var aes = Aes.Create();
            aes.Key = key;
            var iv = new byte[16];
            Buffer.BlockCopy(encryptedWithIv, 0, iv, 0, 16);
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            return dec.TransformFinalBlock(encryptedWithIv, 16, encryptedWithIv.Length - 16);
        }
        catch { return null; }
    }

    public static bool RestoreIconOverlayBlocks(Image<Rgba32> bmp, byte[] blockData, int blockSize)
    {
        if (blockData == null || blockSize < 4) return false;
        int w = bmp.Width, h = bmp.Height;
        int bx = (w + blockSize - 1) / blockSize;
        int by = (h + blockSize - 1) / blockSize;
        int offset = 0;
        for (int idx = 0; idx < bx * by && offset < blockData.Length; idx++)
        {
            int xb = idx % bx, yb = idx / bx;
            int x0 = xb * blockSize, y0 = yb * blockSize;
            int bw = Math.Min(blockSize, w - x0), bh = Math.Min(blockSize, h - y0);
            int need = bw * bh * 4;
            if (offset + need > blockData.Length) break;
            for (int dy = 0; dy < bh; dy++)
            for (int dx = 0; dx < bw; dx++)
            {
                int o = offset + (dy * bw + dx) * 4;
                bmp[x0 + dx, y0 + dy] = new Rgba32(blockData[o + 2], blockData[o + 1], blockData[o + 0], blockData[o + 3]);
            }
            offset += need;
        }
        return true;
    }

    /// <summary>解密流程：先按元数据恢复遮挡块，再反向像素算法。</summary>
    public static Image<Rgba32> DecryptPipeline(
        Image<Rgba32> encryptedImage,
        ImageEffectOptions options,
        string password)
    {
        Image<Rgba32> work = encryptedImage.Clone();
        if (!string.IsNullOrEmpty(options.IconOverlayBlocksEncryptedBase64) &&
            options.IconOverlayBlockSize >= 4)
        {
            try
            {
                var enc = Convert.FromBase64String(options.IconOverlayBlocksEncryptedBase64);
                var blockData = DecryptBlockData(password, options.SaltBase64 ?? "", enc);
                if (blockData != null && blockData.Length > 0)
                        RestoreIconOverlayBlocks(work, blockData, options.IconOverlayBlockSize);
            }
            catch { }
        }

        return ApplyPixelEffect(work, options, password, encrypt: false);
    }
}
