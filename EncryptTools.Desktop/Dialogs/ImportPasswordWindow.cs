using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using EncryptTools.Desktop.Ui;
using EncryptTools.PasswordFile;

namespace EncryptTools.Desktop.Dialogs;

public sealed class ImportPasswordWindow : Window
{
    public bool Imported { get; private set; }

    public ImportPasswordWindow()
    {
        Title = "导入密码文件";
        Width = 480;
        Height = 200;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var lbl = new TextBlock { Text = "选择 .pwd 文件复制到程序 pwd 目录。", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var btnBrowse = new Button { Content = "浏览…", HorizontalAlignment = HorizontalAlignment.Left };
        var btnClose = new Button { Content = "关闭", MinWidth = 88 };

        btnBrowse.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top == null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 .pwd 文件",
                AllowMultiple = false
            });
            if (files.Count == 0) return;
            var path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
            {
                await Messages.ShowAsync(this, "错误", "请选择 .pwd 文件。");
                return;
            }
            PasswordFileService.EnsurePwdDirectory();
            var pwdDir = Path.GetFullPath(PasswordFileService.GetPwdDirectory()).TrimEnd(Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(path);
            if (fullPath.StartsWith(pwdDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                fullPath.Equals(pwdDir, StringComparison.OrdinalIgnoreCase))
            {
                await Messages.ShowAsync(this, "提示", "该文件已在程序 pwd 目录中。");
                Imported = true;
                Close();
                return;
            }
            try
            {
                var dest = Path.Combine(PasswordFileService.GetPwdDirectory(), Path.GetFileName(path));
                File.Copy(path, dest, true);
                Imported = true;
                await Messages.ShowAsync(this, "完成", "已导入到程序 pwd 目录。");
                Close();
            }
            catch (Exception ex)
            {
                await Messages.ShowAsync(this, "错误", "导入失败: " + ex.Message);
            }
        };

        btnClose.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children = { lbl, btnBrowse, btnClose }
        };
    }
}
