using System;
using System.IO;
using Avalonia.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EncryptTools.Desktop.Imaging;

public static class ImageBitmapLoader
{
    /// <summary>解码为 Avalonia Bitmap（跨平台；避免部分环境直接 new Bitmap(path) 失败）。</summary>
    public static Bitmap? LoadAvaloniaBitmap(string path, int? maxDimension = null)
    {
        try
        {
            using var img = Image.Load<Rgba32>(path);
            if (maxDimension is > 0 and int cap && (img.Width > cap || img.Height > cap))
            {
                var w = img.Width;
                var h = img.Height;
                var scale = Math.Min((double)cap / w, (double)cap / h);
                var nw = Math.Max(1, (int)(w * scale));
                var nh = Math.Max(1, (int)(h * scale));
                img.Mutate(x => x.Resize(nw, nh));
            }

            using var ms = new MemoryStream();
            img.SaveAsPng(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>由内存中的 ImageSharp 图像生成 Avalonia Bitmap（用于加解密预览）。</summary>
    public static Bitmap? LoadAvaloniaBitmapFromImage(Image<Rgba32> img, int? maxDimension = null)
    {
        try
        {
            using var work = img.Clone();
            if (maxDimension is > 0 and int cap && (work.Width > cap || work.Height > cap))
            {
                var w = work.Width;
                var h = work.Height;
                var scale = Math.Min((double)cap / w, (double)cap / h);
                var nw = Math.Max(1, (int)(w * scale));
                var nh = Math.Max(1, (int)(h * scale));
                work.Mutate(x => x.Resize(nw, nh));
            }

            using var ms = new MemoryStream();
            work.SaveAsPng(ms);
            ms.Position = 0;
            return new Bitmap(ms);
        }
        catch
        {
            return null;
        }
    }
}
