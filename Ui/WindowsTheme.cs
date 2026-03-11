using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace EncryptTools.Ui
{
    internal static class WindowsTheme
    {
        public static bool IsDarkMode()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var v = key?.GetValue("AppsUseLightTheme");
                if (v is int i) return i == 0;
                if (v is byte b) return b == 0;
            }
            catch { }
            return false;
        }
    }
}

