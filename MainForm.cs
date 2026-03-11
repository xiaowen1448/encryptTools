using System;
using System.ComponentModel;
using System.Diagnostics;
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
using System.Reflection;
using EncryptTools.Ui;

namespace EncryptTools
{
    [SupportedOSPlatform("windows6.1")]
    public partial class MainForm : Form
    {
        private CancellationTokenSource? _cts;
        private EncryptToolsConfig _cfg = new EncryptToolsConfig();

        public MainForm()
        {
            InitializeComponent();
            // 设置窗口图标（任务栏图标/左上角logo）为 app.ico
            try
            {
                this.Icon = LoadEmbeddedAppIcon() ?? this.Icon;
            }
            catch { /* 忽略图标生成失败 */ }

            try { _cfg = ConfigHelper.Load(); } catch { _cfg = new EncryptToolsConfig(); }

            // Apply theme/backdrop and wire card actions
            ApplyThemeAndBackdrop();
            WireCardActions();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyThemeAndBackdrop();
        }

        private void ApplyThemeAndBackdrop()
        {
            bool dark = WindowsTheme.IsDarkMode();
            try { Backdrop.TryApplyMicaOrAcrylic(Handle, dark); } catch { }

            try
            {
                BackColor = dark ? Color.FromArgb(20, 20, 20) : Color.White;
                ForeColor = dark ? Color.Gainsboro : Color.Black;
                _lblSubTitle.ForeColor = dark ? Color.FromArgb(170, 170, 170) : Color.DimGray;
                _cardString.ApplyTheme(dark);
                _cardFile.ApplyTheme(dark);
                _cardImage.ApplyTheme(dark);
                _cardSmart.ApplyTheme(dark);
            }
            catch { }

            try
            {
                _statusLeft.Text = "就绪";
                _statusRight.Text = "v0.1";
            }
            catch { }
        }

        private void WireCardActions()
        {
            try
            {
                _cardString.PrimaryClick += (_, __) =>
                {
                    _statusLeft.Text = "字符串功能开发中";
                    MessageBox.Show(this, "字符串/剪贴板加密解密功能开发中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _statusLeft.Text = "就绪";
                };

                _cardFile.PrimaryClick += async (_, __) =>
                {
                    using var dlg = new OpenFileDialog { Title = "选择要加密/解密的文件", CheckFileExists = true };
                    if (dlg.ShowDialog(this) != DialogResult.OK) return;

                    _cfg.SourcePath = dlg.FileName;
                    try { ConfigHelper.Save(_cfg); } catch { }
                    _statusLeft.Text = "开始处理文件…";

                    // 默认做“加密”，解密可以后续在卡片内做二级按钮
                    await StartProcessAsync(true);
                    _statusLeft.Text = "就绪";
                };

                _cardFile.FileDropped += async (_, e) =>
                {
                    try
                    {
                        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                        {
                            _cfg.SourcePath = files[0];
                            try { ConfigHelper.Save(_cfg); } catch { }
                            _statusLeft.Text = "开始处理文件…";
                            await StartProcessAsync(true);
                            _statusLeft.Text = "就绪";
                        }
                    }
                    catch
                    {
                        _statusLeft.Text = "就绪";
                    }
                };

                _cardImage.PrimaryClick += (_, __) =>
                {
                    _statusLeft.Text = "图片像素化开发中";
                    MessageBox.Show(this, "图片像素化加密功能开发中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _statusLeft.Text = "就绪";
                };

                _cardSmart.PrimaryClick += (_, __) =>
                {
                    _statusLeft.Text = "智能解密开发中";
                    MessageBox.Show(this, "常见规则智能解密功能开发中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    _statusLeft.Text = "就绪";
                };
            }
            catch { }
        }

        private static Icon? LoadEmbeddedAppIcon()
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                var res = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(res)) return null;
                using var s = asm.GetManifestResourceStream(res);
                if (s == null) return null;
                return new Icon(s);
            }
            catch
            {
                return null;
            }
        }

        private void OpenSettings()
        {
            using var dlg = new SettingsForm(_cfg);
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _cfg = dlg.ResultConfig ?? ConfigHelper.Load();
                Log("设置已保存");
            }
        }

        private void OpenAbout()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/xiaowen1448/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Log("打开链接失败: " + ex.Message);
            }
        }

        private string LoadPasswordFromExeDir()
        {
            try
            {
                var exeDir = ConfigHelper.GetExeDir();
                var fileName = string.IsNullOrWhiteSpace(_cfg?.PasswordFileName) ? "password.pwd" : _cfg.PasswordFileName;
                var pwdPath = Path.Combine(exeDir, fileName);
                if (!File.Exists(pwdPath))
                {
                    Log("未找到密码文件，请先在「设置」中保存密码: " + pwdPath);
                    return "";
                }
                return PasswordFileHelper.LoadPasswordFromFile(pwdPath);
            }
            catch (Exception ex)
            {
                Log("读取密码文件失败: " + ex.Message);
                return "";
            }
        }

        private async Task StartProcessAsync(bool encrypt)
        {
            try
            {
                var sourcePath = (_cfg?.SourcePath ?? "").Trim();
                var outputPath = (_cfg?.OutputPath ?? "").Trim();
                var inPlace = true;
                var recursive = true;
                var randomName = false;
                var algorithmText = "AES-CBC";
                var password = LoadPasswordFromExeDir();
                var iterations = 200_000;
                var aesKeySizeBits = 256;

                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    MessageBox.Show(this, "请先在“文件加密/解密”卡片选择文件或拖拽文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
                {
                    MessageBox.Show(this, "源路径不存在。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 检查密码是否为空
                if (string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show(this, "密码未配置。请先在“设置”里保存密码文件（password.pwd）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!inPlace)
                {
                    if (string.IsNullOrWhiteSpace(outputPath))
                    {
                        Log("未选择输出路径，已自动使用源路径同级的 output 目录");
                        var autoOut = Path.Combine(Path.GetDirectoryName(sourcePath) ?? sourcePath, "output");
                        Directory.CreateDirectory(autoOut);
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
                    "AES-GCM" => CryptoAlgorithm.AesGcm,
                    "TripleDES" => CryptoAlgorithm.TripleDes,
                    _ => CryptoAlgorithm.Xor
                };

                SetBusy(true);
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
                    RandomizeFileName = randomName,
                    Algorithm = algorithm,
                    Password = password,
                    Iterations = iterations,
                    AesKeySizeBits = aesKeySizeBits,
                    Log = _ => { } // UI v0.1: no log box in main page
                };

                var encryptor = new FileEncryptor(options);
                if (encrypt)
                {
                    await encryptor.EncryptAsync(progress, _cts?.Token ?? CancellationToken.None);
                    MessageBox.Show(this, "加密完成。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    await encryptor.DecryptAsync(progress, _cts?.Token ?? CancellationToken.None);
                    MessageBox.Show(this, "解密完成。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (OperationCanceledException)
            {
                _statusLeft.Text = "已取消";
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
                    MessageBox.Show(this, "密码不正确或密码文件不可用，请在“设置”中重新保存密码。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show(this, "发生错误: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            finally
            {
                _cts?.Dispose();
                _cts = null;
                SetBusy(false);
            }
        }

        // v0.1 新版首页不展示日志/高级参数控件；保留设置与文件处理入口
        private void Log(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                _statusLeft.Text = text;
            }
            catch { }
        }

        private void SetBusy(bool busy)
        {
            try
            {
                _statusLeft.Text = busy ? "处理中…" : "就绪";
                _cardString.PrimaryEnabled = !busy;
                _cardFile.PrimaryEnabled = !busy;
                _cardImage.PrimaryEnabled = !busy;
                _cardSmart.PrimaryEnabled = !busy;
            }
            catch { }
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