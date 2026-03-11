using System;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace EncryptTools
{
    /// <summary>
    /// 仅用于快速验证文件加密/解密的基础测试窗体（.NET 8，WinForms）。
    /// </summary>
    public sealed class QuickTestForm : Form
    {
        private TextBox _txtSource = null!;
        private TextBox _txtOutput = null!;
        private TextBox _txtPassword = null!;
        private TextBox _txtLog = null!;
        private Button _btnEncrypt = null!;
        private Button _btnDecrypt = null!;
        private Button _btnCancel = null!;

        private CancellationTokenSource? _cts;

        public QuickTestForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            Text = "encryptTools - 文件快速加密测试";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(800, 520);
            MinimumSize = new Size(720, 400);
            Font = new Font("Microsoft YaHei UI", 9F);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 5,
                Padding = new Padding(8),
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 源文件
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 输出目录
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 密码
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 按钮
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));// 日志

            // 源文件
            var lblSource = new Label { Text = "源文件:", AutoSize = true, Anchor = AnchorStyles.Left };
            _txtSource = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            var btnBrowseSource = new Button { Text = "浏览...", AutoSize = true };
            btnBrowseSource.Click += (_, __) => BrowseSource();
            layout.Controls.Add(lblSource, 0, 0);
            layout.Controls.Add(_txtSource, 1, 0);
            layout.Controls.Add(btnBrowseSource, 2, 0);

            // 输出目录
            var lblOutput = new Label { Text = "输出目录:", AutoSize = true, Anchor = AnchorStyles.Left };
            _txtOutput = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            var btnBrowseOutput = new Button { Text = "选择...", AutoSize = true };
            btnBrowseOutput.Click += (_, __) => BrowseOutput();
            layout.Controls.Add(lblOutput, 0, 1);
            layout.Controls.Add(_txtOutput, 1, 1);
            layout.Controls.Add(btnBrowseOutput, 2, 1);

            // 密码
            var lblPassword = new Label { Text = "密码:", AutoSize = true, Anchor = AnchorStyles.Left };
            _txtPassword = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, UseSystemPasswordChar = true };
            layout.Controls.Add(lblPassword, 0, 2);
            layout.Controls.Add(_txtPassword, 1, 2);

            // 按钮
            var btnPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            _btnEncrypt = new Button { Text = "加密", AutoSize = true };
            _btnDecrypt = new Button { Text = "解密", AutoSize = true };
            _btnCancel = new Button { Text = "取消", AutoSize = true, Enabled = false };
            _btnEncrypt.Click += async (_, __) => await StartAsync(true);
            _btnDecrypt.Click += async (_, __) => await StartAsync(false);
            _btnCancel.Click += (_, __) => CancelCurrent();
            btnPanel.Controls.Add(_btnEncrypt);
            btnPanel.Controls.Add(_btnDecrypt);
            btnPanel.Controls.Add(_btnCancel);
            layout.Controls.Add(new Label { Text = "操作:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
            layout.Controls.Add(btnPanel, 1, 3);

            // 日志
            var lblLog = new Label { Text = "日志:", AutoSize = true, Anchor = AnchorStyles.Left };
            _txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(lblLog, 0, 4);
            layout.Controls.Add(_txtLog, 1, 4);
            layout.SetColumnSpan(_txtLog, 2);

            Controls.Add(layout);
        }

        private void BrowseSource()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "选择要加密/解密的文件",
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtSource.Text = dlg.FileName;
        }

        private void BrowseOutput()
        {
            using var dlg = new FolderBrowserDialog { Description = "选择输出目录" };
            if (dlg.ShowDialog(this) == DialogResult.OK)
                _txtOutput.Text = dlg.SelectedPath;
        }

        private async Task StartAsync(bool encrypt)
        {
            CancelCurrent();
            _cts = new CancellationTokenSource();
            ToggleUi(false);
            try
            {
                var src = _txtSource.Text.Trim();
                var outDir = _txtOutput.Text.Trim();
                var pwd = _txtPassword.Text;

                if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                {
                    AppendLog("请先选择存在的源文件。");
                    return;
                }

                if (string.IsNullOrWhiteSpace(outDir))
                {
                    outDir = Path.Combine(Path.GetDirectoryName(src) ?? Environment.CurrentDirectory, "output");
                    Directory.CreateDirectory(outDir);
                    _txtOutput.Text = outDir;
                    AppendLog($"未选择输出目录，已自动使用: {outDir}");
                }
                else if (!Directory.Exists(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                if (string.IsNullOrWhiteSpace(pwd))
                {
                    AppendLog("密码不能为空。");
                    return;
                }

                var options = new FileEncryptorOptions
                {
                    SourcePath = src,
                    OutputRoot = outDir,
                    InPlace = false,
                    Recursive = false,
                    RandomizeFileName = false,
                    Algorithm = CryptoAlgorithm.AesCbc,
                    Password = pwd,
                    Iterations = 200_000,
                    AesKeySizeBits = 256,
                    Log = AppendLog
                };

                var enc = new FileEncryptor(options);
                var progress = new Progress<double>(_ => { });

                AppendLog(encrypt ? "开始加密..." : "开始解密...");
                if (encrypt)
                    await enc.EncryptAsync(progress, _cts.Token);
                else
                    await enc.DecryptAsync(progress, _cts.Token);

                AppendLog(encrypt ? "加密完成。" : "解密完成。");
            }
            catch (OperationCanceledException)
            {
                AppendLog("已取消。");
            }
            catch (Exception ex)
            {
                AppendLog("发生错误: " + ex.Message);
            }
            finally
            {
                ToggleUi(true);
            }
        }

        private void CancelCurrent()
        {
            try { _cts?.Cancel(); } catch { }
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }

        private void ToggleUi(bool enable)
        {
            _btnEncrypt.Enabled = enable;
            _btnDecrypt.Enabled = enable;
            _btnCancel.Enabled = !enable;
        }

        private void AppendLog(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), text);
                return;
            }
            _txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        }
    }
}

