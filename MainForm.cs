using System;
using System.ComponentModel;
using System.Collections.Generic;
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
                    // 如果未选择路径，打开对话框让用户选择（仅导入路径，不立即开始加密）
                    if (string.IsNullOrWhiteSpace(_cfg?.SourcePath) || (!File.Exists(_cfg.SourcePath) && !Directory.Exists(_cfg.SourcePath)))
                    {
                        using var dlg = new OpenFileDialog { Title = "选择要加密/解密的文件", CheckFileExists = false };
                        if (dlg.ShowDialog(this) != DialogResult.OK) return;

                        _cfg.SourcePath = dlg.FileName;
                        try { ConfigHelper.Save(_cfg); } catch { }
                        _statusLeft.Text = $"已选择: {_cfg.SourcePath}";
                        // 按用户要求：导入路径后不立即递归扫描大目录。提醒用户点击加密开始计算并执行。
                        MessageBox.Show(this, "路径已导入。点击“加密当前剪贴板/加密”按钮以开始计算文件统计并执行加密。", "已导入", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    // 已有路径：先异步递归计算文件数量与总大小，再询问是否开始加密
                    try
                    {
                        _statusLeft.Text = "正在统计文件...";
                        var cts = new CancellationTokenSource();
                        var progress = new Progress<(long files, long bytes)>(t =>
                        {
                            // 实时更新状态，显示已扫描文件数与大小（近似）
                            _statusLeft.Text = $"扫描中: {t.files} 个文件, {FormatBytes(t.bytes)}";
                        });

                        var (filesCount, totalBytes) = await CountFilesAndBytesAsync(_cfg.SourcePath, recursive: true, progress, cts.Token);
                        _statusLeft.Text = $"检测完成: {filesCount} 个文件, {FormatBytes(totalBytes)}";

                        var ask = MessageBox.Show(this, $"检测到 {filesCount} 个文件，总大小 {FormatBytes(totalBytes)}。\n是否开始加密？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (ask == DialogResult.Yes)
                        {
                            _statusLeft.Text = "开始处理文件…";
                            await StartProcessAsync(true);
                            _statusLeft.Text = "就绪";
                        }
                        else
                        {
                            _statusLeft.Text = "已取消";
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        _statusLeft.Text = "已取消";
                    }
                    catch (Exception ex)
                    {
                        _statusLeft.Text = "就绪";
                        MessageBox.Show(this, "统计文件时出错: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                };

                _cardFile.FileDropped += (_, e) =>
                {
                    try
                    {
                        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                        {
                            _cfg.SourcePath = files[0];
                            try { ConfigHelper.Save(_cfg); } catch { }
                            _statusLeft.Text = $"路径已导入: {_cfg.SourcePath}";
                            // 不在此处开始处理；等待用户点击加密按钮后再递归统计与执行
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
                    ,
                    FileProgress = (processedFiles, totalFiles) =>
                    {
                        try
                        {
                            var txt = $"处理中… ({processedFiles}/{totalFiles})";
                            if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = txt));
                            else _statusLeft.Text = txt;
                        }
                        catch { }
                    }
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

        /// <summary>
        /// 异步计算路径下文件数量与总字节数（在后台线程执行，期间不会阻塞 UI）。
        /// progress 可用于实时上报已扫描文件与字节数。
        /// </summary>
        private Task<(long files, long bytes)> CountFilesAndBytesAsync(string path, bool recursive, IProgress<(long files, long bytes)>? progress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                long files = 0;
                long bytes = 0;

                if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            var fi = new FileInfo(path);
                            files = 1;
                            bytes = fi.Length;
                            progress?.Report((files, bytes));
                        }
                        catch { }
                        return (files, bytes);
                    }

                    // 迭代遍历目录，避免 EnumerateFiles(..., AllDirectories) 在遇到受限目录时抛出并导致中断
                    var stack = new System.Collections.Generic.Stack<string>();
                    stack.Push(path);

                    while (stack.Count > 0)
                    {
                        if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
                        var dir = stack.Pop();
                        try
                        {
                            IEnumerable<string> fileEnum;
                            try { fileEnum = Directory.EnumerateFiles(dir); }
                            catch { fileEnum = Array.Empty<string>(); }
                            foreach (var f in fileEnum)
                            {
                                if (ct.IsCancellationRequested) ct.ThrowIfCancellationRequested();
                                try
                                {
                                    var fi = new FileInfo(f);
                                    files++;
                                    bytes += fi.Length;
                                }
                                catch { }
                                if ((files & 0x3FF) == 0) // 每1024个文件更新一次，避免频繁 UI 调用
                                    progress?.Report((files, bytes));
                            }

                            if (recursive)
                            {
                                IEnumerable<string> dirEnum;
                                try { dirEnum = Directory.EnumerateDirectories(dir); }
                                catch { dirEnum = Array.Empty<string>(); }
                                foreach (var sub in dirEnum)
                                {
                                    stack.Push(sub);
                                }
                            }
                        }
                        catch { /* 忽略单目录异常，继续 */ }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* 忽略其它错误，返回已有统计 */ }

                progress?.Report((files, bytes));
                return (files, bytes);
            }, ct);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024.0;
            if (kb < 1024) return kb.ToString("0.##") + " KB";
            double mb = kb / 1024.0;
            if (mb < 1024) return mb.ToString("0.##") + " MB";
            double gb = mb / 1024.0;
            if (gb < 1024) return gb.ToString("0.##") + " GB";
            double tb = gb / 1024.0;
            return tb.ToString("0.##") + " TB";
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