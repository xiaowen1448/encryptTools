using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Layout;
using EncryptTools;
using EncryptTools.Desktop.Ui;
using EncryptTools.PasswordFile;

namespace EncryptTools.Desktop.Dialogs;

public sealed class EditPasswordWindow : Window
{
    public bool Saved { get; private set; }

    public EditPasswordWindow()
    {
        Title = "编辑密码文件";
        Width = 480;
        Height = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var cb = new ComboBox { MinWidth = 280 };
        var txt = new TextBox { AcceptsReturn = true, MinHeight = 120, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        var btnDerive = new Button { Content = "系统随机派生", HorizontalAlignment = HorizontalAlignment.Left };
        var lbl = new TextBlock { Opacity = 0.6 };

        PasswordFileService.EnsurePwdDirectory();
        foreach (var f in PasswordFileService.ListPwdFiles())
            cb.Items.Add(Path.GetFileName(f));
        if (cb.Items.Count > 0)
            cb.SelectedIndex = 0;

        void LoadSelected()
        {
            if (cb.SelectedItem is not string name) return;
            var path = Path.Combine(PasswordFileService.GetPwdDirectory(), name);
            if (!File.Exists(path)) return;
            try { txt.Text = PasswordFileHelper.LoadPasswordFromFile(path); }
            catch { txt.Text = ""; }
        }

        cb.SelectionChanged += (_, _) => LoadSelected();
        btnDerive.Click += (_, _) => txt.Text = PasswordFileService.GenerateSystemDerivedPassword();

        var btnSave = new Button { Content = "保存", MinWidth = 88 };
        var btnClose = new Button { Content = "关闭", MinWidth = 88 };
        btnClose.Click += (_, _) => Close();

        btnSave.Click += (_, _) =>
        {
            if (cb.SelectedItem is not string name)
            {
                _ = Messages.ShowAsync(this, "提示", "请先选择密码文件。");
                return;
            }
            var path = Path.Combine(PasswordFileService.GetPwdDirectory(), name);
            if (!File.Exists(path)) return;
            var pwd = txt.Text ?? "";
            if (!PasswordFileService.ValidateComplexity(pwd))
            {
                _ = Messages.ShowAsync(this, "错误", "密码复杂度不足。");
                return;
            }
            try
            {
                PasswordFileHelper.SavePasswordToFile(pwd, path);
                Saved = true;
                lbl.Text = "已保存";
            }
            catch (Exception ex)
            {
                _ = Messages.ShowAsync(this, "错误", ex.Message);
            }
        };

        Opened += (_, _) => LoadSelected();

        Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = "选择文件：" },
                cb,
                new TextBlock { Text = "密码（可修改）：" },
                txt,
                btnDerive,
                lbl,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { btnSave, btnClose } }
            }
        };
    }
}
