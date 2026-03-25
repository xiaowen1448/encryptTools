using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EncryptTools.Desktop.Workspace;

/// <summary>与 Windows WorkspaceForm 一致的路径去重与公共父目录计算（简化移植）。</summary>
internal static class WorkspacePathHelpers
{
    public static List<string> RemoveNestedPaths(List<string> paths)
    {
        if (paths == null || paths.Count <= 1) return paths ?? new List<string>();
        var normalized = new List<(string original, string full)>();
        char sep = Path.DirectorySeparatorChar;
        foreach (var p in paths)
        {
            try
            {
                string full = Path.GetFullPath(p);
                if (Directory.Exists(full))
                    full = full.TrimEnd(sep, Path.AltDirectorySeparatorChar) + sep;
                normalized.Add((p, full));
            }
            catch
            {
                normalized.Add((p, p));
            }
        }
        var result = new List<string>();
        for (int i = 0; i < normalized.Count; i++)
        {
            var (orig, full) = normalized[i];
            bool underOther = false;
            for (int j = 0; j < normalized.Count; j++)
            {
                if (i == j) continue;
                string otherFull = normalized[j].full;
                if (!Directory.Exists(normalized[j].original))
                    continue;
                if (otherFull.Length > 0 && otherFull[^1] != sep)
                    otherFull += sep;
                if (full.StartsWith(otherFull, StringComparison.OrdinalIgnoreCase) && full.Length > otherFull.Length)
                {
                    underOther = true;
                    break;
                }
            }
            if (!underOther)
                result.Add(orig);
        }
        return result.Count > 0 ? result : new List<string> { paths[0] };
    }

    public static string GetCommonParentOnly(List<string> paths)
    {
        if (paths == null || paths.Count == 0)
            return "";
        var dirs = new List<string>();
        foreach (var p in paths)
        {
            try
            {
                var full = Path.GetFullPath(p);
                if (Directory.Exists(full))
                    dirs.Add(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                else
                    dirs.Add(Path.GetDirectoryName(full) ?? full);
            }
            catch { }
        }
        if (dirs.Count == 0) return "";
        string common = dirs[0];
        for (int i = 1; i < dirs.Count; i++)
        {
            var other = dirs[i];
            while (common.Length > 0 && !other.StartsWith(common, StringComparison.OrdinalIgnoreCase))
            {
                var lastSep = common.LastIndexOf(Path.DirectorySeparatorChar);
                if (lastSep <= 0) { common = ""; break; }
                common = common.Substring(0, lastSep);
            }
            if (common.Length > 0 && other.Length > common.Length && other[common.Length] != Path.DirectorySeparatorChar)
            {
                var lastSep = common.LastIndexOf(Path.DirectorySeparatorChar);
                if (lastSep > 0) common = common.Substring(0, lastSep);
            }
        }
        if (string.IsNullOrEmpty(common)) common = Path.GetPathRoot(dirs[0]) ?? Environment.CurrentDirectory;
        return common;
    }
}
