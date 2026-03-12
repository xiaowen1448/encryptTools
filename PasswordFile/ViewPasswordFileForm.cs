using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace EncryptTools.PasswordFile
{
    /// <summary>
    /// 查看密码文件：支持拖拽和浏览打开 .pwd 文件，显示明文密码并可复制。
    /// </summary>
    internal sealed class ViewPasswordFileForm : Form
    {
        private readonly TextBox _txtPath;
        private readonly TextBox _txtPassword;
        private readonly Button _btnOpen;

        public ViewPasswordFileForm()
        {
            Text = "查看密码文件";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(500, 220);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            AllowDrop = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _txtPath = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            _txtPath.AllowDrop = true;
            _txtPath.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };
            _txtPath.DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                    LoadPwdFile(paths[0]);
            };

            _txtPassword = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, UseSystemPasswordChar = false };
            var chkShow = new CheckBox { Text = "显示明文", AutoSize = true, Checked = true };

            _btnOpen = new Button { Text = "浏览...", AutoSize = true };
            _btnOpen.Click += (_, __) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "选择 .pwd 密码文件",
                    Filter = "密码文件 (*.pwd)|*.pwd|所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    LoadPwdFile(dlg.FileName);
            };

            root.Controls.Add(new Label { Text = "文件路径：", AutoSize = true }, 0, 0);
            root.Controls.Add(_txtPath, 1, 0);
            root.Controls.Add(new Label { Text = "", AutoSize = true }, 0, 1);
            var pnlOpen = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight };
            pnlOpen.Controls.Add(_btnOpen);
            root.Controls.Add(pnlOpen, 1, 1);
            root.Controls.Add(new Label { Text = "明文密码：", AutoSize = true }, 0, 2);
            root.Controls.Add(_txtPassword, 1, 2);
            root.Controls.Add(chkShow, 0, 3);
            root.SetColumnSpan(chkShow, 2);

            chkShow.CheckedChanged += (_, __) => _txtPassword.UseSystemPasswordChar = !chkShow.Checked;

            var btnCopy = new Button { Text = "复制密码", AutoSize = true };
            btnCopy.Click += (_, __) =>
            {
                if (!string.IsNullOrEmpty(_txtPassword.Text))
                {
                    Clipboard.SetText(_txtPassword.Text);
                    MessageBox.Show(this, "已复制到剪贴板。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.OK, AutoSize = true };
            var btnBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
            btnBar.Controls.Add(btnClose);
            btnBar.Controls.Add(btnCopy);

            Controls.Add(root);
            Controls.Add(btnBar);
            CancelButton = btnClose;
        }

        private void LoadPwdFile(string path)
        {
            _txtPath.Text = path;
            _txtPassword.Clear();
            if (!File.Exists(path) || !path.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
                return;
            try
            {
                var pwd = PasswordFileHelper.LoadPasswordFromFile(path);
                _txtPassword.Text = pwd;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法读取密码文件: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
