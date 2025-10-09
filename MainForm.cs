using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EncryptTools
{
    [SupportedOSPlatform("windows6.1")]
    public partial class MainForm : Form
    {
        private CancellationTokenSource? _cts;

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
                var password = txtPassword.Text;
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
                AppendLog("发生错误: " + ex.Message);
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