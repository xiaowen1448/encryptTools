using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using EncryptTools.PasswordFile;

namespace EncryptTools.Ui
{
    /// <summary>
    /// 图片像素化工作区内容：多图选择/拖拽、按图片名称 sheet 切换、原图与加密预览、保存输出。
    /// </summary>
    public sealed class ImageWorkspacePanel : UserControl
    {
        private readonly Action<string> _log;
        private readonly Action<double>? _reportProgress;
        private readonly List<string> _imagePaths = new List<string>();
        private TabControl _sheetTabs = null!;
        private ComboBox _cbMode = null!;
        private ComboBox _cbBlock = null!;
        private ComboBox _cbPwdFiles = null!;
        private string? _passwordFilePath;
        private NumericUpDown _numIterations = null!;
        private NumericUpDown _numArnoldIterations = null!;
        private bool _lastActionWasDecrypt;

        public ImageWorkspacePanel(Action<string> log, Action<double>? reportProgress = null)
        {
            _log = log ?? (_ => { });
            _reportProgress = reportProgress;
            Dock = DockStyle.Fill;
            BuildUi();
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SystemColors.ControlLight,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = new Padding(8, 6, 8, 6)
            };
            var btnSelect = new Button { Text = "选择图片", AutoSize = true, Margin = new Padding(0, 0, 8, 4) };
            var lblHint = new Label { Text = "支持多选、拖拽图片或文件夹", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 16, 0) };
            _cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 4, 8, 4), MinimumSize = new Size(100, 0) };
            _cbMode.Items.AddRange(new object[] { "不可逆马赛克(仅效果)", "密钥置乱(可逆)", "像素XOR(可逆)", "分块置乱(可逆)", "Arnold猫映射(可逆)" });
            _cbMode.SelectedIndex = 1;
            _cbMode.DropDown += (_, __) => SetComboDropDownWidth(_cbMode);
            SetComboDropDownWidth(_cbMode);
            _cbBlock = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 4, 8, 4), MinimumSize = new Size(50, 0) };
            _cbBlock.Items.AddRange(new object[] { "4x4", "8x8", "16x16", "32x32" });
            _cbBlock.SelectedIndex = 2;
            _cbBlock.DropDown += (_, __) => SetComboDropDownWidth(_cbBlock);
            SetComboDropDownWidth(_cbBlock);
            var btnEncrypt = new Button { Text = "加密(批量)", BackColor = Color.RoyalBlue, ForeColor = Color.White, AutoSize = true, Margin = new Padding(8, 0, 4, 4) };
            var btnDecrypt = new Button { Text = "解密(批量)", BackColor = Color.SeaGreen, ForeColor = Color.White, AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnSave = new Button { Text = "保存输出(批量)", AutoSize = true, Margin = new Padding(4, 0, 4, 4) };

            _cbPwdFiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 4, 8, 4), MinimumSize = new Size(120, 0) };
            _cbPwdFiles.DropDown += (_, __) => SetComboDropDownWidth(_cbPwdFiles);
            _numIterations = new NumericUpDown { Minimum = 10_000, Maximum = 1_000_000, Increment = 10_000, Value = 200_000, Width = 90, Margin = new Padding(4, 4, 8, 4) };
            _numArnoldIterations = new NumericUpDown { Minimum = 1, Maximum = 200, Increment = 1, Value = 10, Width = 60, Margin = new Padding(4, 4, 8, 4) };

            toolbar.Controls.Add(btnSelect);
            toolbar.Controls.Add(lblHint);
            toolbar.Controls.Add(new Label { Text = "像素化:", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
            toolbar.Controls.Add(_cbMode);
            toolbar.Controls.Add(new Label { Text = "密码文件:", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
            toolbar.Controls.Add(_cbPwdFiles);
            toolbar.Controls.Add(new Label { Text = "块:", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
            toolbar.Controls.Add(_cbBlock);
            toolbar.Controls.Add(new Label { Text = "迭代:", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
            toolbar.Controls.Add(_numIterations);
            toolbar.Controls.Add(new Label { Text = "Arnold:", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
            toolbar.Controls.Add(_numArnoldIterations);
            toolbar.Controls.Add(btnEncrypt);
            toolbar.Controls.Add(btnDecrypt);
            toolbar.Controls.Add(btnSave);

            _sheetTabs = new TabControl { Dock = DockStyle.Fill };
            _sheetTabs.TabPages.Clear();

            var contentHost = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.Control };
            var placeholder = new Label
            {
                Text = "选择图片或拖拽到此处",
                AutoSize = false,
                Dock = DockStyle.None,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Microsoft YaHei UI", 12f),
                ForeColor = Color.Gray
            };
            placeholder.Bounds = new Rectangle(20, 20, 400, 80);
            placeholder.Anchor = AnchorStyles.None;
            contentHost.Resize += (_, __) =>
            {
                placeholder.Location = new Point((contentHost.Width - placeholder.Width) / 2, Math.Max(20, (contentHost.Height - placeholder.Height) / 2 - 40));
            };
            contentHost.Controls.Add(placeholder);
            contentHost.Controls.Add(_sheetTabs);
            _sheetTabs.Visible = false;
            placeholder.Visible = true;

            var bottomStatus = new Panel { Dock = DockStyle.Fill };
            var lblStatus = new Label { Text = "就绪", Dock = DockStyle.Left, AutoSize = true };
            bottomStatus.Controls.Add(lblStatus);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(contentHost, 0, 1);
            root.Controls.Add(bottomStatus, 0, 2);

            Controls.Add(root);

            contentHost.AllowDrop = true;
            contentHost.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };
            contentHost.DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    foreach (var p in paths)
                    {
                        if (Directory.Exists(p))
                            AddImagesFromFolder(p);
                        else
                            AddImagePath(p);
                    }
                }
            };

            btnSelect.Click += (_, __) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
                    Multiselect = true
                };
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK && dlg.FileNames.Length > 0)
                {
                    foreach (var f in dlg.FileNames)
                        AddImagePath(f);
                }
            };

            btnEncrypt.Click += (_, __) => _ = RunEncryptAsync();
            btnDecrypt.Click += async (_, __) => await RunDecryptAsync();
            btnSave.Click += (_, __) => SaveBatchOutput();

            _cbMode.SelectedIndexChanged += (_, __) => UpdateModeUiState(btnDecrypt);
            UpdateModeUiState(btnDecrypt);

            _cbPwdFiles.DropDown += (_, __) => RefreshPasswordFiles();
            _cbPwdFiles.SelectedIndexChanged += (_, __) =>
            {
                if (_cbPwdFiles.SelectedItem is PasswordFileItem item)
                {
                    _passwordFilePath = string.IsNullOrWhiteSpace(item.FullPath) ? null : item.FullPath;
                    _log($"[{DateTime.Now:HH:mm:ss}] 已选择密码文件: {item.DisplayName}");
                }
            };
            RefreshPasswordFiles();

            // 图片 Tab 右键菜单：关闭此文件 / 关闭其他文件
            var imageTabMenu = new ContextMenuStrip();
            var miCloseThisFile = new ToolStripMenuItem("关闭此文件", null, (_, __) =>
            {
                if (_sheetTabs.SelectedTab != null)
                {
                    var t = _sheetTabs.SelectedTab;
                    _sheetTabs.TabPages.Remove(t);
                    if (t.Tag is ImageSheetState s && s.EncryptedImage != null) s.EncryptedImage.Dispose();
                    if (_sheetTabs.TabPages.Count == 0 && _sheetTabs.Parent is Panel host)
                    {
                        var ph = host.Controls.OfType<Label>().FirstOrDefault(l => l.Text.Contains("选择图片"));
                        if (ph != null) { ph.Visible = true; _sheetTabs.Visible = false; }
                    }
                }
            });
            var miCloseOthers = new ToolStripMenuItem("关闭其他文件", null, (_, __) =>
            {
                var keep = _sheetTabs.SelectedTab;
                if (keep == null) return;
                for (int i = _sheetTabs.TabPages.Count - 1; i >= 0; i--)
                {
                    var t = _sheetTabs.TabPages[i];
                    if (t != keep)
                    {
                        _sheetTabs.TabPages.Remove(t);
                        if (t.Tag is ImageSheetState s && s.EncryptedImage != null) s.EncryptedImage.Dispose();
                    }
                }
            });
            imageTabMenu.Items.Add(miCloseThisFile);
            imageTabMenu.Items.Add(miCloseOthers);
            _sheetTabs.ContextMenuStrip = imageTabMenu;
            _sheetTabs.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Right) return;
                for (int i = 0; i < _sheetTabs.TabCount; i++)
                {
                    if (_sheetTabs.GetTabRect(i).Contains(e.Location))
                    {
                        _sheetTabs.SelectedIndex = i;
                        imageTabMenu.Show(_sheetTabs, e.Location);
                        break;
                    }
                }
            };
        }

        private async Task RunEncryptAsync()
        {
            if (_sheetTabs.TabPages.Count == 0) return;
            _lastActionWasDecrypt = false;
            _reportProgress?.Invoke(0);
            var progress = new Progress<int>(p => { _reportProgress?.Invoke(p / 100.0); });
            try
            {
                var password = TryLoadPasswordFromPwdFile();
                var modeNow = (ImageMode)Math.Max(0, _cbMode.SelectedIndex);
                if (modeNow != ImageMode.Mosaic && string.IsNullOrEmpty(password))
                    return;

                int total = _sheetTabs.TabPages.Count;
                for (int i = 0; i < total; i++)
                {
                    var tab = _sheetTabs.TabPages[i];
                    var path = tab.Tag is ImageSheetState s ? s.Path : tab.Tag as string;
                    if (string.IsNullOrEmpty(path)) continue;
                    var rightBox = GetRightPictureBox(tab);
                    if (rightBox == null) continue;
                    var options = BuildOptionsFromUi();
                    Bitmap? processed = null;
                    await Task.Run(() =>
                    {
                        using var orig = Image.FromFile(path);
                        processed = ApplyPixelEffect(orig, options, password, encrypt: true);
                        ((IProgress<int>)progress).Report((i + 1) * 100 / total);
                    }).ConfigureAwait(true);
                    if (processed != null && !rightBox.IsDisposed)
                    {
                        rightBox.Image?.Dispose();
                        rightBox.Image = processed;
                        rightBox.Size = FitThumbnailSize(processed.Size, 480, 360);
                        tab.Tag = new ImageSheetState { Path = path, EncryptedImage = (Bitmap)processed.Clone(), Options = options };
                        _log($"[{DateTime.Now:HH:mm:ss}] 加密完成: {Path.GetFileName(path)}");
                    }
                }
                _log($"[{DateTime.Now:HH:mm:ss}] 加密预览完成。");
            }
            finally
            {
                _reportProgress?.Invoke(-1);
            }
        }

        private async Task RunDecryptAsync()
        {
            if (_sheetTabs.TabPages.Count == 0) return;
            _lastActionWasDecrypt = true;
            var password = TryLoadPasswordFromPwdFile();
            if (string.IsNullOrEmpty(password)) return;

            int total = _sheetTabs.TabPages.Count;
            for (int i = 0; i < total; i++)
            {
                var tab = _sheetTabs.TabPages[i];
                if (tab.Tag is not ImageSheetState state)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 跳过(无状态): {tab.Text}");
                    continue;
                }
                if (state.EncryptedImage == null)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 跳过(非加密图): {Path.GetFileName(state.Path)}");
                    continue;
                }
                if (state.Options == null)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 跳过(缺少元数据): {Path.GetFileName(state.Path)}");
                    continue;
                }
                if (state.Options.Mode == ImageMode.Mosaic)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 跳过(不可逆模式): {Path.GetFileName(state.Path)}");
                    continue;
                }
                var rightBox = GetRightPictureBox(tab);
                if (rightBox == null) continue;

                try
                {
                    Bitmap? decrypted = null;
                    await Task.Run(() =>
                    {
                        decrypted = ApplyPixelEffect(state.EncryptedImage, state.Options, password, encrypt: false);
                    }).ConfigureAwait(true);
                    if (decrypted != null && !rightBox.IsDisposed)
                    {
                        rightBox.Image?.Dispose();
                        rightBox.Image = decrypted;
                        rightBox.Size = FitThumbnailSize(decrypted.Size, 480, 360);
                        _log($"[{DateTime.Now:HH:mm:ss}] 解密完成: {Path.GetFileName(state.Path)}");
                    }
                }
                catch
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 解密失败(密码不匹配或参数不一致): {Path.GetFileName(state.Path)}");
                }
            }
            _log($"[{DateTime.Now:HH:mm:ss}] 批量解密完成。");
        }

        private static PictureBox? GetRightPictureBox(TabPage tab)
        {
            var split = tab.Controls.OfType<SplitContainer>().FirstOrDefault();
            if (split?.Panel2 == null) return null;
            return FindFirstPictureBox(split.Panel2);
        }

        private static PictureBox? FindFirstPictureBox(Control c)
        {
            if (c is PictureBox pb) return pb;
            foreach (Control child in c.Controls)
            {
                var found = FindFirstPictureBox(child);
                if (found != null) return found;
            }
            return null;
        }

        private sealed class ImageSheetState
        {
            public string Path = "";
            public Bitmap? EncryptedImage;
            public ImageEffectOptions? Options;
        }

        private sealed class PasswordFileItem
        {
            public required string DisplayName { get; init; }
            public required string FullPath { get; init; }
            public override string ToString() => DisplayName;
        }

        private enum ImageMode
        {
            Mosaic = 0,
            Permutation = 1,
            XorStream = 2,
            BlockShuffle = 3,
            ArnoldCat = 4
        }

        private sealed class ImageEffectOptions
        {
            public int Version { get; set; } = 1;
            public ImageMode Mode { get; set; }
            public int BlockSize { get; set; } = 16;
            public int Iterations { get; set; } = 200_000;
            public int ArnoldIterations { get; set; } = 10;
            public string SaltBase64 { get; set; } = "";
        }

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };

        private void AddImagePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            var ext = Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext) || !ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase)) return;
            if (_imagePaths.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
            _imagePaths.Add(path);
            AddSheet(path);
            _log($"[{DateTime.Now:HH:mm:ss}] 已添加: {Path.GetFileName(path)}");
        }

        private void AddImagesFromFolder(string folderPath)
        {
            try
            {
                var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                foreach (var f in files)
                    AddImagePath(f);
                _log($"[{DateTime.Now:HH:mm:ss}] 从文件夹递归添加 {files.Length} 张图片。");
            }
            catch (Exception ex)
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 扫描文件夹失败: {ex.Message}");
            }
        }

        private void AddSheet(string imagePath)
        {
            if (_sheetTabs.Parent is Panel host)
            {
                var placeholder = host.Controls.OfType<Label>().FirstOrDefault(l => l.Text.Contains("选择图片"));
                if (placeholder != null && _sheetTabs.TabPages.Count == 0)
                {
                    placeholder.Visible = false;
                    _sheetTabs.Visible = true;
                }
            }
            var name = Path.GetFileName(imagePath);
            var tab = new TabPage(name) { Tag = imagePath };
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 8,
                Panel1MinSize = 80,
                Panel2MinSize = 80
            };
            var splitInitialized = false;
            split.SizeChanged += (s, _) =>
            {
                var sp = (SplitContainer)s;
                if (!splitInitialized && sp.Width > 10) { sp.SplitterDistance = sp.Width / 2; splitInitialized = true; }
            };
            if (split.Width > 10)
            {
                split.SplitterDistance = split.Width / 2;
                splitInitialized = true;
            }

            var leftPanel = CreatePreviewPanel(out PictureBox leftBox);
            var rightPanel = CreatePreviewPanel(out PictureBox rightBox);
            try
            {
                leftBox.Image = Image.FromFile(imagePath);
            }
            catch { }
            rightBox.Tag = imagePath;

            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(rightPanel);
            tab.Controls.Add(split);
            try
            {
                if (leftBox.Image != null)
                {
                    var sz = FitThumbnailSize(leftBox.Image.Size, 480, 360);
                    leftBox.Size = sz;
                }
            }
            catch { }
            _sheetTabs.TabPages.Add(tab);
            _sheetTabs.SelectedTab = tab;

            // 若存在元数据 sidecar，则认为导入的是“加密图”，等待输入密码后解密预览
            var metaPath = GetMetaPathForImage(imagePath);
            var meta = TryLoadMeta(metaPath);
            if (meta != null && leftBox.Image != null)
            {
                try
                {
                    tab.Tag = new ImageSheetState { Path = imagePath, EncryptedImage = new Bitmap(leftBox.Image), Options = meta };
                    _log($"[{DateTime.Now:HH:mm:ss}] 检测到元数据: {Path.GetFileName(metaPath)}。请输入密码后点击「解密」预览。");
                }
                catch { }
            }
            else
            {
                // 导入后右侧默认按当前加密方式展示预览
                if (leftBox.Image != null)
                    _ = UpdateRightPreviewForSheetAsync(tab, imagePath, leftBox.Image, rightBox);
            }
        }

        private async Task UpdateRightPreviewForSheetAsync(TabPage tab, string imagePath, Image leftImage, PictureBox rightBox)
        {
            var password = TryLoadPasswordFromPwdFile();
            var options = BuildOptionsFromUi();
            Bitmap? processed = null;
            await Task.Run(() =>
            {
                processed = ApplyPixelEffect(leftImage, options, password, encrypt: true);
            }).ConfigureAwait(true);
            if (processed != null && !rightBox.IsDisposed && !tab.IsDisposed)
            {
                rightBox.Image?.Dispose();
                rightBox.Image = processed;
                rightBox.Size = FitThumbnailSize(processed.Size, 480, 360);
                tab.Tag = new ImageSheetState { Path = imagePath, EncryptedImage = (Bitmap)processed.Clone(), Options = options };
            }
        }

        private static Panel CreatePreviewPanel(out PictureBox picBox)
        {
            var outer = new Panel { Dock = DockStyle.Fill };
            var toolBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, FlowDirection = FlowDirection.LeftToRight, BackColor = SystemColors.ControlDark };
            var btnZoom100 = new Button { Text = "1:1", AutoSize = true, Margin = new Padding(2) };
            toolBar.Controls.Add(btnZoom100);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var pb = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 0), BorderStyle = BorderStyle.FixedSingle };
            scroll.Controls.Add(pb);
            outer.Controls.Add(scroll);
            outer.Controls.Add(toolBar);

            var baseSize = Size.Empty; // “刚打开时的比例大小”
            float zoom = 1f;
            void ApplyZoom()
            {
                if (pb.Image == null) return;
                if (baseSize.Width <= 0 || baseSize.Height <= 0)
                    baseSize = pb.Size;
                int w = (int)(baseSize.Width * zoom);
                int h = (int)(baseSize.Height * zoom);
                pb.Size = new Size(Math.Max(1, w), Math.Max(1, h));
            }
            btnZoom100.Click += (_, __) =>
            {
                zoom = 1f;
                if (baseSize.Width <= 0 || baseSize.Height <= 0)
                    baseSize = pb.Size;
                if (baseSize.Width > 0 && baseSize.Height > 0)
                    pb.Size = baseSize;
            };

            scroll.MouseWheel += (_, e) =>
            {
                if ((Control.ModifierKeys & Keys.Control) == 0) return;
                if (pb.Image == null) return;
                if (baseSize.Width <= 0 || baseSize.Height <= 0)
                    baseSize = pb.Size;
                zoom = e.Delta > 0 ? zoom * 1.1f : zoom / 1.1f;
                zoom = Math.Max(0.05f, Math.Min(20f, zoom));
                ApplyZoom();
            };
            scroll.MouseEnter += (_, __) => scroll.Focus();
            scroll.TabStop = true;

            pb.SizeChanged += (_, __) =>
            {
                // 外部首次设置“适配大小”时，把它当成 baseSize
                if (baseSize.Width <= 0 || baseSize.Height <= 0)
                    baseSize = pb.Size;
            };
            picBox = pb;
            return outer;
        }

        private static Size FitThumbnailSize(Size imageSize, int maxW, int maxH)
        {
            if (imageSize.Width <= 0 || imageSize.Height <= 0) return new Size(maxW, maxH);
            double r = Math.Min((double)maxW / imageSize.Width, (double)maxH / imageSize.Height);
            if (r >= 1) return imageSize;
            return new Size((int)(imageSize.Width * r), (int)(imageSize.Height * r));
        }

        private void UpdateModeUiState(Button btnDecrypt)
        {
            var mode = (ImageMode)Math.Max(0, _cbMode.SelectedIndex);
            bool needsPassword = mode is ImageMode.Permutation or ImageMode.XorStream or ImageMode.BlockShuffle or ImageMode.ArnoldCat;
            _cbPwdFiles.Enabled = needsPassword;
            _numIterations.Enabled = needsPassword;
            _numArnoldIterations.Enabled = mode == ImageMode.ArnoldCat;
            btnDecrypt.Enabled = mode != ImageMode.Mosaic;
        }

        private void RefreshPasswordFiles()
        {
            try
            {
                PasswordFileService.EnsurePwdDirectory();
                var files = PasswordFileService.ListPwdFiles();

                _cbPwdFiles.BeginUpdate();
                _cbPwdFiles.Items.Clear();
                _cbPwdFiles.Items.Add(new PasswordFileItem { DisplayName = "(未选择)", FullPath = "" });
                foreach (var f in files)
                {
                    _cbPwdFiles.Items.Add(new PasswordFileItem { DisplayName = Path.GetFileName(f), FullPath = f });
                }
                _cbPwdFiles.EndUpdate();

                if (!string.IsNullOrWhiteSpace(_passwordFilePath))
                {
                    for (int i = 0; i < _cbPwdFiles.Items.Count; i++)
                    {
                        if (_cbPwdFiles.Items[i] is PasswordFileItem p && string.Equals(p.FullPath, _passwordFilePath, StringComparison.OrdinalIgnoreCase))
                        {
                            _cbPwdFiles.SelectedIndex = i;
                            return;
                        }
                    }
                }
                _cbPwdFiles.SelectedIndex = 0;
                SetComboDropDownWidth(_cbPwdFiles);
            }
            catch (Exception ex)
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 刷新密码文件失败: {ex.Message}");
            }
        }

        private static void SetComboDropDownWidth(ComboBox cb)
        {
            if (cb == null || cb.Items.Count == 0) return;
            int maxW = cb.Width;
            try
            {
                using (var g = cb.CreateGraphics())
                {
                    foreach (var item in cb.Items)
                    {
                        string s = (item as PasswordFileItem)?.DisplayName ?? item?.ToString() ?? "";
                        var w = (int)Math.Ceiling(g.MeasureString(s, cb.Font).Width) + 24;
                        if (w > maxW) maxW = w;
                    }
                }
                cb.DropDownWidth = Math.Max(maxW, 80);
                if (maxW > cb.Width)
                    cb.Width = Math.Min(maxW, 400);
            }
            catch { }
        }

        private string? TryLoadPasswordFromPwdFile()
        {
            if (string.IsNullOrWhiteSpace(_passwordFilePath) || !File.Exists(_passwordFilePath))
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 请先选择密码文件(.pwd)。");
                return null;
            }
            try
            {
                var pwd = PasswordFileHelper.LoadPasswordFromFile(_passwordFilePath);
                if (string.IsNullOrWhiteSpace(pwd))
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 密码为空。");
                    return null;
                }
                return pwd;
            }
            catch (Exception ex)
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 密码文件导入失败: {ex.Message}");
                return null;
            }
        }

        private ImageEffectOptions BuildOptionsFromUi()
        {
            int block = _cbBlock.SelectedIndex switch { 0 => 4, 1 => 8, 2 => 16, _ => 32 };
            var salt = new byte[16];
            EncryptTools.Compat.RngFill(salt);
            return new ImageEffectOptions
            {
                Mode = (ImageMode)Math.Max(0, _cbMode.SelectedIndex),
                BlockSize = block,
                Iterations = (int)_numIterations.Value,
                ArnoldIterations = (int)_numArnoldIterations.Value,
                SaltBase64 = Convert.ToBase64String(salt)
            };
        }

        private Bitmap ApplyPixelEffect(Image src, ImageEffectOptions options, string? password, bool encrypt)
        {
            var bmp = new Bitmap(src);
            return options.Mode switch
            {
                ImageMode.Mosaic => ApplyMosaic(bmp, options.BlockSize),
                ImageMode.Permutation => ApplyPermutation(bmp, options, password, encrypt),
                ImageMode.XorStream => ApplyXorStream(bmp, options, password),
                ImageMode.BlockShuffle => ApplyBlockShuffle(bmp, options, password, encrypt),
                ImageMode.ArnoldCat => ApplyArnoldCat(bmp, options, password, encrypt),
                _ => ApplyMosaic(bmp, options.BlockSize)
            };
        }

        private static Bitmap ApplyMosaic(Bitmap bmp, int block)
        {
            int w = bmp.Width, h = bmp.Height;
            for (int y = 0; y < h; y += block)
            {
                for (int x = 0; x < w; x += block)
                {
                    int r = 0, g = 0, b = 0, n = 0;
                    for (int dy = 0; dy < block && y + dy < h; dy++)
                    for (int dx = 0; dx < block && x + dx < w; dx++)
                    {
                        var c = bmp.GetPixel(x + dx, y + dy);
                        r += c.R; g += c.G; b += c.B; n++;
                    }
                    if (n > 0)
                    {
                        var avg = Color.FromArgb(255, r / n, g / n, b / n);
                        for (int dy = 0; dy < block && y + dy < h; dy++)
                        for (int dx = 0; dx < block && x + dx < w; dx++)
                            bmp.SetPixel(x + dx, y + dy, avg);
                    }
                }
            }
            return bmp;
        }

        private static byte[] DeriveKey(string password, ImageEffectOptions options, int keyLen)
        {
            var salt = Convert.FromBase64String(options.SaltBase64);
            using var kdf = new Rfc2898DeriveBytes(password, salt, options.Iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(keyLen);
        }

        private static Bitmap ApplyPermutation(Bitmap bmp, ImageEffectOptions options, string? password, bool encrypt)
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
            var key = DeriveKey(password, options, 32);
            int seed = BitConverter.ToInt32(EncryptTools.Compat.Sha256Hash(key), 0);
            return PermutePixels(bmp, seed, encrypt);
        }

        private static Bitmap PermutePixels(Bitmap bmp, int seed, bool encrypt)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int len = Math.Abs(stride) * bmp.Height;
                var buf = new byte[len];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, len);

                int nPixels = bmp.Width * bmp.Height;
                var perm = new int[nPixels];
                for (int i = 0; i < nPixels; i++) perm[i] = i;
                var rng = new Random(seed);
                for (int i = nPixels - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (perm[i], perm[j]) = (perm[j], perm[i]);
                }

                var outBuf = new byte[len];
                if (encrypt)
                {
                    for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int idx = y * bmp.Width + x;
                        int srcIdx = perm[idx];
                        int srcX = srcIdx % bmp.Width;
                        int srcY = srcIdx / bmp.Width;
                        int srcOff = srcY * stride + srcX * 4;
                        int dstOff = y * stride + x * 4;
                        Buffer.BlockCopy(buf, srcOff, outBuf, dstOff, 4);
                    }
                }
                else
                {
                    var inv = new int[nPixels];
                    for (int i = 0; i < nPixels; i++) inv[perm[i]] = i;
                    for (int y = 0; y < bmp.Height; y++)
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int idx = y * bmp.Width + x;
                        int srcIdx = inv[idx];
                        int srcX = srcIdx % bmp.Width;
                        int srcY = srcIdx / bmp.Width;
                        int srcOff = srcY * stride + srcX * 4;
                        int dstOff = y * stride + x * 4;
                        Buffer.BlockCopy(buf, srcOff, outBuf, dstOff, 4);
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(outBuf, 0, data.Scan0, len);
                return bmp;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static Bitmap ApplyXorStream(Bitmap bmp, ImageEffectOptions options, string? password)
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
            var key = DeriveKey(password, options, 32);
            return XorPixels(bmp, key);
        }

        private static Bitmap XorPixels(Bitmap bmp, byte[] key)
        {
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var data = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = data.Stride;
                int len = Math.Abs(stride) * bmp.Height;
                var buf = new byte[len];
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buf, 0, len);

                using var hmac = new HMACSHA256(key);
                Span<byte> counter = stackalloc byte[8];
                ulong ctr = 0;
                int offset = 0;
                while (offset < len)
                {
#if NET48
                    var ctrBytes = BitConverter.GetBytes(ctr++);
                    for (int i = 0; i < 8; i++) counter[i] = ctrBytes[i];
#else
                    BitConverter.TryWriteBytes(counter, ctr++);
#endif
                    var mac = hmac.ComputeHash(counter.ToArray());
                    int take = Math.Min(mac.Length, len - offset);
                    for (int i = 0; i < take; i++)
                        buf[offset + i] ^= mac[i];
                    offset += take;
                }

                System.Runtime.InteropServices.Marshal.Copy(buf, 0, data.Scan0, len);
                return bmp;
            }
            finally
            {
                bmp.UnlockBits(data);
            }
        }

        private static Bitmap ApplyBlockShuffle(Bitmap bmp, ImageEffectOptions options, string? password, bool encrypt)
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
            var key = DeriveKey(password, options, 32);
            int seed = BitConverter.ToInt32(EncryptTools.Compat.Sha256Hash(key), 0);
            return ShuffleBlocks(bmp, options.BlockSize, seed, encrypt);
        }

        private static Bitmap ShuffleBlocks(Bitmap bmp, int block, int seed, bool encrypt)
        {
            int w = bmp.Width, h = bmp.Height;
            int bx = (w + block - 1) / block;
            int by = (h + block - 1) / block;
            int n = bx * by;
            var perm = new int[n];
            for (int i = 0; i < n; i++) perm[i] = i;
            var rng = new Random(seed);
            for (int i = n - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }
            int[] map;
            if (encrypt) map = perm;
            else
            {
                var inv = new int[n];
                for (int i = 0; i < n; i++) inv[perm[i]] = i;
                map = inv;
            }

            var rect = new Rectangle(0, 0, w, h);
            var srcData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var outBmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var dstData = outBmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                int srcStride = srcData.Stride;
                int dstStride = dstData.Stride;
                int len = Math.Abs(srcStride) * h;
                var srcBuf = new byte[len];
                System.Runtime.InteropServices.Marshal.Copy(srcData.Scan0, srcBuf, 0, len);
                var dstBuf = new byte[len];

                for (int yb = 0; yb < by; yb++)
                for (int xb = 0; xb < bx; xb++)
                {
                    int idx = yb * bx + xb;
                    int srcIdx = map[idx];
                    int srcXb = srcIdx % bx;
                    int srcYb = srcIdx / bx;

                    int x0 = xb * block;
                    int y0 = yb * block;
                    int sx0 = srcXb * block;
                    int sy0 = srcYb * block;
                    int bw = Math.Min(block, w - x0);
                    int bh = Math.Min(block, h - y0);

                    for (int dy = 0; dy < bh; dy++)
                    {
                        int srcOff = (sy0 + dy) * srcStride + sx0 * 4;
                        int dstOff = (y0 + dy) * dstStride + x0 * 4;
                        Buffer.BlockCopy(srcBuf, srcOff, dstBuf, dstOff, bw * 4);
                    }
                }

                System.Runtime.InteropServices.Marshal.Copy(dstBuf, 0, dstData.Scan0, dstBuf.Length);
            }
            finally
            {
                bmp.UnlockBits(srcData);
                outBmp.UnlockBits(dstData);
            }
            bmp.Dispose();
            return outBmp;
        }

        private static Bitmap ApplyArnoldCat(Bitmap bmp, ImageEffectOptions options, string? password, bool encrypt)
        {
            if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("missing password");
            if (bmp.Width != bmp.Height)
                throw new InvalidOperationException("Arnold 仅支持正方形图片。");
            return ArnoldScramble(bmp, encrypt, options.ArnoldIterations);
        }

        private static Bitmap ArnoldScramble(Bitmap bmp, bool encrypt, int iterations)
        {
            int N = bmp.Width;
            if (N != bmp.Height) return bmp;
            var outBmp = new Bitmap(N, N);
            for (int y = 0; y < N; y++)
            for (int x = 0; x < N; x++)
            {
                int nx = x, ny = y;
                for (int k = 0; k < iterations; k++)
                {
                    if (encrypt)
                    {
                        int tx = (nx + ny) % N;
                        int ty = (nx + 2 * ny) % N;
                        nx = tx; ny = ty;
                    }
                    else
                    {
                        int tx = (2 * nx - ny + N * 2) % N;
                        int ty = (-nx + ny + N * 2) % N;
                        nx = tx; ny = ty;
                    }
                }
                outBmp.SetPixel(nx, ny, bmp.GetPixel(x, y));
            }
            bmp.Dispose();
            return outBmp;
        }

        private void SaveCurrentOutput(bool saveDecrypted)
        {
            var tab = _sheetTabs.SelectedTab;
            if (tab == null) return;

            if (tab.Tag is not ImageSheetState state)
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 请先选择一张图片。");
                return;
            }

            var rightBox = GetRightPictureBox(tab);
            if (rightBox?.Image == null)
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 右侧没有可保存的图像。");
                return;
            }

            if (!saveDecrypted)
            {
                if (state.EncryptedImage == null)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 未生成加密图，请先点击「加密」。");
                    return;
                }
                if (state.Options == null)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 缺少加密参数，无法保存可解密的加密图。");
                    return;
                }
            }

            using var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png",
                FileName = saveDecrypted ? "decrypted.png" : "encrypted.png"
            };
            if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;
            var outPath = dlg.FileName;

            if (saveDecrypted)
            {
                rightBox.Image.Save(outPath, ImageFormat.Png);
                _log($"[{DateTime.Now:HH:mm:ss}] 已保存解密图: {outPath}");
                return;
            }

            state.EncryptedImage!.Save(outPath, ImageFormat.Png);
            var metaPath = GetMetaPathForImage(outPath);
            try
            {
                var json = JsonSerializer.Serialize(state.Options!, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(metaPath, json, Encoding.UTF8);
                _log($"[{DateTime.Now:HH:mm:ss}] 已保存加密图: {outPath}");
                _log($"[{DateTime.Now:HH:mm:ss}] 已写入元数据: {metaPath}");
            }
            catch (Exception ex)
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 写入元数据失败: {ex.Message}");
            }
        }

        private static string GetMetaPathForImage(string imagePath) => imagePath + ".encmeta.json";

        private static ImageEffectOptions? TryLoadMeta(string metaPath)
        {
            try
            {
                if (!File.Exists(metaPath)) return null;
                var json = File.ReadAllText(metaPath, Encoding.UTF8);
                return JsonSerializer.Deserialize<ImageEffectOptions>(json);
            }
            catch { return null; }
        }

        private void SaveBatchOutput()
        {
            if (_sheetTabs.TabPages.Count == 0) return;

            bool saveDecrypted = _lastActionWasDecrypt;
            int total = _sheetTabs.TabPages.Count;
            for (int i = 0; i < total; i++)
            {
                var tab = _sheetTabs.TabPages[i];
                if (tab.Tag is not ImageSheetState state)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 跳过保存(无状态): {tab.Text}");
                    continue;
                }
                var srcPath = state.Path;
                if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath))
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 跳过保存(路径无效): {tab.Text}");
                    continue;
                }
                var outDir = Path.Combine(Path.GetDirectoryName(srcPath) ?? srcPath, "output");
                Directory.CreateDirectory(outDir);

                var baseName = Path.GetFileNameWithoutExtension(srcPath);
                string outPath = Path.Combine(outDir, baseName + (saveDecrypted ? "_decrypted.png" : "_encrypted.png"));

                try
                {
                    if (saveDecrypted)
                    {
                        var rightBox = GetRightPictureBox(tab);
                        if (rightBox?.Image == null)
                        {
                            _log($"[{DateTime.Now:HH:mm:ss}] 跳过保存(无解密预览): {Path.GetFileName(srcPath)}");
                            continue;
                        }
                        rightBox.Image.Save(outPath, ImageFormat.Png);
                        _log($"[{DateTime.Now:HH:mm:ss}] 已保存: {outPath}");
                    }
                    else
                    {
                        if (state.EncryptedImage == null || state.Options == null)
                        {
                            _log($"[{DateTime.Now:HH:mm:ss}] 跳过保存(未加密或缺参数): {Path.GetFileName(srcPath)}");
                            continue;
                        }
                        state.EncryptedImage.Save(outPath, ImageFormat.Png);
                        var metaPath = GetMetaPathForImage(outPath);
                        var json = JsonSerializer.Serialize(state.Options, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metaPath, json, Encoding.UTF8);
                        _log($"[{DateTime.Now:HH:mm:ss}] 已保存: {outPath}");
                        _log($"[{DateTime.Now:HH:mm:ss}] 已写入元数据: {metaPath}");
                    }
                }
                catch (Exception ex)
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 保存失败: {Path.GetFileName(srcPath)} - {ex.Message}");
                }
            }
            _log($"[{DateTime.Now:HH:mm:ss}] 批量保存完成。");
        }
    }
}
