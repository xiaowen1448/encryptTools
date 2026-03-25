using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace EncryptTools.Desktop.Input;

/// <summary>
/// Linux 桌面常通过 FileNames / Text（file:// 多行）传递拖放，而不一定填充 DataFormats.Files。
/// X11/Wayland 常见 <c>text/uri-list</c>，需单独解析。
/// </summary>
public static class DragDropPaths
{
    /// <summary>部分环境 <see cref="IStorageItem.TryGetLocalPath"/> 为空，尝试从 Uri 恢复本地路径。</summary>
    public static string? TryGetPathFromStorageItem(IStorageItem? item)
    {
        if (item == null) return null;
        try
        {
            var p = item.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p))
                return p;
        }
        catch { }
        try
        {
            var prop = item.GetType().GetProperty("Path");
            var val = prop?.GetValue(item);
            if (val is Uri u && u.IsAbsoluteUri)
            {
                if (u.IsFile)
                {
                    var local = Uri.UnescapeDataString(u.LocalPath);
                    if (!string.IsNullOrEmpty(local) && (File.Exists(local) || Directory.Exists(local)))
                        return local;
                }
                else if (u.IsLoopback == false && u.Scheme == Uri.UriSchemeFile)
                {
                    var local = Uri.UnescapeDataString(u.LocalPath);
                    if (!string.IsNullOrEmpty(local) && (File.Exists(local) || Directory.Exists(local)))
                        return local;
                }
            }
        }
        catch { }
        return null;
    }

    private static bool SafeContains(IDataObject data, string format)
    {
        try { return data.Contains(format); }
        catch { return false; }
    }

    private static List<string> GetDataFormatsSafe(IDataObject data)
    {
        try
        {
            var fmts = data.GetDataFormats();
            if (fmts == null) return new List<string>();
            var list = new List<string>();
            foreach (var f in fmts)
            {
                if (!string.IsNullOrEmpty(f))
                    list.Add(f);
            }
            return list;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static string? TryGetStringPayload(object? o)
    {
        if (o is null) return null;
        if (o is string s) return s;
        if (o is byte[] b)
        {
            if (b.Length == 0) return null;
            try
            {
                if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
                    return Encoding.Unicode.GetString(b, 2, b.Length - 2);
                if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
                    return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
            }
            catch { /* try UTF-8 */ }
            try { return Encoding.UTF8.GetString(b); }
            catch { return null; }
        }
        return o.ToString();
    }

    private static string? TryResolveFileUriToExistingPath(string p)
    {
        try
        {
            var uri = new Uri(p);
            var local = Uri.UnescapeDataString(uri.LocalPath);
            if (string.IsNullOrEmpty(local)) return null;
            var n = NormalizePath(local);
            if (File.Exists(n) || Directory.Exists(n))
                return n;
        }
        catch { }
        return null;
    }

    private static IEnumerable<string> PathsFromUriListText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = line.Trim();
            if (p.Length == 0 || p[0] == '#') continue;
            if (p.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                var n = TryResolveFileUriToExistingPath(p);
                if (n != null) yield return n;
            }
            else
            {
                var n = NormalizePath(p);
                if (File.Exists(n) || Directory.Exists(n))
                    yield return n;
            }
        }
    }

    private static object? GetDataPayloadSafe(IDataObject data, string format)
    {
        try { return data.Get(format); }
        catch { return null; }
    }

    private static IEnumerable<string> TryPathsFromFormat(IDataObject data, string format)
    {
        var payload = GetDataPayloadSafe(data, format);
        var text = TryGetStringPayload(payload);
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (var p in PathsFromUriListText(text))
            yield return p;
    }

    /// <summary>
    /// 用于 DragOver：Linux/Wayland 上此时可能尚不能解析出路径，但应显示可放置。
    /// 勿在此处做完整 <see cref="GetPathsFromData"/>（开销大），除非其它启发式均失败。
    /// </summary>
    public static bool LooksLikeFileDrop(IDataObject data)
    {
        try
        {
            var files = data.GetFiles();
            if (files != null)
            {
                foreach (var f in files)
                {
                    if (f is not IStorageItem item) continue;
                    if (!string.IsNullOrEmpty(TryGetPathFromStorageItem(item)))
                        return true;
                }
            }
        }
        catch { /* ignore */ }

        foreach (var fmt in GetDataFormatsSafe(data))
        {
            if (string.IsNullOrEmpty(fmt)) continue;
            var lf = fmt.ToLowerInvariant();
            if (lf.Contains("uri-list") || lf.Contains("text/plain") || lf.Contains("files") ||
                lf.Contains("gnome") || lf.Contains("xdnd") || lf.Contains("x-special") ||
                lf.Contains("text/") || lf.Contains("url") ||
                (lf.Contains("kde") && lf.Contains("uri")))
                return true;
        }

        if (SafeContains(data, DataFormats.FileNames))
            return true;

        try
        {
            if (SafeContains(data, DataFormats.Text))
            {
                var t = data.GetText();
                if (!string.IsNullOrEmpty(t) &&
                    ((t.Length > 1 && t[0] == '/') || t.IndexOf("file:", StringComparison.OrdinalIgnoreCase) >= 0))
                    return true;
            }
        }
        catch { /* ignore */ }

        try
        {
            return GetPathsFromData(data).Any();
        }
        catch
        {
            return false;
        }
    }

    public static IEnumerable<string> GetPathsFromData(IDataObject data)
    {
        // 直接 GetFiles：部分 Linux 后端 Contains(DataFormats.Files) 为假但 Get 仍有数据
        // 不能在带 catch 的 try 内 yield return（CS1626），先收集再枚举
        List<string>? fromGetFiles = null;
        try
        {
            var files = data.GetFiles();
            if (files != null)
            {
                fromGetFiles = new List<string>();
                foreach (var f in files)
                {
                    if (f is not IStorageItem item) continue;
                    var p = TryGetPathFromStorageItem(item);
                    if (!string.IsNullOrEmpty(p))
                        fromGetFiles.Add(p);
                }
            }
        }
        catch { /* ignore */ }

        if (fromGetFiles != null)
        {
            foreach (var p in fromGetFiles)
                yield return p;
        }

        List<string>? fromFileNames = null;
        try
        {
#pragma warning disable CS0618 // Linux 等环境仍依赖 FileNames；Contains 可能为假但 Get 仍有数据
            var names = data.GetFileNames();
#pragma warning restore CS0618
            if (names != null)
            {
                fromFileNames = new List<string>();
                foreach (var raw in names)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    var n = NormalizePath(raw);
                    if (File.Exists(n) || Directory.Exists(n))
                        fromFileNames.Add(n);
                }
            }
        }
        catch { /* ignore */ }

        if (fromFileNames != null)
        {
            foreach (var p in fromFileNames)
                yield return p;
        }

        // 常见 MIME：不依赖 Contains，直接 Get（部分后端 Contains 不可靠）
        foreach (var fmt in new[] { "text/uri-list", "text/uri-list;charset=utf-8", "text/plain", "text/plain;charset=utf-8" })
        {
            foreach (var p in TryPathsFromFormat(data, fmt))
                yield return p;
        }

        foreach (var fmt in GetDataFormatsSafe(data))
        {
            if (string.IsNullOrEmpty(fmt)) continue;
            var lf = fmt.ToLowerInvariant();
            if (lf.IndexOf("uri-list", StringComparison.OrdinalIgnoreCase) < 0 &&
                lf.IndexOf("text/plain", StringComparison.OrdinalIgnoreCase) < 0 &&
                lf.IndexOf("gnome", StringComparison.OrdinalIgnoreCase) < 0 &&
                lf.IndexOf("x-special", StringComparison.OrdinalIgnoreCase) < 0 &&
                !(lf.IndexOf("kde", StringComparison.OrdinalIgnoreCase) >= 0 && lf.IndexOf("uri", StringComparison.OrdinalIgnoreCase) >= 0))
                continue;
            foreach (var p in TryPathsFromFormat(data, fmt))
                yield return p;
        }

        // 兜底：任意格式中的文本若像 uri-list 或绝对路径，再试一次（部分桌面只注册自定义 MIME）
        foreach (var fmt in GetDataFormatsSafe(data))
        {
            if (string.IsNullOrEmpty(fmt)) continue;
            var payload = GetDataPayloadSafe(data, fmt);
            var text = TryGetStringPayload(payload);
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (text.IndexOf("file:", StringComparison.OrdinalIgnoreCase) < 0 &&
                text.IndexOf('\n') < 0 && text.IndexOf('\r') < 0 &&
                !(text.Length > 1 && text[0] == '/' && text.Trim().Length > 1))
                continue;
            foreach (var p in PathsFromUriListText(text))
                yield return p;
        }

        string? t;
        try { t = data.GetText(); }
        catch { t = null; }
        if (!string.IsNullOrEmpty(t))
        {
            foreach (var line in t.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = line.Trim();
                if (string.IsNullOrEmpty(p)) continue;
                if (p.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var uri = new Uri(p);
                        p = Uri.UnescapeDataString(uri.LocalPath);
                    }
                    catch { continue; }
                }
                p = NormalizePath(p);
                if (File.Exists(p) || Directory.Exists(p))
                    yield return p;
            }
        }
    }

    private static string NormalizePath(string p)
    {
        try
        {
            if (File.Exists(p) || Directory.Exists(p))
                return Path.GetFullPath(p);
        }
        catch { }
        return p;
    }

    public static List<string> DistinctPaths(IEnumerable<string> paths)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var raw in paths)
        {
            var resolved = TryResolveToExistingLocalPath(raw);
            if (string.IsNullOrEmpty(resolved)) continue;
            if (set.Add(resolved))
                list.Add(resolved);
        }
        return list;
    }

    /// <summary>
    /// 将拖放/剪贴板中的路径解析为已存在的本地绝对路径。
    /// 部分桌面仍传入带 file:// 的字符串，直接 Path.GetFullPath 会失败或无法匹配文件。
    /// </summary>
    public static string? TryResolveToExistingLocalPath(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();

        try
        {
            if (File.Exists(trimmed) || Directory.Exists(trimmed))
                return Path.GetFullPath(trimmed);
        }
        catch { /* continue */ }

        try
        {
            var full = Path.GetFullPath(trimmed);
            if (File.Exists(full) || Directory.Exists(full))
                return full;
        }
        catch { /* continue */ }

        if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var uri = new Uri(trimmed);
                var local = Uri.UnescapeDataString(uri.LocalPath);
                if (string.IsNullOrEmpty(local)) return null;
                if (File.Exists(local) || Directory.Exists(local))
                    return Path.GetFullPath(local);
            }
            catch { /* ignore */ }
        }

        return null;
    }

    /// <summary>
    /// 从剪贴板文本解析本地路径（与拖放中的 uri-list / 绝对路径 规则一致）。
    /// Wayland 等环境下拖放可能无效时，可在文件管理器中复制文件后使用「粘贴路径」。
    /// </summary>
    public static IEnumerable<string> GetPathsFromClipboardText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var p in PathsFromUriListText(text.Trim()))
            yield return p;
    }
}
