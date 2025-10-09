using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EncryptTools
{
    [SupportedOSPlatform("windows6.1")]
    public partial class MainForm : Form
    {
        private CancellationTokenSource? _cts;
        private string? _importedPassword; // 存储从密码文件导入的实际密码

        public MainForm()
        {
            InitializeComponent();
            // 设置临时窗口图标（左上角logo）
            try
            {
                this.Icon = GenerateTemporaryIcon();
            }
            catch { /* 忽略图标生成失败 */ }
        }

        private void BrowseSource()
        {
            if (chkSelectFile.Checked)
            {
                using var fileDlg = new OpenFileDialog
                {
                    Title = "选择源文件",
                    CheckFileExists = true
                };
                if (fileDlg.ShowDialog(this) == DialogResult.OK)
                {
                    txtSourcePath.Text = fileDlg.FileName;
                }
            }
            else
            {
                using var folderDlg = new FolderBrowserDialog { Description = "选择源文件夹" };
                if (folderDlg.ShowDialog(this) == DialogResult.OK)
                {
                    txtSourcePath.Text = folderDlg.SelectedPath;
                }
            }
        }

        private void BrowseOutput()
        {
            using var fbd = new FolderBrowserDialog { Description = "选择输出文件夹" };
            if (fbd.ShowDialog(this) == DialogResult.OK)
            {
                txtOutputPath.Text = fbd.SelectedPath;
            }
        }

        private async Task StartProcessAsync(bool encrypt)
        {
            try
            {
                ClearLog();
                var sourcePath = txtSourcePath.Text.Trim();
                var outputPath = txtOutputPath.Text.Trim();
                var inPlace = chkInPlace.Checked;
                var recursive = chkRecursive.Checked;
                var algorithmText = cmbAlgorithm.SelectedItem?.ToString() ?? "AES-CBC";
                // 优先使用导入的密码，如果没有则使用输入框中的密码
                var password = _importedPassword ?? txtPassword.Text;
                var iterations = (int)nudIterations.Value;
                var aesKeySizeBits = 256;
                if (int.TryParse(cmbKeySize.SelectedItem?.ToString(), out var selBits))
                {
                    aesKeySizeBits = selBits;
                }

                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    AppendLog("请先选择源路径(文件或文件夹)");
                    return;
                }
                if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                {
                    AppendLog("源路径不存在");
                    return;
                }

                // 检查密码是否为空
                if (string.IsNullOrWhiteSpace(password))
                {
                    AppendLog("密码不能为空，请输入密码后再进行加密或解密操作");
                    return;
                }

                if (!inPlace)
                {
                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        AppendLog("未选择输出路径，已自动使用源路径同级的 output 目录");
                        var autoOut = Path.Combine(Path.GetDirectoryName(sourcePath) ?? sourcePath, "output");
                        Directory.CreateDirectory(autoOut);
                        txtOutputPath.Text = autoOut;
                        outputPath = autoOut;
                    }
                    else if (!Directory.Exists(outputPath))
                    {
                        Directory.CreateDirectory(outputPath);
                    }
                }

                var algorithm = algorithmText switch
                {
                    "AES-CBC" => CryptoAlgorithm.AesCbc,
                    "AES-GCM(小文件)" => CryptoAlgorithm.AesGcm,
                    "TripleDES" => CryptoAlgorithm.TripleDes,
                    _ => CryptoAlgorithm.Xor
                };

                ToggleUI(false);
                _cts = new CancellationTokenSource();

                var progress = new Progress<double>(p =>
                {
                    // 进度更新不再显示进度条，保留占位以便未来扩展。
                });

                var options = new FileEncryptorOptions
                {
                    SourcePath = sourcePath,
                    OutputRoot = inPlace ? "" : outputPath, // 使用空字符串而不是null
                    InPlace = inPlace,
                    Recursive = recursive,
                    Algorithm = algorithm,
                    Password = password,
                    Iterations = iterations,
                    AesKeySizeBits = aesKeySizeBits,
                    Log = AppendLog
                };

                var encryptor = new FileEncryptor(options);
                if (encrypt)
                {
                    await encryptor.EncryptAsync(progress, _cts?.Token ?? CancellationToken.None);
                    AppendLog("加密完成");
                }
                else
                {
                    await encryptor.DecryptAsync(progress, _cts?.Token ?? CancellationToken.None);
                    AppendLog("解密完成");
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("已取消");
            }
            catch (Exception ex)
            {
                // 检查是否为密码相关错误
                if (ex.Message.Contains("Padding is invalid") || 
                    ex.Message.Contains("padding") ||
                    ex.Message.Contains("Authentication tag mismatch") ||
                    ex.Message.Contains("authentication") ||
                    ex.Message.Contains("Invalid key") ||
                    ex.Message.Contains("密码") ||
                    ex.Message.Contains("password"))
                {
                    // 根据当前模式显示不同的错误信息
                    if (cmbPasswordType.SelectedIndex == 0) // 输入密码模式
                    {
                        AppendLog("密码不正确，请检查密码是否正确或文件是否损坏");
                    }
                    else // 密码文件模式
                    {
                        AppendLog("密码文件不可用，请检查密码文件是否完整或文件是否损坏");
                    }
                }
                else
                {
                    AppendLog("发生错误: " + ex.Message);
                }
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                ToggleUI(true);
            }
        }

        private void CancelProcessing()
        {
            _cts?.Cancel();
        }

        private void ToggleUI(bool enable)
        {
            txtSourcePath.Enabled = enable;
            btnBrowseSource.Enabled = enable;
            txtOutputPath.Enabled = enable;
            btnBrowseOutput.Enabled = enable;
            chkInPlace.Enabled = enable;
            chkRecursive.Enabled = enable;
            chkSelectFile.Enabled = enable;
            cmbAlgorithm.Enabled = enable;
            txtPassword.Enabled = enable;
            cmbPasswordType.Enabled = enable;
            btnSavePassword.Enabled = enable && btnSavePassword.Text != "已保存";
            btnImportPassword.Enabled = enable;
            nudIterations.Enabled = enable;
            btnEncrypt.Enabled = enable;
            btnDecrypt.Enabled = enable;
        }

        private void AppendLog(string text)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string>(AppendLog), text);
                return;
            }
            txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }

        private void ClearLog()
        {
            txtLog.Clear();
        }

        private void UpdateKeySizeAvailability()
        {
            var algo = cmbAlgorithm.SelectedItem?.ToString() ?? string.Empty;
            var isAes = algo.StartsWith("AES", StringComparison.OrdinalIgnoreCase);
            cmbKeySize.Enabled = isAes;
        }

        private void UpdatePasswordTypeUI()
        {
            var isInputPassword = cmbPasswordType.SelectedIndex == 0; // "输入密码"
            btnSavePassword.Visible = isInputPassword;
            btnImportPassword.Visible = !isInputPassword;
            
            // 控制密码输入框的可编辑状态和显示模式
            txtPassword.ReadOnly = !isInputPassword;
            txtPassword.UseSystemPasswordChar = isInputPassword; // 输入密码模式时隐藏，文件模式时显示明文
            
            // 设置输入框背景颜色
            if (!isInputPassword)
            {
                // 密码文件模式：设置为灰色背景
                txtPassword.BackColor = System.Drawing.SystemColors.Control;
            }
            else
            {
                // 输入密码模式：恢复默认白色背景
                txtPassword.BackColor = System.Drawing.SystemColors.Window;
            }
            
            if (!isInputPassword)
            {
                // 重置保存按钮状态
                btnSavePassword.Enabled = true;
                btnSavePassword.Text = "保存密码";
                // 清空密码输入框，准备显示文件名
                txtPassword.Text = "";
                // 清空导入的密码
                _importedPassword = null;
            }
            else
            {
                // 切换回输入密码模式时，清空并启用输入
                txtPassword.Text = "";
                // 清空导入的密码
                _importedPassword = null;
            }
        }

        private void OnPasswordTextChanged()
        {
            // 只有在"输入密码"模式下才处理文本变化
            if (cmbPasswordType.SelectedIndex == 0 && btnSavePassword.Text == "已保存")
            {
                // 密码内容发生变化，重置保存按钮状态
                btnSavePassword.Enabled = true;
                btnSavePassword.Text = "保存密码";
            }
        }

        private void SavePassword()
        {
            var password = txtPassword.Text;
            if (string.IsNullOrWhiteSpace(password))
            {
                AppendLog("请先输入密码");
                return;
            }

            using var saveDialog = new SaveFileDialog
            {
                Title = "保存密码文件",
                Filter = "密码文件 (*.pwd)|*.pwd",
                DefaultExt = "pwd"
            };

            if (saveDialog.ShowDialog(this) == DialogResult.OK)
            {
                try
                {
                    SavePasswordToFile(password, saveDialog.FileName);
                    AppendLog($"密码已保存到: {saveDialog.FileName}");
                    btnSavePassword.Enabled = false;
                    btnSavePassword.Text = "已保存";
                }
                catch (Exception ex)
                {
                    AppendLog($"保存密码失败: {ex.Message}");
                }
            }
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
                try
                {
                    var password = LoadPasswordFromFile(openDialog.FileName);
                    // 存储实际密码到一个私有字段，但在输入框中显示文件名
                    _importedPassword = password;
                    txtPassword.Text = Path.GetFileName(openDialog.FileName);
                    AppendLog($"密码文件已导入: {openDialog.FileName}");
                }
                catch (Exception ex)
                {
                    AppendLog($"导入密码文件失败: {ex.Message}");
                }
            }
        }

        private void SavePasswordToFile(string password, string filePath)
        {
            // 使用AES-GCM 256位加密密码
            var key = new byte[32]; // 256位密钥
            var nonce = new byte[12]; // AES-GCM标准nonce长度
            
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(key);
            rng.GetBytes(nonce);

            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var ciphertext = new byte[passwordBytes.Length];
            var tag = new byte[16]; // 认证标签

            using var aesGcm = new AesGcm(key, 16);
            aesGcm.Encrypt(nonce, passwordBytes, ciphertext, tag);

            // 保存格式: Key(32字节) + Nonce(12字节) + Tag(16字节) + 加密的密码
            using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            fs.Write(key, 0, key.Length);
            fs.Write(nonce, 0, nonce.Length);
            fs.Write(tag, 0, tag.Length);
            fs.Write(ciphertext, 0, ciphertext.Length);
        }

        private string LoadPasswordFromFile(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            
            if (fs.Length < 60) // 至少需要32+12+16=60字节
            {
                throw new InvalidDataException("密码文件格式不正确");
            }
            
            // 读取Key(32字节)
            var key = new byte[32];
            fs.Read(key, 0, 32);
            
            // 读取Nonce(12字节)
            var nonce = new byte[12];
            fs.Read(nonce, 0, 12);
            
            // 读取Tag(16字节)
            var tag = new byte[16];
            fs.Read(tag, 0, 16);
            
            // 读取加密的密码
            var ciphertext = new byte[fs.Length - 60];
            fs.Read(ciphertext, 0, ciphertext.Length);

            // 解密密码
            var plaintext = new byte[ciphertext.Length];
            using var aesGcm = new AesGcm(key, 16);
            
            try
            {
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
                return Encoding.UTF8.GetString(plaintext);
            }
            catch (CryptographicException)
            {
                throw new InvalidDataException("密码文件已损坏或格式不正确");
            }
        }
        // 临时生成一个窗口图标（不影响 exe 的打包图标设置）
        private Icon GenerateTemporaryIcon()
        {
            var bmp = new Bitmap(64, 64);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                // 背景渐变
                using var bg = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 64, 64), Color.FromArgb(40, 120, 220), Color.FromArgb(20, 60, 120), 45f);
                g.FillRectangle(bg, 0, 0, 64, 64);
                // 圆形装饰
                using var circleBrush = new SolidBrush(Color.FromArgb(80, Color.White));
                g.FillEllipse(circleBrush, 6, 6, 52, 52);
                // 文字 ET
                using var font = new Font("Segoe UI", 26, FontStyle.Bold, GraphicsUnit.Pixel);
                var text = "ET";
                var size = g.MeasureString(text, font);
                var x = (64 - size.Width) / 2f;
                var y = (64 - size.Height) / 2f - 2f;
                using var textBrush = new SolidBrush(Color.White);
                g.DrawString(text, font, textBrush, x, y);
            }

            // 转为 Icon 对象
            var hIcon = bmp.GetHicon();
            var icon = Icon.FromHandle(hIcon);
            // 复制到托管 Icon 后释放原始句柄，避免资源泄漏
            using var ms = new MemoryStream();
            icon.Save(ms);
            ms.Position = 0;
            var managedIcon = new Icon(ms);
            DestroyIcon(hIcon);
            return managedIcon;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}