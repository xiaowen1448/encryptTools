using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace EncryptTools.PasswordFile
{
    /// <summary>
    /// 创建密码文件：系统派生、随机文件名；明文查看、再次随机生成新密码。
    /// </summary>
    internal sealed class CreatePasswordFileForm : Form
    {
        private readonly string _pwdDir;
        private readonly TextBox _txtPwd1;
        private readonly TextBox _txtPwd2;
        private readonly TextBox _txtName;
        private readonly CheckBox _chkSystemDerived;
        private readonly CheckBox _chkRandomFileName;
        private bool _plainVisible;

        public string? CreatedFilePath { get; private set; }

        public CreatePasswordFileForm(string pwdDir)
        {
            _pwdDir = pwdDir;
            Text = "创建密码文件";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(460, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new Padding(12)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 7; i++)
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            _chkSystemDerived = new CheckBox { Text = "随机密码", AutoSize = true, Checked = true };
            _chkRandomFileName = new CheckBox { Text = "随机文件名", AutoSize = true, Checked = true };

            _txtPwd1 = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            _txtPwd2 = new TextBox { UseSystemPasswordChar = true, Dock = DockStyle.Fill };
            _txtName = new TextBox { Dock = DockStyle.Fill };

            void ApplySystemDerived()
            {
                if (_chkSystemDerived.Checked)
                {
                    var pwd = PasswordFileService.GenerateSystemDerivedPassword();
                    _txtPwd1.Text = pwd;
                    _txtPwd2.Text = pwd;
                }
            }
            void ApplyRandomFileName()
            {
                if (_chkRandomFileName.Checked)
                    _txtName.Text = PasswordFileService.GenerateRandomFileName();
            }

            _chkSystemDerived.CheckedChanged += (_, __) => ApplySystemDerived();
            _chkRandomFileName.CheckedChanged += (_, __) => ApplyRandomFileName();

            var btnPlain = new Button { Text = "密码明文查看", AutoSize = true };
            btnPlain.Click += (_, __) =>
            {
                _plainVisible = !_plainVisible;
                _txtPwd1.UseSystemPasswordChar = !_plainVisible;
                _txtPwd2.UseSystemPasswordChar = !_plainVisible;
                btnPlain.Text = _plainVisible ? "隐藏密码" : "密码明文查看";
            };
            var btnRefresh = new Button { Text = "刷新密码和文件名", AutoSize = true };
            btnRefresh.Click += (_, __) =>
            {
                // 刷新：保持与首次打开一致的密码长度，并更新随机文件名
                int len = Math.Max(20, _txtPwd1.Text?.Length ?? 20);
                var pwd = PasswordFileService.GenerateRandomPassword(len);
                _txtPwd1.Text = pwd;
                _txtPwd2.Text = pwd;
                ApplyRandomFileName();
            };

            int r = 0;
            root.Controls.Add(_chkSystemDerived, 0, r);
            root.SetColumnSpan(_chkSystemDerived, 2);
            r++;
            root.Controls.Add(_chkRandomFileName, 0, r);
            root.SetColumnSpan(_chkRandomFileName, 2);
            r++;
            root.Controls.Add(new Label { Text = "输入密码：", AutoSize = true }, 0, r);
            root.Controls.Add(_txtPwd1, 1, r++);
            root.Controls.Add(new Label { Text = "再次输入：", AutoSize = true }, 0, r);
            root.Controls.Add(_txtPwd2, 1, r++);
            root.Controls.Add(new Label { Text = "文件名：", AutoSize = true }, 0, r);
            root.Controls.Add(_txtName, 1, r++);
            var pnlBtns = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight };
            pnlBtns.Controls.Add(btnPlain);
            pnlBtns.Controls.Add(btnRefresh);
            root.Controls.Add(pnlBtns, 0, r);
            root.SetColumnSpan(pnlBtns, 2);
            r++;
            root.Controls.Add(new Label { Text = "至少12位，包含大小写/数字/特殊字符。保存到程序 pwd 目录。", AutoSize = true, ForeColor = Color.DimGray }, 0, r);
            root.SetColumnSpan(root.Controls[root.Controls.Count - 1], 2);

            var btnSave = new Button { Text = "保存", DialogResult = DialogResult.None, AutoSize = true };
            var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
            btnBar.Controls.Add(btnClose);
            btnBar.Controls.Add(btnSave);

            btnSave.Click += (_, __) =>
            {
                var p1 = _txtPwd1.Text ?? "";
                var p2 = _txtPwd2.Text ?? "";
                if (p1 != p2)
                {
                    MessageBox.Show(this, "两次输入的密码不一致。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (!PasswordFileService.ValidateComplexity(p1))
                {
                    MessageBox.Show(this, "密码复杂度不足：至少12位，包含大小写字母、数字、特殊字符。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                string name = (_txtName.Text ?? "").Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    MessageBox.Show(this, "请输入文件名或勾选随机文件名。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                foreach (var c in Path.GetInvalidFileNameChars())
                {
                    if (name.Contains(c))
                    {
                        MessageBox.Show(this, "文件名包含非法字符。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }
                if (!name.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
                    name += ".pwd";

                var target = Path.Combine(_pwdDir, name);
                PasswordFileHelper.SavePasswordToFile(p1, target);
                CreatedFilePath = target;
                DialogResult = DialogResult.OK;
                Close();
            };

            Controls.Add(root);
            Controls.Add(btnBar);
            AcceptButton = btnSave;
            CancelButton = btnClose;
            Load += (_, __) =>
            {
                ApplySystemDerived();
                ApplyRandomFileName();
            };
        }
    }
}
