using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using EncryptTools;
using EncryptTools.Desktop.Ui;
using EncryptTools.PasswordFile;

namespace EncryptTools.Desktop.Dialogs;

public sealed class CreatePasswordFileWindow : Window
{
    public bool Success { get; private set; }

    public CreatePasswordFileWindow()
    {
        Title = "创建密码文件";
        Width = 460;
        Height = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var chkRandPwd = new CheckBox { Content = "随机密码", IsChecked = true };
        var chkRandName = new CheckBox { Content = "随机文件名", IsChecked = true };
        var txtPwd1 = new TextBox { PasswordChar = '*' };
        var txtPwd2 = new TextBox { PasswordChar = '*' };
        var txtName = new TextBox();

        void ApplyRandPwd()
        {
            if (chkRandPwd.IsChecked == true)
            {
                var pwd = PasswordFileService.GenerateRandomPassword(32);
                txtPwd1.Text = txtPwd2.Text = pwd;
            }
        }
        void ApplyRandName()
        {
            if (chkRandName.IsChecked == true)
                txtName.Text = PasswordFileService.GenerateRandomFileName();
        }

        chkRandPwd.IsCheckedChanged += (_, _) => { if (chkRandPwd.IsChecked == true) ApplyRandPwd(); };
        chkRandName.IsCheckedChanged += (_, _) => { if (chkRandName.IsChecked == true) ApplyRandName(); };

        var btnSave = new Button { Content = "保存", MinWidth = 88 };
        var btnClose = new Button { Content = "关闭", MinWidth = 88 };
        btnClose.Click += (_, _) => Close();

        btnSave.Click += (_, _) =>
        {
            var p1 = txtPwd1.Text ?? "";
            var p2 = txtPwd2.Text ?? "";
            if (p1 != p2)
            {
                _ = Messages.ShowAsync(this, "错误", "两次输入的密码不一致。");
                return;
            }
            if (!PasswordFileService.ValidateComplexity(p1))
            {
                _ = Messages.ShowAsync(this, "错误", "密码复杂度不足：至少12位，包含大小写字母、数字、特殊字符。");
                return;
            }
            var name = (txtName.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                _ = Messages.ShowAsync(this, "错误", "请输入文件名或勾选随机文件名。");
                return;
            }
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                if (name.IndexOf(c) >= 0)
                {
                    _ = Messages.ShowAsync(this, "错误", "文件名包含非法字符。");
                    return;
                }
            }
            if (!name.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
                name += ".pwd";
            PasswordFileService.EnsurePwdDirectory();
            var target = Path.Combine(PasswordFileService.GetPwdDirectory(), name);
            try
            {
                PasswordFileHelper.SavePasswordToFile(p1, target);
                Success = true;
                Close();
            }
            catch (Exception ex)
            {
                _ = Messages.ShowAsync(this, "错误", ex.Message);
            }
        };

        Opened += (_, _) =>
        {
            ApplyRandPwd();
            ApplyRandName();
        };

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                chkRandPwd, chkRandName,
                new TextBlock { Text = "输入密码：" },
                txtPwd1,
                new TextBlock { Text = "再次输入：" },
                txtPwd2,
                new TextBlock { Text = "文件名：" },
                txtName,
                new TextBlock { Text = "至少12位，含大小写/数字/符号。保存到程序 pwd 目录。", Opacity = 0.6 },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { btnSave, btnClose } }
            }
        };
    }
}
