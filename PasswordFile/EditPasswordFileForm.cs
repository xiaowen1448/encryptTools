using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace EncryptTools.PasswordFile
{
    /// <summary>
    /// 编辑密码文件：选择 pwd 文件，手动输入或系统派生，保存后状态为已编辑。
    /// </summary>
    internal sealed class EditPasswordFileForm : Form
    {
        private readonly string _pwdDir;
        private readonly ComboBox _cbFile;
        private readonly TextBox _txtPassword;
        private readonly Label _lblStatus;
        private string? _currentPath;

        public EditPasswordFileForm(string pwdDir)
        {
            _pwdDir = pwdDir;
            Text = "编辑密码文件";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(480, 280);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            _cbFile = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "DisplayName", ValueMember = "FullPath" };
            _cbFile.SelectedIndexChanged += (_, __) => LoadCurrentFile();

            _txtPassword = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };

            var btnDerive = new Button { Text = "系统随机派生", AutoSize = true };
            btnDerive.Click += (_, __) =>
            {
                _txtPassword.Text = PasswordFileService.GenerateSystemDerivedPassword();
            };

            _lblStatus = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray };

            root.Controls.Add(new Label { Text = "选择文件：", AutoSize = true }, 0, 0);
            root.Controls.Add(_cbFile, 1, 0);
            root.Controls.Add(new Label { Text = "密码（可修改）：", AutoSize = true }, 0, 1);
            root.Controls.Add(_txtPassword, 1, 1);
            root.Controls.Add(btnDerive, 0, 2);
            root.SetColumnSpan(btnDerive, 2);
            root.Controls.Add(_lblStatus, 0, 3);
            root.SetColumnSpan(_lblStatus, 2);
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));

            var btnSave = new Button { Text = "✓ 保存", DialogResult = DialogResult.None, AutoSize = true };
            var btnCancel = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
            btnBar.Controls.Add(btnCancel);
            btnBar.Controls.Add(btnSave);

            btnSave.Click += (_, __) =>
            {
                if (string.IsNullOrWhiteSpace(_currentPath) || !File.Exists(_currentPath))
                {
                    MessageBox.Show(this, "请先选择要编辑的密码文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                var pwd = _txtPassword.Text ?? "";
                if (!PasswordFileService.ValidateComplexity(pwd))
                {
                    MessageBox.Show(this, "密码复杂度不足：至少12位，包含大小写字母、数字、特殊字符。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                try
                {
                    PasswordFileHelper.SavePasswordToFile(pwd, _currentPath);
                    _lblStatus.Text = "已编辑";
                    _lblStatus.ForeColor = Color.Green;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "保存失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            Controls.Add(root);
            Controls.Add(btnBar);
            AcceptButton = btnSave;
            CancelButton = btnCancel;
            Load += (_, __) => RefreshFileList();
        }

        private sealed class PwdFileEntry
        {
            public string DisplayName { get; set; } = "";
            public string FullPath { get; set; } = "";
        }

        private void RefreshFileList()
        {
            _cbFile.Items.Clear();
            PasswordFileService.EnsurePwdDirectory();
            var files = PasswordFileService.ListPwdFiles();
            foreach (var f in files)
                _cbFile.Items.Add(new PwdFileEntry { DisplayName = Path.GetFileName(f), FullPath = f });
            if (_cbFile.Items.Count > 0)
                _cbFile.SelectedIndex = 0;
        }

        private void LoadCurrentFile()
        {
            _currentPath = (_cbFile.SelectedItem as PwdFileEntry)?.FullPath;
            _txtPassword.Clear();
            _lblStatus.Text = "";
            if (string.IsNullOrEmpty(_currentPath) || !File.Exists(_currentPath)) return;
            try
            {
                _txtPassword.Text = PasswordFileHelper.LoadPasswordFromFile(_currentPath);
            }
            catch
            {
                _txtPassword.Text = "";
            }
        }
    }
}
