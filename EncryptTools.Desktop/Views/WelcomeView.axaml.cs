using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace EncryptTools.Desktop.Views;

public partial class WelcomeView : UserControl
{
    public event Action<string>? WorkspaceKindRequested;

    public WelcomeView()
    {
        InitializeComponent();
        
        // 相对路径加载 encryptTools 根目录的 app2.png
        try
        {
            var logoImage = this.FindControl<Image>("LogoImage");
            if (logoImage != null)
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var path = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "app2.png"));
                if (File.Exists(path))
                {
                    logoImage.Source = new Bitmap(path);
                }
                else
                {
                    // 备用回退：保持原来的资源方式，防止路径错误时不显示
                    logoImage.Source = new Bitmap(AssetLoader.Open(new Uri("avares://encryptTools/Assets/app2.png")));
                }
            }
        }
        catch { /* ignore if not found */ }
        
        BtnFile.Click += (_, _) => WorkspaceKindRequested?.Invoke("文件");
        BtnString.Click += (_, _) => WorkspaceKindRequested?.Invoke("字符串");
        BtnImage.Click += (_, _) => WorkspaceKindRequested?.Invoke("图片");
    }
}
