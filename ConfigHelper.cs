using System;
using System.Collections.Generic;
using System.IO;

namespace EncryptTools
{
    internal static class ConfigHelper
    {
        private static string ConfigPath
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                return Path.Combine(dir, "config.ini");
            }
        }

        public static EncryptToolsConfig Load()
        {
            var cfg = new EncryptToolsConfig();
            try
            {
                if (!File.Exists(ConfigPath)) return cfg;
                var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var raw in File.ReadAllLines(ConfigPath))
                {
                    var line = raw?.Trim();
                    if (string.IsNullOrEmpty(line)) continue;
                    if (line.StartsWith("#") || line.StartsWith(";")) continue;
                    var idx = line.IndexOf('=');
                    if (idx <= 0) continue;
                    var k = line.Substring(0, idx).Trim();
                    var v = line.Substring(idx + 1).Trim();
                    dict[k] = v;
                }

                cfg.SourcePath = Get(dict, "SourcePath");
                cfg.OutputPath = Get(dict, "OutputPath");
                cfg.PasswordFileName = Get(dict, "PasswordFileName", "password.pwd");
                cfg.PasswordMode = Get(dict, "PasswordMode", "file");
                cfg.LastPasswordFileName = Get(dict, "LastPasswordFileName", "");
            }
            catch { }
            return cfg;
        }

        public static void Save(EncryptToolsConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            try
            {
                File.WriteAllLines(ConfigPath, new[]
                {
                    "SourcePath=" + (cfg.SourcePath ?? ""),
                    "OutputPath=" + (cfg.OutputPath ?? ""),
                    "PasswordMode=" + (cfg.PasswordMode ?? "file"),
                    "PasswordFileName=" + (cfg.PasswordFileName ?? "password.pwd"),
                    "LastPasswordFileName=" + (cfg.LastPasswordFileName ?? ""),
                });
            }
            catch { }
        }

        public static string GetExeDir()
        {
            return AppContext.BaseDirectory;
        }

        private static string Get(Dictionary<string, string> dict, string key, string def = "")
        {
            return dict.TryGetValue(key, out var v) ? v : def;
        }
    }

    internal sealed class EncryptToolsConfig
    {
        public string SourcePath { get; set; } = "";
        public string OutputPath { get; set; } = "";
        public string PasswordMode { get; set; } = "file"; // input|file
        public string PasswordFileName { get; set; } = "password.pwd";
        /// <summary>上次在工作区下拉中选择的密码文件名（不含路径），用于恢复选择。</summary>
        public string LastPasswordFileName { get; set; } = "";
    }
}

