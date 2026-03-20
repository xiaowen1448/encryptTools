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

            _lblStatus = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.DimGray, Text = "提示：v2 加密包需用「密码文件」选择加密时的 .pwd；解密失败可修改后重试。" };

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
                _lblStatus.Text = "提示：绑定 .pwd 的加密包请使用「密码文件」方式解密。";
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
                    ResetDecryptUiAfterFailure("载荷读取失败。" + (string.IsNullOrEmpty(payloadErr) ? "" : " " + payloadErr));
                    return;
                }

                string password;
                byte[]? passwordFileHash = null;
                bool isPwdFile = _cbMode.SelectedIndex == 1;
                if (!isPwdFile && !string.IsNullOrWhiteSpace(_txtValue.Text))
                {
                    password = _txtValue.Text;
                }
                else if (isPwdFile && !string.IsNullOrWhiteSpace(_txtValue.Text) && File.Exists(_txtValue.Text))
                {
                    var pwdPath = _txtValue.Text;
                    try { password = PasswordFileHelper.LoadPasswordFromFile(pwdPath); }
                    catch (Exception exPwd)
                    {
                        ResetDecryptUiAfterFailure("密码文件读取失败：" + (exPwd.Message ?? "请确认文件未损坏；GCM 格式 .pwd 需本机安装 .NET 8。"));
                        return;
                    }
                    // 与主程序加密一致：v2 头中绑定的是「整个 .pwd 文件字节」的 SHA256，解密时必须传入
                    try
                    {
                        var raw = File.ReadAllBytes(pwdPath);
                        passwordFileHash = Compat.Sha256Hash(raw);
                    }
                    catch { passwordFileHash = null; }
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
                    "即将解密并尝试将文件释放到本程序所在文件夹。\n\n若密码或 .pwd 不正确将解密失败，可修改后再次尝试。",
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
                // 解密中间文件写到用户临时目录，避免 EXE 位于只读/受保护目录时创建失败而被误判为解密失败
                var outTemp = Path.Combine(Path.GetTempPath(), "encryptTools_decrypt_" + Guid.NewGuid().ToString("N") + ".tmp");

                var crypto = new CryptoService();
                CryptoService.DecryptResult result = new CryptoService.DecryptResult();
                try
                {
                    result = await crypto.DecryptFileAsync(tempEnc, outTemp, password, progress: null, ct: System.Threading.CancellationToken.None, passwordFileHash: passwordFileHash);
                }
                catch (NotSupportedException ex)
                {
                    try { File.Delete(outTemp); } catch { }
                    var hint = ex.Message ?? "";
                    // NET461/.NET4.x 进程可能不支持 AesGcm：若载荷是 AES-GCM，则尝试走 GcmCli 外部进程解密。
                    try
                    {
                        var (alg, originalFileName) = CryptoService.PeekEncryptedFileInfo(tempEnc);
                        if (alg == CryptoAlgorithm.AesGcm)
                        {
                            bool ok = await GcmRunner.DecryptAsync(tempEnc, outTemp, password, progress: null, log: null, ct: System.Threading.CancellationToken.None);
                            if (ok)
                            {
                                result = new CryptoService.DecryptResult { OriginalFileName = originalFileName };
                                _lblStatus.ForeColor = Color.DarkGreen;
                                _lblStatus.Text = "解密完成（AES-GCM，GcmCli 模式）。正在释放…";
                            }
                            else
                            {
                                ResetDecryptUiAfterFailure("AES-GCM 载荷需要 GcmCli 解密，但解密失败。请确认目标机可运行 dotnet/.NET 运行时。");
                                return;
                            }
                        }
                        else
                        {
                            ResetDecryptUiAfterFailure("当前环境不支持此加密格式：" + hint);
                            return;
                        }
                    }
                    catch
                    {
                        if (hint.IndexOf("AES-GCM", StringComparison.Ordinal) >= 0 || hint.IndexOf(".NET 8", StringComparison.Ordinal) >= 0 || hint.IndexOf("需要", StringComparison.Ordinal) >= 0)
                            ResetDecryptUiAfterFailure("该载荷为 AES-GCM 加密，当前运行环境无法解密。\n" + hint);
                        else
                            ResetDecryptUiAfterFailure("当前环境不支持此加密格式：" + hint);
                        return;
                    }
                }
                catch (CryptographicException ex)
                {
                    try { File.Delete(outTemp); } catch { }
                    var msg = ex.Message ?? "";
                    // v2 载荷绑定了 .pwd 文件指纹：仅输入口令无法通过校验
                    if (msg.IndexOf("缺少密码文件", StringComparison.Ordinal) >= 0 ||
                        msg.IndexOf("密码文件指纹", StringComparison.Ordinal) >= 0)
                    {
                        ResetDecryptUiAfterFailure("该加密包已绑定密码文件：请切换为「密码文件」方式，并选择加密时使用的 .pwd 文件。");
                        return;
                    }
                    if (msg.IndexOf("密码文件不匹配", StringComparison.Ordinal) >= 0)
                    {
                        ResetDecryptUiAfterFailure("所选 .pwd 与加密时不一致（文件内容指纹不同）。请换用加密时保存的那份密码文件。");
                        return;
                    }
                    ResetDecryptUiAfterFailure("密码错误或密钥无法验证，请核对口令与 .pwd 后重试。");
                    return;
                }
                catch (Exception ex)
                {
                    try { File.Delete(outTemp); } catch { }
                    ResetDecryptUiAfterFailure("解密失败：" + (ex.Message ?? ex.GetType().Name));
                    return;
                }
                finally
                {
                    try { File.Delete(tempEnc); } catch { }
                }

                if (!File.Exists(outTemp))
                {
                    ResetDecryptUiAfterFailure("解密未生成有效文件。");
                    return;
                }
                long outTempLen = new FileInfo(outTemp).Length;
                if (outTempLen == 0)
                {
                    try { File.Delete(outTemp); } catch { }
                    ResetDecryptUiAfterFailure("解密结果为空文件。");
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
                    ResetDecryptUiAfterFailure("释放文件失败：" + (ex.Message ?? "未知错误") + "。解密结果仍保存在临时文件：" + outTemp);
                    return;
                }

                if (!File.Exists(outPath) || new FileInfo(outPath).Length == 0)
                {
                    ResetDecryptUiAfterFailure("释放后文件不存在或为空。");
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

        private void ResetDecryptUiAfterFailure(string statusText)
        {
            try
            {
                _lblStatus.ForeColor = Color.Firebrick;
                _lblStatus.Text = statusText;
                _btnDecrypt.Enabled = true;
                _btnBrowsePwd.Enabled = true;
                _cbMode.Enabled = true;
                _txtValue.Enabled = true;
            }
            catch { }
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

