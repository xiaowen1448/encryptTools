using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace EncryptTools.Ui
{
    [SupportedOSPlatform("windows")]
    internal static class Backdrop
    {
        // Win11: DWMWA_SYSTEMBACKDROP_TYPE = 38
        // 2 = Mica, 3 = Acrylic, 4 = Tabbed
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static void TryApplyMicaOrAcrylic(IntPtr hwnd, bool dark)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int darkVal = dark ? 1 : 0;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkVal, sizeof(int));
            }
            catch { }

            // Prefer Mica; if not supported, call will fail silently.
            try
            {
                int mica = 2;
                _ = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref mica, sizeof(int));
            }
            catch { }
        }
    }
}

