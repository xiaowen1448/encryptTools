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
        private const int IconThumbSize = 16;
        private const int IconRowHeight = 20;
        // 下拉时默认展示的行数（>=10），其余由列表滚动
        private const int IconVisibleItems = 12;
        private const int IconDropdownMaxWidth = 220;
        private readonly Dictionary<string, Image> _iconThumbCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        private readonly Action<string> _log;
        private readonly Action<double>? _reportProgress;
        private readonly List<string> _imagePaths = new List<string>();
        private TabControl _sheetTabs = null!;
        private ComboBox _cbMode = null!;
        private ComboBox _cbBlock = null!;
        private ComboBox _cbPwdFiles = null!;
        private string? _passwordFilePath;
        private CheckBox _chkPixelation = null!;
        private CheckBox _chkIconOverlay = null!;
        private CheckBox _chkIconRandomize = null!;
        private ComboBox _cbIcons = null!;
        private ComboBox _cbIconBlock = null!;
        private NumericUpDown _numOverlayOpacity = null!;
        private List<string> _customIconPaths = new List<string>();
        private bool _lastActionWasDecrypt;
        private ToolTip? _toolTip;

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
            var lblHint = new Label { Text = "支持拖拽", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 16, 0) };
            _toolTip = new ToolTip { AutoPopDelay = 8000 };
            const int comboMaxW = 140;
            _cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 4, 8, 4), MinimumSize = new Size(100, 0), MaximumSize = new Size(comboMaxW, 0), Width = 120 };
            _cbMode.Items.AddRange(new object[] { "不可逆马赛克(仅效果)", "密钥置乱(可逆)", "像素XOR(可逆)", "分块置乱(可逆)" });
            _cbMode.SelectedIndex = 1;
            _cbMode.DropDown += (_, __) => SetComboDropDownWidth(_cbMode, comboMaxW);
            SetComboDropDownWidth(_cbMode, comboMaxW);
            _chkPixelation = new CheckBox { Text = "像素化", AutoSize = true, Margin = new Padding(4, 6, 8, 0), Checked = false };
            _cbBlock = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 4, 8, 4), MinimumSize = new Size(56, 0), MaximumSize = new Size(comboMaxW, 0), Width = 72 };
            _cbBlock.Items.AddRange(new object[] { "4×4", "8×8", "16×16", "24×24", "32×32", "48×48", "64×64" });
            _cbBlock.SelectedIndex = 2;
            _cbBlock.DropDown += (_, __) => SetComboDropDownWidth(_cbBlock, comboMaxW);
            SetComboDropDownWidth(_cbBlock, comboMaxW);
            var btnEncrypt = new Button { Text = "执行加密",  AutoSize = true, Margin = new Padding(8, 0, 4, 4) };
            var btnDecrypt = new Button { Text = "执行解密",  AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnSave = new Button { Text = "保存输出", AutoSize = true, Margin = new Padding(4, 0, 4, 4) };

            _cbPwdFiles = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 4, 8, 4), MinimumSize = new Size(100, 0), MaximumSize = new Size(comboMaxW, 0), Width = 120 };
            _cbPwdFiles.DropDown += (_, __) => SetComboDropDownWidth(_cbPwdFiles, comboMaxW);
            _chkIconOverlay = new CheckBox { Text = "图标覆盖", AutoSize = true, Margin = new Padding(4, 6, 4, 0), Checked = true };
            _cbIcons = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 4, 0), MinimumSize = new Size(100, 0), MaximumSize = new Size(comboMaxW, 0), Width = 120 };
            // 需要在下拉项里同时显示“文件名 + 缩略图”，因此启用 OwnerDraw 并限制下拉高度以保证可滚动
            _cbIcons.DrawMode = DrawMode.OwnerDrawFixed;
            _cbIcons.ItemHeight = IconRowHeight;
            // 保证下拉列表与行高一致，不出现裁剪/风格不一致
            _cbIcons.IntegralHeight = true;
            _cbIcons.MaxDropDownItems = IconVisibleItems;
            _cbIcons.DropDownHeight = IconRowHeight * IconVisibleItems + 2;
            // 下拉宽度固定为控件宽度，避免长文件名把下拉窗体撑宽
            _cbIcons.DropDown += (_, __) => _cbIcons.DropDownWidth = _cbIcons.Width;

            _cbIcons.DrawItem += (_, e) =>
            {
                try
                {
                    if (e.Index < 0 || e.Index >= _cbIcons.Items.Count) return;
                    var itemText = _cbIcons.Items[e.Index]?.ToString() ?? "";
                    e.DrawBackground();

                    var bounds = e.Bounds;
                    // 未选择项不显示缩略图
                    if (!string.IsNullOrWhiteSpace(itemText) && string.Equals(itemText, "(未选择)", StringComparison.OrdinalIgnoreCase))
                    {
                        TextRenderer.DrawText(e.Graphics, itemText, e.Font, bounds, e.ForeColor);
                        return;
                    }

                    // 缩略图位于左侧
                    var full = Path.Combine(GetIcoDirectory(), itemText);
                    if (!string.IsNullOrWhiteSpace(itemText) && File.Exists(full))
                    {
                        var thumb = GetOrLoadIconThumb(full);
                        if (thumb != null)
                        {
                            var th = IconThumbSize;
                            var x = bounds.Left + 4;
                            var y = bounds.Top + (bounds.Height - th) / 2;
                            e.Graphics.DrawImage(thumb, new Rectangle(x, y, th, th));
                            var left = bounds.Left + th + 10;
                            var width = Math.Max(10, bounds.Width - (th + 10));
                            var textRect = new Rectangle(left, bounds.Top, width, bounds.Height);
                            TextRenderer.DrawText(e.Graphics, itemText, e.Font, textRect, e.ForeColor,
                                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                            return;
                        }
                    }

                    // 兜底：只画文字
                    TextRenderer.DrawText(e.Graphics, itemText, e.Font, bounds, e.ForeColor,
                        TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                catch
                {
                    // 忽略绘制异常，避免卡死 UI
                }
            };
            _numOverlayOpacity = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 80, Width = 44, Margin = new Padding(2, 4, 4, 4) };
            _cbIconBlock = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(4, 4, 8, 4), MinimumSize = new Size(56, 0), MaximumSize = new Size(comboMaxW, 0), Width = 72 };
            _cbIconBlock.Items.AddRange(new object[] { "8×8", "16×16", "24×24", "32×32", "48×48", "64×64", "96×96", "128×128" });
            _cbIconBlock.SelectedIndex = 3; // 32×32
            _cbIconBlock.DropDown += (_, __) => SetComboDropDownWidth(_cbIconBlock, comboMaxW);
            SetComboDropDownWidth(_cbIconBlock, comboMaxW);
            _chkIconRandomize = new CheckBox { Text = "图标无序化", AutoSize = true, Margin = new Padding(4, 6, 8, 0), Checked = true };

            toolbar.Controls.Add(btnSelect);
            toolbar.Controls.Add(lblHint);
            toolbar.Controls.Add(_chkPixelation);
            toolbar.Controls.Add(_cbMode);
            toolbar.Controls.Add(new Label { Text = "密码文件:", AutoSize = true, Margin = new Padding(8, 8, 4, 0) });
            toolbar.Controls.Add(_cbPwdFiles);
            toolbar.Controls.Add(new Label { Text = "块:", AutoSize = true, Margin = new Padding(4, 8, 4, 0) });
            toolbar.Controls.Add(_cbBlock);
            toolbar.Controls.Add(_chkIconOverlay);
            // 图标下拉 + 导入按钮：放入同一行容器，保证高度/基线一致
            var btnImportIcons = new Button { Text = "导入图标", AutoSize = false, Margin = new Padding(0) };
            // 让“导入图标”与“加密(批量)”按钮长宽一致
            var encSize = btnEncrypt.GetPreferredSize(Size.Empty);
            btnImportIcons.Size = encSize;
            var iconRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                Margin = new Padding(4, 4, 4, 4),
                Padding = new Padding(0),
                FlowDirection = FlowDirection.LeftToRight
            };
            // 图标覆盖选择框后：导入图标按钮 -> 图标下拉 -> 图标预览
            iconRow.Controls.Add(btnImportIcons);
            _cbIcons.Width = btnImportIcons.Width;
            _cbIcons.DropDownWidth = _cbIcons.Width;
            btnImportIcons.Height = _cbIcons.Height;
            iconRow.Controls.Add(_cbIcons);
            toolbar.Controls.Add(iconRow);
            toolbar.Controls.Add(new Label { Text = "透明度%:", AutoSize = true, Margin = new Padding(4, 8, 2, 0) });
            toolbar.Controls.Add(_numOverlayOpacity);
            toolbar.Controls.Add(new Label { Text = "图标块:", AutoSize = true, Margin = new Padding(4, 8, 2, 0) });
            toolbar.Controls.Add(_cbIconBlock);
            toolbar.Controls.Add(_chkIconRandomize);
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
                    Filter = "图片|*.png;*.jpg;*.jpeg;*.jfif;*.jpe;*.bmp;*.gif|所有文件|*.*",
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

            btnImportIcons.Click += (_, __) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Filter = "图片|*.png;*.jpg;*.jpeg;*.jfif;*.jpe;*.bmp;*.gif;*.ico|所有文件|*.*",
                    Multiselect = true
                };
                if (dlg.ShowDialog(FindForm()) == DialogResult.OK && dlg.FileNames.Length > 0)
                {
                    string? firstImportedName = null;
                    int copied = 0;
                    foreach (var f in dlg.FileNames)
                    {
                        try
                        {
                            var name = Path.GetFileName(f);
                            var dst = Path.Combine(GetIcoDirectory(), name);
                            Directory.CreateDirectory(GetIcoDirectory());
                            File.Copy(f, dst, overwrite: true);
                            if (firstImportedName == null) firstImportedName = name;
                            copied++;
                        }
                        catch { }
                    }
                    RefreshIconsCombo(selectFileName: firstImportedName);
                    _log($"[{DateTime.Now:HH:mm:ss}] 已导入 {copied} 个图标到 ico 目录。");
                }
            };

            _cbIcons.SelectedIndexChanged += (_, __) =>
            {
                if (_cbIcons.SelectedItem is string name && !string.IsNullOrWhiteSpace(name) && name != "(未选择)")
                {
                    var full = Path.Combine(GetIcoDirectory(), name);
                    _customIconPaths = File.Exists(full) ? new List<string> { full } : new List<string>();
                }
                else
                {
                    _customIconPaths = new List<string>();
                }
            };

            _cbMode.SelectedIndexChanged += (_, __) => UpdateModeUiState(btnDecrypt);
            _chkPixelation.CheckedChanged += (_, __) =>
            {
                _cbMode.Enabled = _chkPixelation.Checked;
                _cbBlock.Enabled = _chkPixelation.Checked;
                UpdateModeUiState(btnDecrypt);
            };
            _chkIconOverlay.CheckedChanged += (_, __) => UpdateModeUiState(btnDecrypt);
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
            void UpdateComboTooltip(ComboBox cb)
            {
                if (_toolTip == null) return;
                var t = cb.SelectedItem is PasswordFileItem p ? p.DisplayName : cb.SelectedItem?.ToString() ?? "";
                _toolTip.SetToolTip(cb, t);
            }
            _cbMode.SelectedIndexChanged += (_, __) => UpdateComboTooltip(_cbMode);
            _cbBlock.SelectedIndexChanged += (_, __) => UpdateComboTooltip(_cbBlock);
            _cbPwdFiles.SelectedIndexChanged += (_, __) => UpdateComboTooltip(_cbPwdFiles);
            _cbIcons.SelectedIndexChanged += (_, __) => UpdateComboTooltip(_cbIcons);
            RefreshPasswordFiles();
            RefreshIconsCombo();
            UpdateComboTooltip(_cbMode);
            UpdateComboTooltip(_cbBlock);
            UpdateComboTooltip(_cbPwdFiles);
            UpdateComboTooltip(_cbIcons);

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

        private static void SetPreviewImagePreserveZoom(PictureBox box, Bitmap newImage)
        {
            if (box == null || box.IsDisposed || newImage == null) return;
            var old = box.Tag as ZoomState;
            float zoom = old?.Zoom ?? 1f;
            var initial = old?.InitialDisplaySize ?? FitThumbnailSize(newImage.Size, 480, 360);

            box.Image?.Dispose();
            box.Image = newImage;
            box.Tag = new ZoomState { BaseSize = newImage.Size, InitialDisplaySize = initial, Zoom = zoom };
            int w = (int)(initial.Width * zoom);
            int h = (int)(initial.Height * zoom);
            box.Size = new Size(Math.Max(1, w), Math.Max(1, h));
        }

        private async Task RunEncryptAsync()
        {
            if (_sheetTabs.TabPages.Count == 0) return;
            if (string.IsNullOrWhiteSpace(_passwordFilePath) || !File.Exists(_passwordFilePath))
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 请先选择密码文件(.pwd)后再加密。");
                return;
            }
            var password = TryLoadPasswordFromPwdFile();
            if (string.IsNullOrEmpty(password)) return;
            _lastActionWasDecrypt = false;
            _reportProgress?.Invoke(0);
            var progress = new Progress<int>(p => { _reportProgress?.Invoke(p / 100.0); });
            try
            {
                var modeNow = (ImageMode)Math.Max(0, _cbMode.SelectedIndex);

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
                    Bitmap? coreEncrypted = null;
                    await Task.Run(() =>
                    {
                        using var orig = Image.FromFile(path);
                        processed = ApplyPixelEffect(orig, options, password, encrypt: true);
                        ((IProgress<int>)progress).Report((i + 1) * 100 / total);
                    }).ConfigureAwait(true);
                    if (processed != null)
                    {
                        if (options.IconOverlayEnabled && _chkIconOverlay.Checked)
                        {
                            ApplyIconOverlay(processed, options, new List<string>(_customIconPaths), out var blockData, out var overlayBlockSize);
                            if (blockData != null && blockData.Length > 0 && !string.IsNullOrEmpty(password))
                            {
                                var encrypted = EncryptBlockData(password, options.SaltBase64 ?? "", blockData);
                                if (encrypted != null)
                                {
                                    options.IconOverlayBlocksEncryptedBase64 = Convert.ToBase64String(encrypted);
                                    options.IconOverlayBlockSize = overlayBlockSize;
                                }
                            }
                        }
                        // 保存带遮挡的图，解密时用密码恢复块再反向解密
                        coreEncrypted = null;
                    }
                    if (processed != null && !rightBox.IsDisposed)
                    {
                        SetPreviewImagePreserveZoom(rightBox, processed);
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

        private static string GetIcoDirectory()
        {
            try
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ico");
            }
            catch
            {
                return Path.Combine(Environment.CurrentDirectory, "ico");
            }
        }

        private void RefreshIconsCombo(string? selectFileName = null)
        {
            try
            {
                var dir = GetIcoDirectory();
                Directory.CreateDirectory(dir);
                var files = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => new[] { ".png", ".jpg", ".jpeg", ".jfif", ".jpe", ".bmp", ".gif", ".ico" }.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                // 刷新图标列表时清理缩略图缓存，避免缓存爆内存/旧图占用文件句柄
                ClearIconThumbCache();

                _cbIcons.BeginUpdate();
                _cbIcons.Items.Clear();
                _cbIcons.Items.Add("(未选择)");
                foreach (var n in files) _cbIcons.Items.Add(n!);
                _cbIcons.EndUpdate();

                if (!string.IsNullOrWhiteSpace(selectFileName))
                {
                    for (int i = 0; i < _cbIcons.Items.Count; i++)
                    {
                        if (string.Equals(_cbIcons.Items[i]?.ToString(), selectFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            _cbIcons.SelectedIndex = i;
                            break;
                        }
                    }
                }
                if (_cbIcons.SelectedIndex < 0) _cbIcons.SelectedIndex = 0;
                // 下拉宽度固定，避免文件名过长导致下拉窗体过宽
                _cbIcons.DropDownWidth = _cbIcons.Width;
            }
            catch { }
        }

        private void ClearIconThumbCache()
        {
            try
            {
                foreach (var kv in _iconThumbCache)
                {
                    try { kv.Value.Dispose(); } catch { }
                }
            }
            catch { }
            _iconThumbCache.Clear();
        }

        private Image? GetOrLoadIconThumb(string fullIcoPath)
        {
            if (string.IsNullOrWhiteSpace(fullIcoPath)) return null;
            try
            {
                if (_iconThumbCache.TryGetValue(fullIcoPath, out var cached) && cached != null)
                    return cached;

                if (!File.Exists(fullIcoPath)) return null;

                // 用流读取避免 FromFile 锁定原文件句柄；再克隆成可独立释放的 Bitmap
                using (var fs = new FileStream(fullIcoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var img = Image.FromStream(fs))
                using (var bmp = new Bitmap(IconThumbSize, IconThumbSize))
                {
                    using (var g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Color.Transparent);
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

                        var src = img.Size;
                        if (src.Width <= 0 || src.Height <= 0) return null;
                        float r = Math.Min((float)IconThumbSize / src.Width, (float)IconThumbSize / src.Height);
                        int w = Math.Max(1, (int)(src.Width * r));
                        int h = Math.Max(1, (int)(src.Height * r));
                        int x = (IconThumbSize - w) / 2;
                        int y = (IconThumbSize - h) / 2;
                        g.DrawImage(img, x, y, w, h);
                    }

                    // 缓存一份，后续 DrawItem 复用
                    var cloned = new Bitmap(bmp);
                    _iconThumbCache[fullIcoPath] = cloned;
                    return cloned;
                }
            }
            catch
            {
                return null;
            }
        }

        private async Task RunDecryptAsync()
        {
            if (_sheetTabs.TabPages.Count == 0) return;
            if (string.IsNullOrWhiteSpace(_passwordFilePath) || !File.Exists(_passwordFilePath))
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 请先选择密码文件(.pwd)后再解密。");
                return;
            }
            var password = TryLoadPasswordFromPwdFile();
            if (string.IsNullOrEmpty(password)) return;
            _lastActionWasDecrypt = true;

            // 当前所选密码文件名（仅文件名部分），用于与元数据中的 PasswordFileName 比较
            string? currentPwdFileName = null;
            try
            {
                if (!string.IsNullOrWhiteSpace(_passwordFilePath))
                    currentPwdFileName = Path.GetFileName(_passwordFilePath);
            }
            catch { }

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
                // 若为新格式（包含 PasswordFileName），但当前选择的密码文件与加密时记录的不一致，则直接提示并跳过解密
                if (!string.IsNullOrWhiteSpace(state.Options.PasswordFileName) &&
                    !string.IsNullOrWhiteSpace(currentPwdFileName) &&
                    !string.Equals(state.Options.PasswordFileName, currentPwdFileName, StringComparison.OrdinalIgnoreCase))
                {
                    _log($"[{DateTime.Now:HH:mm:ss}] 密码文件错误: {Path.GetFileName(state.Path)}，请选用加密时使用的密码文件。");
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
                    Bitmap? toDecrypt = state.EncryptedImage;
                    if (toDecrypt != null && state.Options != null &&
                        !string.IsNullOrEmpty(state.Options.IconOverlayBlocksEncryptedBase64) &&
                        state.Options.IconOverlayBlockSize >= 4)
                    {
                        var enc = Convert.FromBase64String(state.Options.IconOverlayBlocksEncryptedBase64);
                        var blockData = DecryptBlockData(password, state.Options.SaltBase64 ?? "", enc);
                        if (blockData != null && blockData.Length > 0)
                        {
                            var restored = (Bitmap)state.EncryptedImage.Clone();
                            if (RestoreIconOverlayBlocks(restored, blockData, state.Options.IconOverlayBlockSize))
                                toDecrypt = restored;
                            else
                                restored?.Dispose();
                        }
                    }
                    Bitmap? decrypted = null;
                    await Task.Run(() =>
                    {
                        decrypted = ApplyPixelEffect(toDecrypt!, state.Options!, password, encrypt: false);
                        if (toDecrypt != null && toDecrypt != state.EncryptedImage)
                            toDecrypt.Dispose();
                    }).ConfigureAwait(true);
                    if (decrypted != null && !rightBox.IsDisposed)
                    {
                        SetPreviewImagePreserveZoom(rightBox, decrypted);
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

        /// <summary>预览区缩放状态：1:1 为首次打开时的显示尺寸，Zoom 为相对该尺寸的缩放。</summary>
        private sealed class ZoomState
        {
            public Size BaseSize;
            /// <summary>首次打开时的显示尺寸（如 FitThumbnailSize 结果），1:1 恢复为此尺寸。</summary>
            public Size InitialDisplaySize;
            public float Zoom = 1f;
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
            public string SaltBase64 { get; set; } = "";
            /// <summary>加密时使用的密码文件名（例如 xxxx.pwd），用于解密时校验是否选对密码文件。</summary>
            public string? PasswordFileName { get; set; }
            public bool PixelationEnabled { get; set; }
            public bool IconOverlayEnabled { get; set; }
            public int OverlayOpacityPercent { get; set; } = 80;
            /// <summary>图标覆盖块大小（像素）。常用：8/16/24/32/48/64/96/128。</summary>
            public int IconOverlayBlockSizeHint { get; set; } = 32;
            /// <summary>图标遮挡块的原始像素（用密码加密后 Base64），解密时用密码恢复再还原原图。</summary>
            public string? IconOverlayBlocksEncryptedBase64 { get; set; }
            /// <summary>遮挡使用的块尺寸，与 IconOverlayBlocksEncryptedBase64 配套。</summary>
            public int IconOverlayBlockSize { get; set; }
            /// <summary>是否启用图标无序化（随机旋转、随机偏移、杂乱覆盖）。</summary>
            public bool IconRandomize { get; set; }
        }

        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".jfif", ".jpe", ".bmp", ".gif" };

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
                if (leftBox.Image != null)
                {
                    var sz = FitThumbnailSize(leftBox.Image.Size, 480, 360);
                    leftBox.Tag = new ZoomState { BaseSize = leftBox.Image.Size, InitialDisplaySize = sz, Zoom = 1f };
                    leftBox.Size = sz;
                }
            }
            catch { }

            split.Panel1.Controls.Add(leftPanel);
            split.Panel2.Controls.Add(rightPanel);
            tab.Controls.Add(split);
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
                SetPreviewImagePreserveZoom(rightBox, processed);
                tab.Tag = new ImageSheetState { Path = imagePath, EncryptedImage = (Bitmap)processed.Clone(), Options = options };
            }
        }

        private static Panel CreatePreviewPanel(out PictureBox picBox)
        {
            var outer = new Panel { Dock = DockStyle.Fill };
            var toolBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 28, FlowDirection = FlowDirection.LeftToRight, BackColor = SystemColors.ControlDark };
            var btnZoom100 = new Button { Text = "1:1", AutoSize = true, Margin = new Padding(2) };
            var btnZoomIn = new Button { Text = "+", AutoSize = true, Margin = new Padding(2), Width = 28 };
            var btnZoomOut = new Button { Text = "-", AutoSize = true, Margin = new Padding(2), Width = 28 };
            toolBar.Controls.Add(btnZoom100);
            toolBar.Controls.Add(btnZoomIn);
            toolBar.Controls.Add(btnZoomOut);

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            var pb = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(0, 0), BorderStyle = BorderStyle.FixedSingle };
            scroll.Controls.Add(pb);
            outer.Controls.Add(scroll);
            outer.Controls.Add(toolBar);

            ZoomState GetState()
            {
                var s = pb.Tag as ZoomState;
                if (s != null) return s;
                var imgSize = pb.Image?.Size ?? Size.Empty;
                s = new ZoomState { BaseSize = imgSize, InitialDisplaySize = imgSize, Zoom = 1f };
                pb.Tag = s;
                return s;
            }
            void ApplyZoom()
            {
                if (pb.Image == null) return;
                var st = GetState();
                if (st.InitialDisplaySize.Width <= 0 || st.InitialDisplaySize.Height <= 0)
                    st.InitialDisplaySize = FitThumbnailSize(pb.Image.Size, 480, 360);
                int w = (int)(st.InitialDisplaySize.Width * st.Zoom);
                int h = (int)(st.InitialDisplaySize.Height * st.Zoom);
                pb.Size = new Size(Math.Max(1, w), Math.Max(1, h));
            }
            // 1:1 = 恢复为首次打开时的显示尺寸，不放大
            btnZoom100.Click += (_, __) =>
            {
                if (pb.Image == null) return;
                var st = GetState();
                st.BaseSize = pb.Image.Size;
                if (st.InitialDisplaySize.Width <= 0 || st.InitialDisplaySize.Height <= 0)
                    st.InitialDisplaySize = FitThumbnailSize(pb.Image.Size, 480, 360);
                st.Zoom = 1f;
                pb.Size = st.InitialDisplaySize;
            };
            btnZoomIn.Click += (_, __) =>
            {
                if (pb.Image == null) return;
                var st = GetState();
                if (st.InitialDisplaySize.Width <= 0 || st.InitialDisplaySize.Height <= 0)
                    st.InitialDisplaySize = FitThumbnailSize(pb.Image.Size, 480, 360);
                st.Zoom = Math.Min(20f, st.Zoom * 1.1f);
                ApplyZoom();
            };
            btnZoomOut.Click += (_, __) =>
            {
                if (pb.Image == null) return;
                var st = GetState();
                if (st.InitialDisplaySize.Width <= 0 || st.InitialDisplaySize.Height <= 0)
                    st.InitialDisplaySize = FitThumbnailSize(pb.Image.Size, 480, 360);
                st.Zoom = Math.Max(0.05f, st.Zoom / 1.1f);
                ApplyZoom();
            };

            scroll.MouseWheel += (_, e) =>
            {
                if ((Control.ModifierKeys & Keys.Control) == 0) return;
                if (pb.Image == null) return;
                var st = GetState();
                if (st.InitialDisplaySize.Width <= 0 || st.InitialDisplaySize.Height <= 0)
                    st.InitialDisplaySize = FitThumbnailSize(pb.Image.Size, 480, 360);
                st.Zoom = e.Delta > 0 ? st.Zoom * 1.1f : st.Zoom / 1.1f;
                st.Zoom = Math.Max(0.05f, Math.Min(20f, st.Zoom));
                ApplyZoom();
            };
            scroll.MouseEnter += (_, __) => scroll.Focus();
            scroll.TabStop = true;

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
            _cbMode.Enabled = _chkPixelation.Checked;
            _cbBlock.Enabled = _chkPixelation.Checked;
            bool needsPassword = mode is ImageMode.Permutation or ImageMode.XorStream or ImageMode.BlockShuffle or ImageMode.ArnoldCat;
            _cbPwdFiles.Enabled = true;
            _chkIconRandomize.Enabled = _chkIconOverlay.Checked;
            btnDecrypt.Enabled = _chkIconOverlay.Checked || (_chkPixelation.Checked && mode != ImageMode.Mosaic);
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

        private static void SetComboDropDownWidth(ComboBox cb, int maxWidth = 140)
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
                cb.DropDownWidth = Math.Min(Math.Max(maxW, 80), maxWidth);
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
            int block = _cbBlock.SelectedIndex switch { 0 => 4, 1 => 8, 2 => 16, 3 => 24, 4 => 32, 5 => 48, _ => 64 };
            var salt = new byte[16];
            EncryptTools.Compat.RngFill(salt);
            var opt = new ImageEffectOptions
            {
                Mode = (ImageMode)Math.Max(0, _cbMode.SelectedIndex),
                BlockSize = block,
                Iterations = 200_000,
                SaltBase64 = Convert.ToBase64String(salt),
                PixelationEnabled = _chkPixelation.Checked,
                IconOverlayEnabled = _chkIconOverlay.Checked,
                OverlayOpacityPercent = (int)_numOverlayOpacity.Value,
                IconOverlayBlockSizeHint = ParseBlockSize(_cbIconBlock?.SelectedItem?.ToString()) ?? 32,
                IconRandomize = _chkIconRandomize?.Checked ?? false
            };
            // 记录当前所选密码文件名（仅文件名），用于解密时校验是否选对密码文件。旧元数据无此字段则不强制。
            try
            {
                if (!string.IsNullOrWhiteSpace(_passwordFilePath))
                    opt.PasswordFileName = Path.GetFileName(_passwordFilePath);
            }
            catch { }
            return opt;
        }

        private static int? ParseBlockSize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            // 支持 "16×16" / "16x16"
            var parts = text.Replace('x', '×').Split('×');
            if (parts.Length < 1) return null;
            if (int.TryParse(parts[0].Trim(), out int n) && n > 0) return n;
            return null;
        }

        /// <summary>
        /// 块状图标覆盖：按块读取原像素并保存，再在每块上绘制图标，便于解密时用密码恢复原图。
        /// 在 UI 线程调用。originalBlocks 为遮挡前块内像素（ARGB 顺序），由调用方用密码加密后写入元数据。
        /// </summary>
        private void ApplyIconOverlay(Bitmap bmp, ImageEffectOptions options, List<string> iconPaths, out byte[]? originalBlocks, out int overlayBlockSize)
        {
            originalBlocks = null;
            overlayBlockSize = 0;
            if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0 || !options.IconOverlayEnabled) return;
            var icons = new List<Bitmap>();
            foreach (var p in iconPaths ?? _customIconPaths)
            {
                if (string.IsNullOrEmpty(p) || !File.Exists(p)) continue;
                try
                {
                    var img = Image.FromFile(p) as Bitmap;
                    if (img != null) icons.Add(img);
                }
                catch { }
            }
            if (icons.Count == 0) return;

            Bitmap? work = null;
            if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
            {
                work = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(work)) g.DrawImage(bmp, 0, 0);
            }
            var target = work ?? bmp;
            int w = target.Width, h = target.Height;
            // 图标覆盖块大小：由下拉框选定（常用 8/16/24/32/48/64/96/128），大小越大图标占比越大
            int block = Math.Max(4, options.IconOverlayBlockSizeHint);
            block = ((block + 3) / 4) * 4;
            block = Math.Min(block, Math.Min(w, h));
            int bx = (w + block - 1) / block;
            int by = (h + block - 1) / block;
            int totalBlocks = bx * by;
            overlayBlockSize = block;

            var blockBytes = new List<byte>();
            var rect = new Rectangle(0, 0, target.Width, target.Height);
            var bmpData = target.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bmpData.Stride;
                IntPtr scan0 = bmpData.Scan0;
                for (int idx = 0; idx < totalBlocks; idx++)
                {
                    int xb = idx % bx, yb = idx / bx;
                    int x0 = xb * block, y0 = yb * block;
                    int bw = Math.Min(block, target.Width - x0), bh = Math.Min(block, target.Height - y0);
                    if (bw <= 0 || bh <= 0) continue;
                    for (int dy = 0; dy < bh; dy++)
                    {
                        IntPtr row = IntPtr.Add(scan0, (y0 + dy) * stride + x0 * 4);
                        var line = new byte[bw * 4];
                        System.Runtime.InteropServices.Marshal.Copy(row, line, 0, line.Length);
                        blockBytes.AddRange(line);
                    }
                }
            }
            finally
            {
                target.UnlockBits(bmpData);
            }

            float alpha = Math.Max(0.01f, Math.Min(1f, options.OverlayOpacityPercent / 100f));
            using var ia = new ImageAttributes();
            var cm = new ColorMatrix { Matrix00 = 1f, Matrix11 = 1f, Matrix22 = 1f, Matrix33 = alpha, Matrix44 = 1f };
            ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
            var rnd = new Random(unchecked(Environment.TickCount * 397) ^ w ^ (h << 16));
            bool randomize = options.IconRandomize;
            using (var g = Graphics.FromImage(target))
            {
                if (randomize)
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    for (int idx = 0; idx < totalBlocks; idx++)
                    {
                        int xb = idx % bx, yb = idx / bx;
                        int x0 = xb * block, y0 = yb * block;
                        int bw = Math.Min(block, w - x0), bh = Math.Min(block, h - y0);
                        if (bw <= 0 || bh <= 0) continue;
                        var icon = icons[rnd.Next(icons.Count)];

                        float angle = (float)(rnd.NextDouble() * 360);
                        float offX = (float)(rnd.NextDouble() - 0.5) * block * 0.6f;
                        float offY = (float)(rnd.NextDouble() - 0.5) * block * 0.6f;
                        float scale = 0.8f + (float)(rnd.NextDouble() * 0.6);

                        var gs = g.Save();
                        float cx = x0 + bw / 2f + offX;
                        float cy = y0 + bh / 2f + offY;
                        g.TranslateTransform(cx, cy);
                        g.RotateTransform(angle);
                        int dw = Math.Max(1, (int)(bw * scale));
                        int dh = Math.Max(1, (int)(bh * scale));
                        g.DrawImage(icon, new Rectangle(-dw / 2, -dh / 2, dw, dh),
                                    0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                        g.Restore(gs);
                    }
                }
                else
                {
                    for (int idx = 0; idx < totalBlocks; idx++)
                    {
                        int xb = idx % bx, yb = idx / bx;
                        int x0 = xb * block, y0 = yb * block;
                        int bw = Math.Min(block, w - x0), bh = Math.Min(block, h - y0);
                        if (bw <= 0 || bh <= 0) continue;
                        var icon = icons[rnd.Next(icons.Count)];
                        g.DrawImage(icon, new Rectangle(x0, y0, bw, bh), 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
                    }
                }
            }
            if (work != null)
            {
                using (var g = Graphics.FromImage(bmp)) g.DrawImage(work, 0, 0);
                work.Dispose();
            }
            foreach (var icon in icons) { try { icon.Dispose(); } catch { } }
            originalBlocks = blockBytes.ToArray();
        }

        private static byte[]? EncryptBlockData(string password, string saltBase64, byte[] data)
        {
            if (string.IsNullOrEmpty(password) || data == null || data.Length == 0) return null;
            try
            {
                var salt = Convert.FromBase64String(saltBase64 ?? "");
                if (salt.Length < 8) salt = Encoding.UTF8.GetBytes("IconOverlayBlocks");
#if NET46 || NET461
                var key = EncryptTools.Compat.DeriveKeyPbkdf2Sha256(password, salt, 10000, 32);
#else
                using var kdf = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
                var key = kdf.GetBytes(32);
#endif
                using var aes = Aes.Create();
                aes.Key = key;
                aes.GenerateIV();
                using var enc = aes.CreateEncryptor();
                var iv = aes.IV;
                var encrypted = enc.TransformFinalBlock(data, 0, data.Length);
                var result = new byte[iv.Length + encrypted.Length];
                Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                Buffer.BlockCopy(encrypted, 0, result, iv.Length, encrypted.Length);
                return result;
            }
            catch { return null; }
        }

        private static byte[]? DecryptBlockData(string password, string saltBase64, byte[] encryptedWithIv)
        {
            if (string.IsNullOrEmpty(password) || encryptedWithIv == null || encryptedWithIv.Length < 16) return null;
            try
            {
                var salt = Convert.FromBase64String(saltBase64 ?? "");
                if (salt.Length < 8) salt = Encoding.UTF8.GetBytes("IconOverlayBlocks");
#if NET46 || NET461
                var key = EncryptTools.Compat.DeriveKeyPbkdf2Sha256(password, salt, 10000, 32);
#else
                using var kdf = new Rfc2898DeriveBytes(password, salt, 10000, HashAlgorithmName.SHA256);
                var key = kdf.GetBytes(32);
#endif
                using var aes = Aes.Create();
                aes.Key = key;
                var iv = new byte[16];
                Buffer.BlockCopy(encryptedWithIv, 0, iv, 0, 16);
                aes.IV = iv;
                using var dec = aes.CreateDecryptor();
                return dec.TransformFinalBlock(encryptedWithIv, 16, encryptedWithIv.Length - 16);
            }
            catch { return null; }
        }

        /// <summary>用密码解密得到的块数据写回位图，恢复遮挡前的加密图，便于再做像素解密。</summary>
        private static bool RestoreIconOverlayBlocks(Bitmap bmp, byte[] blockData, int blockSize)
        {
            if (bmp == null || blockData == null || blockSize < 4) return false;
            int w = bmp.Width, h = bmp.Height;
            int bx = (w + blockSize - 1) / blockSize;
            int by = (h + blockSize - 1) / blockSize;
            if (bmp.PixelFormat != PixelFormat.Format32bppArgb)
                return false;
            var rect = new Rectangle(0, 0, w, h);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int stride = bmpData.Stride;
                IntPtr scan0 = bmpData.Scan0;
                int offset = 0;
                for (int idx = 0; idx < bx * by && offset < blockData.Length; idx++)
                {
                    int xb = idx % bx, yb = idx / bx;
                    int x0 = xb * blockSize, y0 = yb * blockSize;
                    int bw = Math.Min(blockSize, w - x0), bh = Math.Min(blockSize, h - y0);
                    int need = bw * bh * 4;
                    if (offset + need > blockData.Length) break;
                    for (int dy = 0; dy < bh; dy++)
                    {
                        IntPtr row = IntPtr.Add(scan0, (y0 + dy) * stride + x0 * 4);
                        System.Runtime.InteropServices.Marshal.Copy(blockData, offset, row, bw * 4);
                        offset += bw * 4;
                    }
                }
                return true;
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }
        }

        private Bitmap ApplyPixelEffect(Image src, ImageEffectOptions options, string? password, bool encrypt)
        {
            var bmp = new Bitmap(src);
            // 未勾选「像素化」：不启用任何模式算法（只执行图标覆盖等其它功能）
            if (options.PixelationEnabled != true)
                return bmp;

            return options.Mode switch
            {
                ImageMode.Mosaic => ApplyMosaic(bmp, options.BlockSize),
                ImageMode.Permutation => ApplyPermutation(bmp, options, password, encrypt),
                ImageMode.XorStream => ApplyXorStream(bmp, options, password),
                ImageMode.BlockShuffle => ApplyBlockShuffle(bmp, options, password, encrypt),
                ImageMode.ArnoldCat => ApplyArnoldCat(bmp, options, password, encrypt),
                _ => bmp
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
#if NET46 || NET461
            return EncryptTools.Compat.DeriveKeyPbkdf2Sha256(password, salt, options.Iterations, keyLen);
#else
            using var kdf = new Rfc2898DeriveBytes(password, salt, options.Iterations, HashAlgorithmName.SHA256);
            return kdf.GetBytes(keyLen);
#endif
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
#if NET46 || NET48
                var counter = new byte[8];
#endif
                ulong ctr = 0;
                int offset = 0;
                while (offset < len)
                {
#if NET46 || NET48
                    var ctrBytes = BitConverter.GetBytes(ctr++);
                    for (int i = 0; i < 8; i++) counter[i] = ctrBytes[i];
                    var mac = hmac.ComputeHash(counter);
#else
                    Span<byte> counterSpan = stackalloc byte[8];
                    BitConverter.TryWriteBytes(counterSpan, ctr++);
                    var mac = hmac.ComputeHash(counterSpan.ToArray());
#endif
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
            return ArnoldScramble(bmp, encrypt, 10);
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
            if (string.IsNullOrWhiteSpace(_passwordFilePath) || !File.Exists(_passwordFilePath))
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 请先选择密码文件(.pwd)后再保存。");
                return;
            }

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
            if (string.IsNullOrWhiteSpace(_passwordFilePath) || !File.Exists(_passwordFilePath))
            {
                _log($"[{DateTime.Now:HH:mm:ss}] 请先选择密码文件(.pwd)后再保存。");
                return;
            }

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
