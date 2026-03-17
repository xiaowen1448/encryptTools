using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace EncryptTools
{
    internal sealed class DecryptPayloadForm : Form
    {
        private readonly string _exePath;
        private readonly ComboBox _cbMode;
        private readonly TextBox _txtValue;
        private readonly Button _btnBrowsePwd;
        private readonly Button _btnDecrypt;
        private readonly Label _lblStatus;

        public DecryptPayloadForm(string exePath)
        {
            _exePath = exePath;
            Text = "解密文件";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(560, 220);
            MinimumSize = new Size(520, 200);
            Font = new Font("Microsoft YaHei UI", 9F);
            try { Icon = LoadAppIcon() ?? Icon; } catch { }

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(14) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40)); // mode + value
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var title = new Label
            {
                Text = "选择密码或密码文件以解密并释放原始文件",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            };

            var rowMode = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
            rowMode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            rowMode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            rowMode.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            rowMode.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            rowMode.Controls.Add(new Label { Text = "方式:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
            _cbMode = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cbMode.Items.AddRange(new object[] { "密码", "密码文件" });
            _cbMode.SelectedIndex = 0;
            rowMode.Controls.Add(_cbMode, 1, 0);
            _txtValue = new TextBox { Dock = DockStyle.Left, Width = 100
#if !NET46 && !NET48 && !NET461
                , PlaceholderText = "输入密码"
#endif
            };
            rowMode.Controls.Add(_txtValue, 2, 0);
            _btnBrowsePwd = new Button { Text = "选择密码文件", Dock = DockStyle.Fill, Visible = false };
            rowMode.Controls.Add(_btnBrowsePwd, 3, 0);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            _btnDecrypt = new Button { Text = "解密并释放", AutoSize = true, BackColor = Color.SeaGreen, ForeColor = Color.White, Padding = new Padding(10, 6, 10, 6) };
            actions.Controls.Add(_btnDecrypt);

            _lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray, Text = "注意：密码文件错误或密码错误一次将删除此程序。" };

            root.Controls.Add(title, 0, 0);
            root.Controls.Add(rowMode, 0, 1);
            root.Controls.Add(actions, 0, 2);
            root.Controls.Add(_lblStatus, 0, 3);
            Controls.Add(root);

            _btnBrowsePwd.Click += (_, __) => BrowsePwd();
            _btnDecrypt.Click += async (_, __) => await DecryptAsync();
            _txtValue.TextChanged += (_, __) => UpdateWarningVisual();
            _cbMode.SelectedIndexChanged += (_, __) => ApplyModeUi();
            ApplyModeUi();
        }

        private void ApplyModeUi()
        {
            bool isPwdFile = _cbMode.SelectedIndex == 1;
            _btnBrowsePwd.Visible = isPwdFile;
            _txtValue.ReadOnly = isPwdFile;
            _txtValue.UseSystemPasswordChar = !isPwdFile;
#if !NET46 && !NET48 && !NET461
            _txtValue.PlaceholderText = isPwdFile ? "请选择密码文件(.pwd)" : "输入密码";
#endif
            if (isPwdFile)
            {
                // 切换到密码文件模式时，清空手动密码
                if (!string.IsNullOrWhiteSpace(_txtValue.Text) && File.Exists(_txtValue.Text) == false)
                    _txtValue.Clear();
            }
            UpdateWarningVisual();
        }

        private void UpdateWarningVisual()
        {
            bool isPwdFile = _cbMode.SelectedIndex == 1;
            bool hasValue = !string.IsNullOrWhiteSpace(_txtValue.Text);
            bool hasPwdFile = isPwdFile && hasValue && File.Exists(_txtValue.Text);
            bool hasPassword = !isPwdFile && hasValue;
            if (!hasPassword && !hasPwdFile)
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "请输出密码和导入密码文件。";
            }
            else
            {
                _lblStatus.ForeColor = Color.DimGray;
                _lblStatus.Text = "注意：密码文件错误或密码错误一次将删除此程序。";
            }
        }

        private void BrowsePwd()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "选择密码文件",
                Filter = "密码文件|*.pwd|所有文件|*.*",
                Multiselect = false
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _txtValue.Text = dlg.FileName;
                UpdateWarningVisual();
            }
        }

        private async System.Threading.Tasks.Task DecryptAsync()
        {
            try
            {
                if (!ExePayload.TryReadPayload(_exePath, out var meta, out var encryptedBytes, out var payloadErr) || encryptedBytes == null)
                {
                    FailAndSelfDelete("载荷读取失败。" + (string.IsNullOrEmpty(payloadErr) ? "" : " " + payloadErr));
                    return;
                }

                string password;
                bool isPwdFile = _cbMode.SelectedIndex == 1;
                if (!isPwdFile && !string.IsNullOrWhiteSpace(_txtValue.Text))
                {
                    password = _txtValue.Text;
                }
                else if (isPwdFile && !string.IsNullOrWhiteSpace(_txtValue.Text) && File.Exists(_txtValue.Text))
                {
                    try { password = PasswordFileHelper.LoadPasswordFromFile(_txtValue.Text); }
                    catch { FailAndSelfDelete("密码文件导入失败。"); return; }
                }
                else
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "请输出密码和导入密码文件。";
                    return;
                }
                if (string.IsNullOrWhiteSpace(password))
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "请输出密码和导入密码文件。";
                    return;
                }

                var confirm = MessageBox.Show(
                    this,
                    "警告：密码输入错误一次，将删除该程序源文件，不可恢复。\n\n你确定要解密吗？",
                    "确认解密",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;

                _btnDecrypt.Enabled = false;
                _btnBrowsePwd.Enabled = false;
                _cbMode.Enabled = false;
                _txtValue.Enabled = false;
                _lblStatus.Text = "正在解密，请稍候…";

                var tempEnc = Path.Combine(Path.GetTempPath(), "encryptTools_payload_" + Guid.NewGuid().ToString("N") + ".enc");
                await Compat.FileWriteAllBytesAsync(tempEnc, encryptedBytes);

                var outDir = Path.GetDirectoryName(_exePath) ?? Environment.CurrentDirectory;
                var outTemp = Path.Combine(outDir, "decrypt_" + Guid.NewGuid().ToString("N") + ".tmp");

                var crypto = new CryptoService();
                CryptoService.DecryptResult result;
                try
                {
                    result = await crypto.DecryptFileAsync(tempEnc, outTemp, password, progress: null, ct: System.Threading.CancellationToken.None);
                }
                catch (NotSupportedException ex) when (ex.Message != null && (ex.Message.IndexOf("AES-GCM", StringComparison.Ordinal) >= 0 || ex.Message.IndexOf("需要", StringComparison.Ordinal) >= 0))
                {
                    try { File.Delete(outTemp); } catch { }
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "该载荷为 AES-GCM 加密，当前运行环境不支持解密。";
                    _btnDecrypt.Enabled = true;
                    _btnBrowsePwd.Enabled = true;
                    _cbMode.Enabled = true;
                    _txtValue.Enabled = true;
                    return;
                }
                catch (CryptographicException)
                {
                    try { File.Delete(outTemp); } catch { }
                    FailAndSelfDelete("密码或密钥错误。");
                    return;
                }
                catch (Exception)
                {
                    try { File.Delete(outTemp); } catch { }
                    FailAndSelfDelete("解密失败。");
                    return;
                }
                finally
                {
                    try { File.Delete(tempEnc); } catch { }
                }

                if (!File.Exists(outTemp))
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "解密未生成有效文件。";
                    _btnDecrypt.Enabled = true;
                    _btnBrowsePwd.Enabled = true;
                    _cbMode.Enabled = true;
                    _txtValue.Enabled = true;
                    return;
                }
                long outTempLen = new FileInfo(outTemp).Length;
                if (outTempLen == 0)
                {
                    try { File.Delete(outTemp); } catch { }
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "解密结果为空文件。";
                    _btnDecrypt.Enabled = true;
                    _btnBrowsePwd.Enabled = true;
                    _cbMode.Enabled = true;
                    _txtValue.Enabled = true;
                    return;
                }

                var desiredName = SanitizeFileName(result.OriginalFileName) ?? "output.bin";
                if (string.IsNullOrWhiteSpace(desiredName)) desiredName = "output.bin";
                var outPath = GetNonConflictingPath(Path.Combine(outDir, desiredName));
                try
                {
                    Compat.FileMoveOverwrite(outTemp, outPath);
                }
                catch (Exception ex)
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "释放文件失败：" + (ex.Message ?? "未知错误") + "。临时文件：" + outTemp;
                    _btnDecrypt.Enabled = true;
                    _btnBrowsePwd.Enabled = true;
                    _cbMode.Enabled = true;
                    _txtValue.Enabled = true;
                    return;
                }

                if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                {
                    _lblStatus.ForeColor = Color.Firebrick;
                    _lblStatus.Text = "释放后文件不存在或为空。";
                    _btnDecrypt.Enabled = true;
                    _btnBrowsePwd.Enabled = true;
                    _cbMode.Enabled = true;
                    _txtValue.Enabled = true;
                    return;
                }

                _lblStatus.ForeColor = Color.DarkGreen;
                _lblStatus.Text = "解密完成：" + outPath;
                try
                {
                    Process.Start(new ProcessStartInfo { FileName = outDir, UseShellExecute = true });
                }
                catch { }

                // 解密成功且文件已释放后再删除 EXE 源文件
                try { ScheduleSelfDeleteAndExit(_exePath); } catch { }
            }
            catch (Exception ex)
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = "解密失败：" + (ex.Message ?? "未知错误");
                _btnDecrypt.Enabled = true;
                _btnBrowsePwd.Enabled = true;
                _cbMode.Enabled = true;
                _txtValue.Enabled = true;
            }
        }

        private static Icon? LoadAppIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var name = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(name)) return null;
                using var s = asm.GetManifestResourceStream(name);
                return s != null ? new Icon(s) : null;
            }
            catch { return null; }
        }

        private static string? SanitizeFileName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static string GetNonConflictingPath(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? "";
            var baseName = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 1; i < 1000; i++)
            {
                var p = Path.Combine(dir, $"{baseName}({i}){ext}");
                if (!File.Exists(p)) return p;
            }
            return Path.Combine(dir, $"{baseName}({Guid.NewGuid():N}){ext}");
        }

        private void FailAndSelfDelete(string reason)
        {
            try { _lblStatus.Text = reason + " 程序将被删除。"; } catch { }
            try { ScheduleSelfDeleteAndExit(_exePath); } catch { Environment.Exit(1); }
        }

        private static void ScheduleSelfDeleteAndExit(string exePath)
        {
            // 延迟删除自身：cmd /c ping 127.0.0.1 -n 2 >nul & del /f /q "xxx.exe"
            var cmd = $"ping 127.0.0.1 -n 2 >nul & del /f /q \"{exePath}\"";
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + cmd,
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            Environment.Exit(1);
        }
    }
}

