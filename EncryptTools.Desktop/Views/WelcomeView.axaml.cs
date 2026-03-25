using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace EncryptTools.Desktop.Views;

public partial class WelcomeView : UserControl
{
    public event Action<string>? WorkspaceKindRequested;

    public WelcomeView()
    {
        InitializeComponent();
        BtnFile.Click += (_, _) => WorkspaceKindRequested?.Invoke("文件");
        BtnString.Click += (_, _) => WorkspaceKindRequested?.Invoke("字符串");
        BtnImage.Click += (_, _) => WorkspaceKindRequested?.Invoke("图片");
    }
}
