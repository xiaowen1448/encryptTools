using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace EncryptTools
{
    [SupportedOSPlatform("windows6.1")]
    internal sealed class SettingsForm : Form
    {
        private readonly TextBox _txtSource;
        private readonly TextBox _txtOutput;
        private readonly ComboBox _cmbPwdMode;
        private readonly TextBox _txtPwd;
        private readonly Button _btnImportPwd;
        private readonly Label _lblHint;

        private string _importPwdFile = "";

        public EncryptToolsConfig ResultConfig { get; private set; } = new EncryptToolsConfig();

        public SettingsForm(EncryptToolsConfig current)
        {
            Text = "设置";
            StartPosition = FormStartPosition.CenterParent;
            Width = 720;
            Height = 320;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.FixedDialog;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 6,
                Padding = new Padding(10),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _txtSource = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _txtOutput = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            _cmbPwdMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            _cmbPwdMode.Items.AddRange(new object[] { "输入密码", "密码文件" });
            _cmbPwdMode.SelectedIndex = 1;
            _txtPwd = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };
            _btnImportPwd = new Button { Text = "导入...", AutoSize = true };
            _lblHint = new Label { AutoSize = true, ForeColor = System.Drawing.Color.DimGray, Dock = DockStyle.Fill };

            var btnBrowseSource = new Button { Text = "浏览...", AutoSize = true };
            var btnBrowseOutput = new Button { Text = "选择...", AutoSize = true };

            layout.Controls.Add(new Label { Text = "源路径:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            layout.Controls.Add(_txtSource, 1, 0);
            layout.Controls.Add(btnBrowseSource, 2, 0);

            layout.Controls.Add(new Label { Text = "输出路径(可选):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
            layout.Controls.Add(_txtOutput, 1, 1);
            layout.Controls.Add(btnBrowseOutput, 2, 1);

            layout.Controls.Add(new Label { Text = "密码方式:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
            layout.Controls.Add(_cmbPwdMode, 1, 2);

            layout.Controls.Add(new Label { Text = "密码/密码文件:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            layout.Controls.Add(_txtPwd, 1, 3);
            layout.Controls.Add(_btnImportPwd, 2, 3);

            layout.Controls.Add(_lblHint, 0, 4);
            layout.SetColumnSpan(_lblHint, 3);

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false };
            var btnOk = new Button { Text = "保存", AutoSize = true };
            var btnCancel = new Button { Text = "取消", AutoSize = true };
            bottom.Controls.Add(btnOk);
            bottom.Controls.Add(btnCancel);
            layout.Controls.Add(bottom, 0, 5);
            layout.SetColumnSpan(bottom, 3);

            Controls.Add(layout);

            btnCancel.Click += (_, __) => { DialogResult = DialogResult.Cancel; Close(); };
            btnOk.Click += (_, __) => OnSave();
            btnBrowseSource.Click += (_, __) => BrowseSource();
            btnBrowseOutput.Click += (_, __) => BrowseOutput();
            _btnImportPwd.Click += (_, __) => ImportPasswordFile();
            _cmbPwdMode.SelectedIndexChanged += (_, __) => RefreshPwdUi();

            // load current config
            _txtSource.Text = current?.SourcePath ?? "";
            _txtOutput.Text = current?.OutputPath ?? "";
            _cmbPwdMode.SelectedIndex = string.Equals(current?.PasswordMode, "input", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
            _txtPwd.Text = "";
            RefreshPwdUi();
            UpdateHint();
        }

        private void UpdateHint()
        {
            var exeDir = ConfigHelper.GetExeDir();
            _lblHint.Text =
                $"保存后将写入: {Path.Combine(exeDir, "config.ini")} ；密码文件将保存为: {Path.Combine(exeDir, "password.pwd")}\r\n" +
                "提示：若源文件被占用，加密会尝试结束占用进程并重试；仍失败会使用临时副本继续加密。";
        }

        private void RefreshPwdUi()
        {
            bool input = _cmbPwdMode.SelectedIndex == 0;
            _txtPwd.UseSystemPasswordChar = input;
            _txtPwd.ReadOnly = !input;
            _btnImportPwd.Enabled = !input;
            if (input)
            {
                _txtPwd.BackColor = System.Drawing.SystemColors.Window;
            }
            else
            {
                _txtPwd.BackColor = System.Drawing.SystemColors.Control;
                if (string.IsNullOrEmpty(_importPwdFile))
                    _txtPwd.Text = "";
            }
        }

        private void BrowseSource()
        {
            using var fbd = new FolderBrowserDialog { Description = "选择源文件夹（或选到包含文件的目录）" };
            if (fbd.ShowDialog(this) == DialogResult.OK)
                _txtSource.Text = fbd.SelectedPath;
        }

        private void BrowseOutput()
        {
            using var fbd = new FolderBrowserDialog { Description = "选择输出文件夹（不勾选源加密时使用）" };
            if (fbd.ShowDialog(this) == DialogResult.OK)
                _txtOutput.Text = fbd.SelectedPath;
        }

        private void ImportPasswordFile()
        {
            using var openDialog = new OpenFileDialog
            {
                Title = "选择密码文件",
                Filter = "密码文件 (*.pwd)|*.pwd|所有文件 (*.*)|*.*",
                CheckFileExists = true
            };
            if (openDialog.ShowDialog(this) == DialogResult.OK)
            {
                _importPwdFile = openDialog.FileName;
                _txtPwd.Text = Path.GetFileName(_importPwdFile);
            }
        }

        private void OnSave()
        {
            var source = _txtSource.Text.Trim();
            var output = _txtOutput.Text.Trim();
            bool inputPwd = _cmbPwdMode.SelectedIndex == 0;

            if (string.IsNullOrWhiteSpace(source))
            {
                MessageBox.Show(this, "源路径不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                MessageBox.Show(this, "源路径不存在", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var exeDir = ConfigHelper.GetExeDir();
            var pwdTarget = Path.Combine(exeDir, "password.pwd");

            try
            {
                if (inputPwd)
                {
                    var pwd = _txtPwd.Text;
                    if (string.IsNullOrWhiteSpace(pwd))
                    {
                        MessageBox.Show(this, "密码不能为空", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    PasswordFileHelper.SavePasswordToFile(pwd, pwdTarget);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(_importPwdFile) || !File.Exists(_importPwdFile))
                    {
                        MessageBox.Show(this, "请先导入密码文件", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    File.Copy(_importPwdFile, pwdTarget, overwrite: true);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存密码文件失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ResultConfig = new EncryptToolsConfig
            {
                SourcePath = source,
                OutputPath = output,
                PasswordMode = "file",
                PasswordFileName = "password.pwd",
            };
            try
            {
                ConfigHelper.Save(ResultConfig);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存 config.ini 失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

