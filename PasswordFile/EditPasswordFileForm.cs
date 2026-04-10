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
        private readonly TextBox _txtFileName;
        private readonly TextBox _txtPassword;
        private readonly Label _lblStatus;
        private string? _currentPath;

        public EditPasswordFileForm(string pwdDir)
        {
            _pwdDir = pwdDir;
            Text = "编辑密码文件";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(480, 320);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 6,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            _cbFile = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "DisplayName", ValueMember = "FullPath" };
            _cbFile.SelectedIndexChanged += (_, __) => LoadCurrentFile();

            _txtFileName = new TextBox { Dock = DockStyle.Fill };

            _txtPassword = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical };

            var btnDerive = new Button { Text = "系统随机派生", AutoSize = true };
            btnDerive.Click += (_, __) =>
            {
                _txtPassword.Text = PasswordFileService.GenerateSystemDerivedPassword();
            };

            _lblStatus = new Label { Text = "", AutoSize = true, ForeColor = Color.Gray };

            root.Controls.Add(new Label { Text = "选择文件：", AutoSize = true }, 0, 0);
            root.Controls.Add(_cbFile, 1, 0);
            root.Controls.Add(new Label { Text = "文件名称：", AutoSize = true }, 0, 1);
            root.Controls.Add(_txtFileName, 1, 1);
            root.Controls.Add(new Label { Text = "密码（可修改）：", AutoSize = true }, 0, 2);
            root.Controls.Add(_txtPassword, 1, 2);
            root.Controls.Add(btnDerive, 0, 3);
            root.SetColumnSpan(btnDerive, 2);
            root.Controls.Add(_lblStatus, 0, 4);
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
                // 移除密码复杂度校验
                if (string.IsNullOrWhiteSpace(pwd))
                {
                    MessageBox.Show(this, "密码不能为空。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                var newFileName = _txtFileName.Text?.Trim() ?? "";
                if (string.IsNullOrWhiteSpace(newFileName))
                {
                    MessageBox.Show(this, "文件名称不能为空。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                
                try
                {
                    // 确保文件名以 .pwd 结尾
                    if (!newFileName.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
                    {
                        newFileName += ".pwd";
                    }
                    
                    var newPath = Path.Combine(_pwdDir, newFileName);
                    
                    // 如果文件名改变了，先删除旧文件
                    if (!string.Equals(_currentPath, newPath, StringComparison.OrdinalIgnoreCase))
                    {
                        // 检查新文件名是否已存在
                        if (File.Exists(newPath))
                        {
                            MessageBox.Show(this, "该文件名已存在，请选择其他名称。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                        
                        // 保存到新文件，然后删除旧文件
                        PasswordFileHelper.SavePasswordToFile(pwd, newPath);
                        File.Delete(_currentPath);
                        _currentPath = newPath;
                    }
                    else
                    {
                        // 只更新密码内容
                        PasswordFileHelper.SavePasswordToFile(pwd, _currentPath);
                    }
                    
                    _lblStatus.Text = "已保存";
                    _lblStatus.ForeColor = Color.Green;
                    
                    // 刷新文件列表
                    RefreshFileList();
                    
                    // 重新选中当前文件
                    for (int i = 0; i < _cbFile.Items.Count; i++)
                    {
                        if (_cbFile.Items[i] is PwdFileEntry entry && 
                            string.Equals(entry.FullPath, _currentPath, StringComparison.OrdinalIgnoreCase))
                        {
                            _cbFile.SelectedIndex = i;
                            break;
                        }
                    }
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
            _txtFileName.Clear();
            _lblStatus.Text = "";
            if (string.IsNullOrEmpty(_currentPath) || !File.Exists(_currentPath)) return;
            try
            {
                _txtPassword.Text = PasswordFileHelper.LoadPasswordFromFile(_currentPath);
                _txtFileName.Text = Path.GetFileName(_currentPath);
            }
            catch
            {
                _txtPassword.Text = "";
                _txtFileName.Text = "";
            }
        }
    }
}
