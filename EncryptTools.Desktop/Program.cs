using System;
using Avalonia;

namespace EncryptTools.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 在已配置 X11（XWayland）时强制走 X11 后端，避免 Wayland 原生下拖放无法进入应用。
        if (OperatingSystem.IsLinux())
        {
            var disp = Environment.GetEnvironmentVariable("DISPLAY");
            if (!string.IsNullOrEmpty(disp))
                Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);
        }

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
