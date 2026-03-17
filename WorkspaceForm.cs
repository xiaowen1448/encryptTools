
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EncryptTools.Ui;
using EncryptTools.PasswordFile;

namespace EncryptTools
{
    /// <summary>
    /// 工作区主窗口：顶部菜单 + 中部工作区 Tab + 底部独立日志区。
    /// 仅做文件快速加密的基础结构壳。
    /// </summary>
    public sealed class WorkspaceForm : Form
    {
        private MenuStrip _menu = null!;
        private StatusStrip _status = null!;
        private ToolStripStatusLabel _statusLeft = null!;
        private ToolStripStatusLabel _statusRight = null!;

        private SplitContainer _vertSplit = null!;
        private TabControl _tabWorkspaces = null!;
        private Panel _logHost = null!;

        private ContextMenuStrip _tabContextMenu = null!;
        private TabPage? _tabContextTarget;

        private sealed class WorkspaceContext
        {
            public string Kind = "文件加密";
            public string? SourcePath;
            public TextBoxBase LogBox = null!;
            // 文件工作区控件引用（Kind=="文件"时使用）
            public ListView? FileListView;
            public CheckBox? ChkPackExe;
            public CheckBox? ChkOverwrite;
            public CheckBox? ChkRandomFileName;
            public TextBox? TxtPassword;
            public ComboBox? CbPwdFile;
            public ComboBox? CbAlgo;
            public ComboBox? CbSuffix;
            /// <summary>不勾选覆盖时，上次加密使用的输出目录（UUID_密码文件名_output），解密时从此目录读取。</summary>
            public string? LastNonInPlaceOutputRoot;
        }

        public WorkspaceForm()
        {
            InitializeComponent();
            try
            {
                Icon = LoadAppIcon() ?? Icon;
            }
            catch { }
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

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                bool dark = WindowsTheme.IsDarkMode();
                Backdrop.TryApplyMicaOrAcrylic(Handle, dark);
                BackColor = dark ? Color.FromArgb(20, 20, 20) : Color.White;
                ForeColor = dark ? Color.Gainsboro : Color.Black;
            }
            catch { }
        }

        private void InitializeComponent()
        {
            Text = "encryptTools - 工作区";
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(950, 680);
            MinimumSize = new Size(900, 600);
            Font = new Font("Microsoft YaHei UI", 9F);

            // 顶部菜单
            _menu = new MenuStrip
            {
                Dock = DockStyle.Top
            };
            var fileMenu = new ToolStripMenuItem("文件(&F)");

            var miNewParent = new ToolStripMenuItem("新建工作区(&N)")
            {
                ShortcutKeys = Keys.Control | Keys.N
            };
            miNewParent.DropDownItems.Add(new ToolStripMenuItem("文件加密 / 解密", null, (_, __) => NewWorkspace("文件")));
            miNewParent.DropDownItems.Add(new ToolStripMenuItem("字符串加密 / 解密", null, (_, __) => NewWorkspace("字符串")));
            miNewParent.DropDownItems.Add(new ToolStripMenuItem("图片像素化加密 / 解密", null, (_, __) => NewWorkspace("图片")));

            var miOpen = new ToolStripMenuItem("打开(&O)...", null, (_, __) => OpenWorkspace())
            {
                ShortcutKeys = Keys.Control | Keys.O
            };
            var miCloseAll = new ToolStripMenuItem("关闭所有工作区(&L)", null, (_, __) => CloseAllWorkspaces());

            fileMenu.DropDownItems.Add(miNewParent);
            fileMenu.DropDownItems.Add(miOpen);
            fileMenu.DropDownItems.Add(miCloseAll);
            fileMenu.DropDownItems.Add(new ToolStripSeparator());
            fileMenu.DropDownItems.Add(new ToolStripMenuItem("退出(&X)", null, (_, __) => Close()));

            var editMenu = new ToolStripMenuItem("编辑(&E)");
            editMenu.DropDownItems.Add(new ToolStripMenuItem("新建密码文件(&N)", null, (_, __) => ShowNewPasswordFile()));
            editMenu.DropDownItems.Add(new ToolStripMenuItem("密码文件导入(&I)", null, (_, __) => ShowImportPasswordFile()));
            editMenu.DropDownItems.Add(new ToolStripMenuItem("密码文件编辑(&E)", null, (_, __) => ShowEditPasswordFile()));

            var helpMenu = new ToolStripMenuItem("帮助(&H)");
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("使用手册", null, (_, __) => OpenHelp()));
            helpMenu.DropDownItems.Add(new ToolStripSeparator());
            helpMenu.DropDownItems.Add(new ToolStripMenuItem("关于", null, (_, __) => OpenAbout()));

            _menu.Items.Add(fileMenu);
            _menu.Items.Add(editMenu);
            _menu.Items.Add(helpMenu);
            MainMenuStrip = _menu;
            Controls.Add(_menu);

            // 中部：垂直 SplitContainer（上：工作区 Tab，下：日志区，均可拖拽调整高度）
            _vertSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 4
            };
            _vertSplit.SplitterDistance = 420;
            _vertSplit.Panel1.Padding = new Padding(0, 16, 0, 0); // 菜单栏与工作区 Tab 之间留出间隔

            // 上：TabControl 工作区
            _tabWorkspaces = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.Normal,
                Multiline = false,
                Alignment = TabAlignment.Top,
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(90, 32),
                HotTrack = true
            };
            _tabWorkspaces.Padding = new Point(12, 6); // 与文件加密解密工具栏按钮高度接近
            _tabWorkspaces.MouseUp += TabWorkspaces_MouseUp;

            // 默认欢迎工作区
            var welcomeTab = new TabPage("欢迎")
            {
                BackColor = SystemColors.Control
            };
            var welcomePanel = new Panel { Dock = DockStyle.Fill };
            var title = new Label
            {
                Text = "快速加密 / 解密",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 28f, FontStyle.Bold),
                Location = new Point(40, 40)
            };
            var subtitle = new Label
            {
                Text = "支持文件 · 字符串 · 图片像素化 · 常见规则智能解密",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Regular),
                ForeColor = Color.DimGray,
                Location = new Point(44, 90)
            };
            var btnFile = new Button
            {
                Text = "创建文件加密解密工作区",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
                Location = new Point(46, 140)
            };
            var btnString = new Button
            {
                Text = "创建字符串加密解密工作区",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
                Location = new Point(46, 178)
            };
            var btnImage = new Button
            {
                Text = "创建图片加密解密工作区",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
                Location = new Point(46, 216)
            };
            btnFile.Click += (_, __) => NewWorkspace("文件");
            btnString.Click += (_, __) => NewWorkspace("字符串");
            btnImage.Click += (_, __) => NewWorkspace("图片");
            welcomePanel.Controls.Add(title);
            welcomePanel.Controls.Add(subtitle);
            welcomePanel.Controls.Add(btnFile);
            welcomePanel.Controls.Add(btnString);
            welcomePanel.Controls.Add(btnImage);
            welcomeTab.Controls.Add(welcomePanel);
            _tabWorkspaces.TabPages.Add(welcomeTab);

            _tabWorkspaces.SelectedIndexChanged += TabWorkspaces_SelectedIndexChanged;
            _vertSplit.Panel1.Controls.Add(_tabWorkspaces);

            // 下：日志区域（当前选中工作区的独立日志 TextBox）
            _logHost = new Panel { Dock = DockStyle.Fill };

            // 为欢迎页初始化一个日志框
            var welcomeLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill
            };
            welcomeTab.Tag = new WorkspaceContext
            {
                Kind = "欢迎",
                SourcePath = null,
                LogBox = welcomeLog
            };
            _logHost.Controls.Add(welcomeLog);

            _vertSplit.Panel2.Controls.Add(_logHost);

            Controls.Add(_vertSplit);
            _vertSplit.BringToFront();

            // 底部状态栏
            _status = new StatusStrip
            {
                Dock = DockStyle.Bottom
            };
            _statusLeft = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusRight = new ToolStripStatusLabel("新建工作区") { IsLink = true };
            _statusRight.Click += (_, __) => NewWorkspace("文件");
            _status.Items.Add(_statusLeft);
            _status.Items.Add(_statusRight);
            Controls.Add(_status);
            _status.BringToFront();
            _menu.BringToFront();

            // Tab 右键菜单（关闭工作区）
            _tabContextMenu = new ContextMenuStrip();
            var miCloseThis = new ToolStripMenuItem("关闭此工作区", null, (_, __) =>
            {
                if (_tabContextTarget != null)
                {
                    CloseWorkspace(_tabContextTarget);
                    _tabContextTarget = null;
                }
            });
            _tabContextMenu.Items.Add(miCloseThis);
        }

        private void NewWorkspace(string kind)
        {
            switch (kind)
            {
                case "文件":
                    CreateFileWorkspace();
                    break;
                case "字符串":
                    CreateStringWorkspace();
                    break;
                case "图片":
                    CreateImageWorkspace();
                    break;
                default:
                    CreateFileWorkspace();
                    break;
            }
        }

        private void CreateFileWorkspace()
        {
            var index = _tabWorkspaces.TabPages.Count;
            var tab = new TabPage($"工作区 {index}")
            {
                BackColor = SystemColors.Control
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3

            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 工具栏，换行时增高，下方预览区自动缩小
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 主区域（文件/文件夹预览）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // 底部状态条

            // 上方工具栏：取消滚动条，按钮与下拉框排不下时自动换行
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SystemColors.ControlLight,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = new Padding(6, 4, 6, 4)
            };

            var btnSelectFile = new Button { Text = "选择文件", AutoSize = true, Margin = new Padding(0, 0, 6, 2) };
            var btnSelectFolder = new Button { Text = "选择文件夹", AutoSize = true, Margin = new Padding(0, 0, 6, 2) };
            var lblDragHint = new Label { Text = "可拖拽", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 12, 0) };

            const int comboMaxW = 140;
            var lblAlgo = new Label { Text = "算法:", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            var cbAlgo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 2, 8, 2), MinimumSize = new Size(100, 0), MaximumSize = new Size(comboMaxW, 0), Width = 120 };
            // 算法列表：AES-256-GCM 仅当本机已安装 .NET 8 时在下拉框中展示（可用 GcmRunner 调用）；否则不展示，其余算法兼容 .NET 4.6
            var algoItems = new List<string>();
            if (RuntimeHelper.IsNet8InstalledOnMachine)
                algoItems.Add("AES-256-GCM");
            algoItems.Add("AES-128-CBC");
            algoItems.Add("ChaCha20-Poly1305");
            algoItems.Add("SM4");
            cbAlgo.Items.AddRange(algoItems.ToArray());
            cbAlgo.SelectedIndex = 0;
            SetComboDropDownWidth(cbAlgo, comboMaxW);
            var lblSuffix = new Label { Text = "后缀:", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            // 允许下拉选择常用后缀，也支持手动输入自定义后缀
            var cbSuffix = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Margin = new Padding(2, 2, 8, 2),
                MinimumSize = new Size(70, 0),
                MaximumSize = new Size(comboMaxW, 0),
                Width = 80
            };
            // 常用后缀列表
            cbSuffix.Items.AddRange(new object[]
            {
                ".enc1",
                ".enc2",
                ".enc",
                ".aes",
                ".bin",
                ".dat",
                ".secure"
            });
            cbSuffix.SelectedItem = ".enc1";
            SetComboDropDownWidth(cbSuffix, comboMaxW);
            var lblPwd = new Label { Text = "密码文件:", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            var cbPwdFile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 2, 8, 2), MinimumSize = new Size(100, 0), MaximumSize = new Size(comboMaxW, 0), Width = 120 };
            cbPwdFile.DropDown += (_, __) => SetComboDropDownWidth(cbPwdFile, comboMaxW);
            RefreshFileWorkspacePwdCombo(cbPwdFile);
            var chkSelfExe = new CheckBox { Text = "加密为可运行的exe", AutoSize = true, Margin = new Padding(8, 4, 4, 2) };
            var chkOverwrite = new CheckBox { Text = "覆盖原文件", AutoSize = true, Margin = new Padding(0, 4, 4, 2), Checked = true };
            var chkRandomFileName = new CheckBox { Text = "随机文件名", AutoSize = true, Margin = new Padding(0, 4, 12, 2), Checked = false };
            var btnEncrypt = new Button { Text = "执行加密", BackColor = Color.RoyalBlue, ForeColor = Color.White, AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnDecrypt = new Button { Text = "执行解密", BackColor = Color.SeaGreen, ForeColor = Color.White, AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnClear = new Button { Text = "清空", AutoSize = true, Margin = new Padding(4, 0, 4, 4) };

            toolbar.Controls.Add(btnSelectFile);
            toolbar.Controls.Add(btnSelectFolder);
            toolbar.Controls.Add(lblDragHint);
            toolbar.Controls.Add(lblAlgo);
            toolbar.Controls.Add(cbAlgo);
            toolbar.Controls.Add(lblSuffix);
            toolbar.Controls.Add(cbSuffix);
            toolbar.Controls.Add(lblPwd);
            toolbar.Controls.Add(cbPwdFile);
            toolbar.Controls.Add(chkSelfExe);
            toolbar.Controls.Add(chkOverwrite);
            toolbar.Controls.Add(chkRandomFileName);
            toolbar.Controls.Add(btnEncrypt);
            toolbar.Controls.Add(btnDecrypt);
            toolbar.Controls.Add(btnClear);

            // 中部：仅文件/文件夹预览列表
            var lvFiles = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                AllowDrop = true,
                OwnerDraw = true
            };
            lvFiles.DrawColumnHeader += (s, e) =>
            {
                e.DrawDefault = true;
            };
            lvFiles.DrawSubItem += (s, e) =>
            {
                if (e.ColumnIndex != 4) { e.DrawDefault = true; return; }
                int raw = e.Item?.Tag is int v ? v : -1;
                var r = e.Bounds;
                if (r.Width <= 0 || r.Height <= 0) return;
                e.Graphics.FillRectangle(SystemBrushes.Window, r);
                bool decryptMode = raw >= 1000;
                int percent = decryptMode ? raw - 1000 : raw;
                string text;
                int barW;
                if (raw < 0)
                {
                    text = "-";
                    barW = 0;
                }
                else
                {
                    text = percent + "%";
                    int p = Math.Max(0, Math.Min(100, percent));
                    if (decryptMode)
                        barW = (int)((r.Width - 4) * p / 100.0);
                    else
                        barW = (int)((r.Width - 4) * p / 100.0);
                }
                if (barW > 0)
                {
                    // 解密=绿色（含 100% 时整条填满绿色）；未解密/加密=红色
                    Color barColor = decryptMode
                        ? Color.FromArgb(0x4C, 0xAF, 0x50)   // 解密：绿色
                        : Color.FromArgb(0xD3, 0x32, 0x2F); // 加密/未解密：红色
                    using (var brush = new SolidBrush(barColor))
                    {
                        var barRect = new Rectangle(r.X + 2, r.Y + 2, barW, r.Height - 4);
                        e.Graphics.FillRectangle(brush, barRect);
                    }
                }
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(text, e.Item?.ListView?.Font ?? SystemFonts.DefaultFont, SystemBrushes.ControlText, r, sf);
            };
            lvFiles.Columns.Add("名称", 160);
            lvFiles.Columns.Add("路径", 280);
            lvFiles.Columns.Add("大小", 80);
            lvFiles.Columns.Add("状态", 80);
            lvFiles.Columns.Add("进度", 72);

            lvFiles.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };
            lvFiles.DragDrop += (s, e) =>
            {
                if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    foreach (var p in paths)
                        AddPathToList(lvFiles, p);
                }
            };

            var bottomStatus = new Panel { Dock = DockStyle.Fill };
            var lblStatus = new Label { Text = "就绪", Dock = DockStyle.Left, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Width = 300 };
            var linkHelp = new LinkLabel { Text = "查看算法细节", Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
            linkHelp.LinkClicked += (_, __) => OpenHelp();
            bottomStatus.Controls.Add(linkHelp);
            bottomStatus.Controls.Add(lblStatus);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(lvFiles, 0, 1);
            root.Controls.Add(bottomStatus, 0, 2);

            tab.Controls.Add(root);

            var logBox = new RichTextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Dock = DockStyle.Fill
            };
            var ctx = new WorkspaceContext
            {
                Kind = "文件",
                SourcePath = null,
                LogBox = logBox,
                FileListView = lvFiles,
                ChkPackExe = chkSelfExe,
                ChkOverwrite = chkOverwrite,
                ChkRandomFileName = chkRandomFileName,
                TxtPassword = null,
                CbPwdFile = cbPwdFile,
                CbAlgo = cbAlgo,
                CbSuffix = cbSuffix
            };
            tab.Tag = ctx;

            var fileComboToolTip = new ToolTip { AutoPopDelay = 8000 };
            void SetComboTooltip(ComboBox c) { fileComboToolTip.SetToolTip(c, c.SelectedItem?.ToString() ?? c.Text ?? ""); }
            SetComboTooltip(cbAlgo);
            SetComboTooltip(cbSuffix);
            SetComboTooltip(cbPwdFile);
            cbAlgo.SelectedIndexChanged += (_, __) => SetComboTooltip(cbAlgo);
            cbSuffix.SelectedIndexChanged += (_, __) => SetComboTooltip(cbSuffix);
            cbSuffix.TextChanged += (_, __) => SetComboTooltip(cbSuffix);
            cbPwdFile.SelectedIndexChanged += (_, __) => SetComboTooltip(cbPwdFile);

            void UpdateEncryptDecryptEnabled()
            {
                bool hasPwd = cbPwdFile.SelectedIndex > 0 && cbPwdFile.SelectedItem is string s && s != "(未选择)";
                btnEncrypt.Enabled = hasPwd;
                btnDecrypt.Enabled = hasPwd;
            }
            cbPwdFile.SelectedIndexChanged += (_, __) => UpdateEncryptDecryptEnabled();
            // 算法变化时自动联动默认后缀：AES-256-GCM -> .enc2，其它 -> .enc1
            void SyncSuffixWithAlgo()
            {
                if (cbAlgo.SelectedItem is string algText)
                {
                    if (algText.StartsWith("AES-256-GCM", StringComparison.OrdinalIgnoreCase))
                        cbSuffix.Text = ".enc2";
                    else
                        cbSuffix.Text = ".enc1";
                }
            }
            cbAlgo.SelectedIndexChanged += (_, __) => SyncSuffixWithAlgo();
            SyncSuffixWithAlgo();
            // 手动输入自定义后缀后，统一清洗并自动加入下拉列表，方便下次选择
            cbSuffix.Validated += (_, __) =>
            {
                var t = cbSuffix.Text;
                if (string.IsNullOrWhiteSpace(t)) return;
                t = SanitizeExtensionLocalForSuffix(t);
                cbSuffix.Text = t;
                bool exists = false;
                foreach (var item in cbSuffix.Items)
                {
                    if (item is string s && string.Equals(s, t, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists)
                    cbSuffix.Items.Add(t);
            };
            UpdateEncryptDecryptEnabled();

            btnSelectFile.Click += (_, __) => SelectSourceFiles(ctx);
            btnSelectFolder.Click += (_, __) => SelectSourceFolder(ctx);
            btnEncrypt.Click += async (_, __) => await ExecuteEncryptWorkspace(ctx);
            btnDecrypt.Click += async (_, __) => await ExecuteDecryptWorkspace(ctx);
            btnClear.Click += (_, __) =>
            {
                lvFiles.Items.Clear();
                ctx.LogBox.Clear();
            };

            _tabWorkspaces.TabPages.Add(tab);
            _tabWorkspaces.SelectedTab = tab;

            _logHost.Controls.Clear();
            _logHost.Controls.Add(logBox);

            _statusLeft.Text = $"已创建新工作区：文件加密 / 解密";
        }

        private void CreateStringWorkspace()
        {
            var index = _tabWorkspaces.TabPages.Count;
            var tab = new TabPage($"字符串工作区 {index}")
            {
                BackColor = SystemColors.Control
            };

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
                Padding = new Padding(6, 4, 6, 4)
            };
            var btnPaste = new Button { Text = "从剪贴板粘贴", AutoSize = true, Margin = new Padding(0, 0, 6, 4) };
            var btnClear = new Button { Text = "清空", AutoSize = true, Margin = new Padding(0, 0, 6, 4) };
            const int comboMaxW = 140;
            var lblMode = new Label { Text = "加密模式:", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            var cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 2, 8, 4), MinimumSize = new Size(100, 0), MaximumSize = new Size(comboMaxW, 0), Width = 120 };
            cbMode.Items.AddRange(new object[] { "对称（AES）", "非对称（RSA）", "混合（PGP）" });
            cbMode.SelectedIndex = 0;
            SetComboDropDownWidth(cbMode, comboMaxW);
            var lblEnc = new Label { Text = "编码输出:", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            var cbEncoding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 2, 8, 4), MinimumSize = new Size(80, 0), MaximumSize = new Size(comboMaxW, 0), Width = 90 };
            cbEncoding.Items.AddRange(new object[] { "Base64", "Hex", "URL编码", "Binary" });
            cbEncoding.SelectedIndex = 0;
            SetComboDropDownWidth(cbEncoding, comboMaxW);
            var strComboToolTip = new ToolTip { AutoPopDelay = 8000 };
            strComboToolTip.SetToolTip(cbMode, cbMode.SelectedItem?.ToString() ?? "");
            strComboToolTip.SetToolTip(cbEncoding, cbEncoding.SelectedItem?.ToString() ?? "");
            cbMode.SelectedIndexChanged += (_, __) => strComboToolTip.SetToolTip(cbMode, cbMode.SelectedItem?.ToString() ?? "");
            cbEncoding.SelectedIndexChanged += (_, __) => strComboToolTip.SetToolTip(cbEncoding, cbEncoding.SelectedItem?.ToString() ?? "");
            var btnEncrypt = new Button { Text = "加密", BackColor = Color.RoyalBlue, ForeColor = Color.White, AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnDecrypt = new Button { Text = "解密", BackColor = Color.SeaGreen, ForeColor = Color.White, AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnCopyOut = new Button { Text = "复制输出", AutoSize = true, Margin = new Padding(4, 0, 4, 4) };

            toolbar.Controls.Add(btnPaste);
            toolbar.Controls.Add(btnClear);
            toolbar.Controls.Add(lblMode);
            toolbar.Controls.Add(cbMode);
            toolbar.Controls.Add(lblEnc);
            toolbar.Controls.Add(cbEncoding);
            toolbar.Controls.Add(btnEncrypt);
            toolbar.Controls.Add(btnDecrypt);
            toolbar.Controls.Add(btnCopyOut);

            var mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2
            };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var txtIn = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
#if !NET48
            #if !NET46 && !NET48 && !NET461
            txtIn.PlaceholderText = "输入明文或密文";
#endif
#endif
            var cbAutoDetect = new CheckBox { Text = "自动检测格式（Base64/Hex/JSON等）", Dock = DockStyle.Bottom, AutoSize = true };
            leftPanel.Controls.Add(txtIn);
            leftPanel.Controls.Add(cbAutoDetect);

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            var txtOut = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
#if !NET46 && !NET48 && !NET461
            txtOut.PlaceholderText = "输出结果";
#endif
            var btnSave = new Button { Text = "保存为文件", Dock = DockStyle.Bottom, Height = 28 };
            btnSave.Click += (_, __) =>
            {
                using var dlg = new SaveFileDialog { Title = "保存输出结果", Filter = "文本文件|*.txt|所有文件|*.*" };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    System.IO.File.WriteAllText(dlg.FileName, txtOut.Text);
                }
            };
            rightPanel.Controls.Add(txtOut);
            rightPanel.Controls.Add(btnSave);

            mainGrid.Controls.Add(leftPanel, 0, 0);
            mainGrid.Controls.Add(rightPanel, 1, 0);

            var bottomStatus = new Panel { Dock = DockStyle.Fill };
            var lblStatus = new Label { Text = "就绪", Dock = DockStyle.Left, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Width = 260 };
            bottomStatus.Controls.Add(lblStatus);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(mainGrid, 0, 1);
            root.Controls.Add(bottomStatus, 0, 2);

            tab.Controls.Add(root);

            var logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            var ctx = new WorkspaceContext { Kind = "字符串", SourcePath = null, LogBox = logBox };
            tab.Tag = ctx;

            btnPaste.Click += (_, __) =>
            {
                if (Clipboard.ContainsText())
                {
                    txtIn.Text = Clipboard.GetText();
                }
            };
            btnClear.Click += (_, __) =>
            {
                txtIn.Clear();
                txtOut.Clear();
            };
            btnEncrypt.Click += (_, __) =>
            {
                // 占位：简单回显
                txtOut.Text = $"[加密模拟] {txtIn.Text}";
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 字符串加密占位执行完成。{Environment.NewLine}");
            };
            btnDecrypt.Click += (_, __) =>
            {
                txtOut.Text = $"[解密模拟] {txtIn.Text}";
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 字符串解密占位执行完成。{Environment.NewLine}");
            };
            btnCopyOut.Click += (_, __) =>
            {
                if (!string.IsNullOrEmpty(txtOut.Text))
                {
                    Clipboard.SetText(txtOut.Text);
                }
            };

            _tabWorkspaces.TabPages.Add(tab);
            _tabWorkspaces.SelectedTab = tab;
            _logHost.Controls.Clear();
            _logHost.Controls.Add(logBox);
            _statusLeft.Text = "已创建新工作区：字符串加密 / 解密";
        }

        private void CreateImageWorkspace()
        {
            var index = _tabWorkspaces.TabPages.Count;
            var tab = new TabPage($"图片工作区 {index}")
            {
                BackColor = SystemColors.Control
            };

            var logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            void Log(string msg)
            {
                if (string.IsNullOrWhiteSpace(msg)) return;
                if (logBox.IsDisposed) return;
                try { logBox.Invoke(() => logBox.AppendText(msg + Environment.NewLine)); } catch { }
            }
            var imagePanel = new ImageWorkspacePanel(Log, null);
            tab.Controls.Add(imagePanel);

            var ctx = new WorkspaceContext { Kind = "图片", SourcePath = null, LogBox = logBox };
            tab.Tag = ctx;

            _tabWorkspaces.TabPages.Add(tab);
            _tabWorkspaces.SelectedTab = tab;
            _logHost.Controls.Clear();
            _logHost.Controls.Add(logBox);
            _statusLeft.Text = "已创建新工作区：图片像素化加密 / 解密";
        }

        private void ConfigureFileSplit(SplitContainer split)
        {
            EventHandler? handler = null;
            handler = (_, __) =>
            {
                if (split.Width <= 0)
                    return;

                // 只在首次获得有效宽度时配置一次
                split.SizeChanged -= handler!;

                // 右侧仅保留少量宽度，列表区域扩大
                const int rightPreferredMin = 120;
                if (split.Width <= rightPreferredMin + split.Panel1MinSize + split.SplitterWidth)
                    return;

                int desiredLeft = (int)(split.Width * 0.78);
                int maxLeft = split.Width - rightPreferredMin;
                if (maxLeft < split.Panel1MinSize)
                    maxLeft = split.Panel1MinSize;

                desiredLeft = Math.Max(split.Panel1MinSize, Math.Min(desiredLeft, maxLeft));
                try
                {
                    split.SplitterDistance = desiredLeft;
                }
                catch
                {
                    // 如果仍然不满足内部约束，则忽略，采用默认布局
                }
            };

            split.SizeChanged += handler;
        }

        private void AddPathToList(ListView lv, string path)
        {
            try
            {
                var name = System.IO.Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                    name = path;
                string sizeText = "";
                if (System.IO.Directory.Exists(path))
                    sizeText = "<文件夹>";
                else if (System.IO.File.Exists(path))
                {
                    var fi = new System.IO.FileInfo(path);
                    sizeText = $"{fi.Length / 1024} KB";
                }
                string status = "正常";
                if (System.IO.File.Exists(path) && CryptoService.IsWxEncryptedFile(path))
                    status = "已加密";
                else if (System.IO.Directory.Exists(path))
                {
                    try
                    {
                        if (Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).Any(f => CryptoService.IsWxEncryptedFile(f)))
                            status = "已加密";
                    }
                    catch { }
                }
                var item = new ListViewItem(new[] { name, path, sizeText, status, "-" });
                item.Tag = -1;
                lv.Items.Add(item);
            }
            catch { }
        }

        private void TryLoadImage(PictureBox box, string path)
        {
            try
            {
                box.Image = Image.FromFile(path);
            }
            catch (Exception)
            {
                MessageBox.Show(this, "无法加载图片。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void TabWorkspaces_SelectedIndexChanged(object? sender, EventArgs e)
        {
            var tab = _tabWorkspaces.SelectedTab;
            if (tab?.Tag is WorkspaceContext ctx)
            {
                _logHost.Controls.Clear();
                _logHost.Controls.Add(ctx.LogBox);
            }
        }

        private void TabWorkspaces_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;

            for (int i = 0; i < _tabWorkspaces.TabCount; i++)
            {
                var rect = _tabWorkspaces.GetTabRect(i);
                if (rect.Contains(e.Location))
                {
                    var tab = _tabWorkspaces.TabPages[i];
                    // 欢迎页不允许通过右键关闭
                    if (tab.Text == "欢迎")
                        return;

                    _tabContextTarget = tab;
                    _tabContextMenu.Show(_tabWorkspaces, e.Location);
                    break;
                }
            }
        }

        private void OpenWorkspace()
        {
            using var dlg = new OpenFileDialog
            {
                Title = "选择保存的工作区文件（占位）",
                Filter = "所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                _statusLeft.Text = "打开工作区: " + System.IO.Path.GetFileName(dlg.FileName);
            }
        }

        private void ExecuteEncrypt()
        {
            if (_tabWorkspaces.SelectedTab?.Tag is WorkspaceContext ctx)
            {
                _ = ExecuteEncryptWorkspace(ctx);
            }
        }

        private void ExecuteDecrypt()
        {
            if (_tabWorkspaces.SelectedTab?.Tag is WorkspaceContext ctx)
                _ = ExecuteDecryptWorkspace(ctx);
        }

        private void CloseWorkspace(TabPage tab)
        {
            if (tab.Text == "欢迎")
                return;

            int index = _tabWorkspaces.TabPages.IndexOf(tab);
            if (index < 0) return;

            _tabWorkspaces.TabPages.RemoveAt(index);

            if (_tabWorkspaces.TabCount > 0)
            {
                var current = _tabWorkspaces.SelectedTab;
                if (current?.Tag is WorkspaceContext ctx)
                {
                    _logHost.Controls.Clear();
                    _logHost.Controls.Add(ctx.LogBox);
                }
            }
            else
            {
                _logHost.Controls.Clear();
            }
        }

        private void CloseAllWorkspaces()
        {
            for (int i = _tabWorkspaces.TabPages.Count - 1; i >= 0; i--)
            {
                var tab = _tabWorkspaces.TabPages[i];
                if (tab.Text == "欢迎")
                    continue;
                _tabWorkspaces.TabPages.RemoveAt(i);
            }

            if (_tabWorkspaces.TabCount > 0 &&
                _tabWorkspaces.TabPages[0].Tag is WorkspaceContext ctx)
            {
                _tabWorkspaces.SelectedIndex = 0;
                _logHost.Controls.Clear();
                _logHost.Controls.Add(ctx.LogBox);
            }
        }

        private void SelectSourceForWorkspace(WorkspaceContext ctx)
        {
            var choice = MessageBox.Show(
                this,
                "是：选择文件\r\n否：选择文件夹",
                "选择源类型",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
            {
                return;
            }

            if (choice == DialogResult.Yes)
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "选择要加密的文件",
                    Filter = "所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    ctx.SourcePath = dlg.FileName;
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已选择源文件: {ctx.SourcePath}{Environment.NewLine}");
                    _statusLeft.Text = "已选择源文件。";
                }
            }
            else
            {
                using var dlgFolder = new FolderBrowserDialog
                {
                    Description = "选择要加密的文件夹"
                };
                if (dlgFolder.ShowDialog(this) == DialogResult.OK)
                {
                    ctx.SourcePath = dlgFolder.SelectedPath;
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已选择源文件夹: {ctx.SourcePath}{Environment.NewLine}");
                    _statusLeft.Text = "已选择源文件夹。";
                }
            }
        }

        private static CryptoAlgorithm MapAlgorithm(ComboBox? cb)
        {
            if (cb == null || cb.SelectedIndex < 0) return CryptoAlgorithm.AesCbc;
            var text = cb.SelectedItem?.ToString() ?? "";
            if (string.Equals(text, "AES-256-GCM", StringComparison.OrdinalIgnoreCase))
                return CryptoAlgorithm.AesGcm;
            return CryptoAlgorithm.AesCbc;
        }

        /// <summary>根据后缀下拉框选择返回加密扩展名；若未选择则按算法返回 .enc1 / .enc2。</summary>
        private static string GetSelectedEncryptedExtension(ComboBox? cbSuffix, CryptoAlgorithm alg)
        {
            if (cbSuffix != null)
            {
                var s = cbSuffix.Text;
                if (!string.IsNullOrWhiteSpace(s))
                    return SanitizeExtensionLocalForSuffix(s);
            }
            return GetEncryptedExtension(alg);
        }

        /// <summary>清洗用户在后缀下拉框中输入的自定义后缀，例如补充前导点、去掉非法字符、限制长度。</summary>
        private static string SanitizeExtensionLocalForSuffix(string ext)
        {
            if (string.IsNullOrWhiteSpace(ext))
                return ".enc1";
            ext = ext.Trim();
            if (!ext.StartsWith(".", StringComparison.Ordinal))
                ext = "." + ext;
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(ext.Length);
            foreach (var c in ext)
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            if (sb.Length == 1 && sb[0] == '.')
            {
                sb.Append("enc1");
            }
            if (sb.Length > 16)
            {
                sb.Length = 16;
            }
            return sb.ToString();
        }

        private static string GetEncryptedExtension(CryptoAlgorithm alg)
        {
            return alg == CryptoAlgorithm.AesGcm ? ".enc2" : ".enc1";
        }

        private string? GetPasswordFromFileWorkspace(WorkspaceContext ctx)
        {
            if (ctx.CbPwdFile == null || ctx.CbPwdFile.SelectedIndex <= 0) return null;
            if (ctx.CbPwdFile.SelectedItem is string name && name.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
            {
                var path = Path.Combine(PasswordFileService.GetPwdDirectory(), name);
                if (File.Exists(path))
                {
                    try { return PasswordFileHelper.LoadPasswordFromFile(path); } catch { }
                }
            }
            return null;
        }

        private static string GetPasswordFileStem(WorkspaceContext ctx)
        {
            if (ctx.CbPwdFile?.SelectedItem is string name && !string.IsNullOrWhiteSpace(name) && name != "(未选择)")
                return Path.GetFileNameWithoutExtension(name);
            return "pwd";
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
                        var s = item?.ToString() ?? "";
                        var w = (int)Math.Ceiling(g.MeasureString(s, cb.Font).Width) + 24;
                        if (w > maxW) maxW = w;
                    }
                }
                cb.DropDownWidth = Math.Min(Math.Max(maxW, 80), maxWidth);
            }
            catch { }
        }

        private static void RefreshFileWorkspacePwdCombo(ComboBox cb, string? preserveSelectedFileName = null)
        {
            cb.Items.Clear();
            cb.Items.Add("(未选择)");
            try
            {
                PasswordFileService.EnsurePwdDirectory();
                foreach (var f in PasswordFileService.ListPwdFiles())
                    cb.Items.Add(Path.GetFileName(f));
                if (!string.IsNullOrEmpty(preserveSelectedFileName))
                {
                    for (int i = 0; i < cb.Items.Count; i++)
                    {
                        if (string.Equals(cb.Items[i]?.ToString(), preserveSelectedFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            cb.SelectedIndex = i;
                            break;
                        }
                    }
                }
                if (cb.SelectedIndex < 0) cb.SelectedIndex = 0;
            }
            catch { }
            SetComboDropDownWidth(cb);
        }

        /// <summary>刷新所有文件工作区的密码文件下拉框（新建/导入保存后调用，实时展示 pwd 目录）</summary>
        private void RefreshAllFileWorkspacePwdCombos()
        {
            for (int i = 0; i < _tabWorkspaces.TabPages.Count; i++)
            {
                var tab = _tabWorkspaces.TabPages[i];
                if (tab?.Tag is not WorkspaceContext ctx || ctx.Kind != "文件" || ctx.CbPwdFile == null) continue;
                string? current = null;
                if (ctx.CbPwdFile.SelectedIndex > 0 && ctx.CbPwdFile.SelectedItem is string s && s != "(未选择)")
                    current = s;
                RefreshFileWorkspacePwdCombo(ctx.CbPwdFile, preserveSelectedFileName: current);
            }
        }

        private void SelectSourceFiles(WorkspaceContext ctx)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "选择要加密/解密的文件",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK || dlg.FileNames.Length == 0) return;
            var lv = ctx.FileListView;
            if (lv == null) return;
            foreach (var path in dlg.FileNames)
                AddPathToList(lv, path);
            ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已添加 {dlg.FileNames.Length} 个文件。{Environment.NewLine}");
        }

        private void SelectSourceFolder(WorkspaceContext ctx)
        {
            using var dlg = new FolderBrowserDialog { Description = "选择要加密/解密的文件夹" };
            if (dlg.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedPath)) return;
            var lv = ctx.FileListView;
            if (lv == null) return;
            AddPathToList(lv, dlg.SelectedPath);
            ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已添加文件夹: {dlg.SelectedPath}{Environment.NewLine}");
        }

        private List<string> GetFilePathsFromContext(WorkspaceContext ctx)
        {
            var list = new List<string>();
            if (ctx.FileListView != null)
            {
                foreach (ListViewItem item in ctx.FileListView.Items)
                {
                    if (item.SubItems.Count > 1)
                    {
                        var path = item.SubItems[1].Text;
                        if (!string.IsNullOrWhiteSpace(path)) list.Add(path);
                    }
                }
            }
            if (list.Count == 0 && !string.IsNullOrWhiteSpace(ctx.SourcePath))
                list.Add(ctx.SourcePath);
            return list;
        }

        private static string GetRelativePathCompat(string basePath, string fullPath)
        {
            try
            {
                var baseFull = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullFull = Path.GetFullPath(fullPath);
                if (fullFull.StartsWith(baseFull, StringComparison.OrdinalIgnoreCase))
                    return fullFull.Substring(baseFull.Length).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }
            catch { }
            return Path.GetFileName(fullPath);
        }

        /// <summary>
        /// 去掉被其他路径包含的子路径，避免递归时同一文件被处理多次（如同时拖入 D:\test 和 D:\test\子文件夹）。
        /// </summary>
        private static List<string> RemoveNestedPaths(List<string> paths)
        {
            if (paths == null || paths.Count <= 1) return paths ?? new List<string>();
            var normalized = new List<(string original, string full)>();
            char sep = Path.DirectorySeparatorChar;
            foreach (var p in paths)
            {
                try
                {
                    string full = Path.GetFullPath(p);
                    if (Directory.Exists(full))
                        full = full.TrimEnd(sep, Path.AltDirectorySeparatorChar) + sep;
                    normalized.Add((p, full));
                }
                catch
                {
                    normalized.Add((p, p));
                }
            }
            var result = new List<string>();
            for (int i = 0; i < normalized.Count; i++)
            {
                var (orig, full) = normalized[i];
                bool underOther = false;
                for (int j = 0; j < normalized.Count; j++)
                {
                    if (i == j) continue;
                    string otherFull = normalized[j].full;
                    if (!Directory.Exists(normalized[j].original))
                        continue;
                    if (otherFull.Length > 0 && otherFull[otherFull.Length - 1] != sep)
                        otherFull = otherFull + sep;
                    if (full.StartsWith(otherFull, StringComparison.OrdinalIgnoreCase) && full.Length > otherFull.Length)
                    {
                        underOther = true;
                        break;
                    }
                }
                if (!underOther)
                    result.Add(orig);
            }
            return result.Count > 0 ? result : new List<string> { paths[0] };
        }

        private void UpdateFileListItemPathStatus(ListView? lv, string sourcePath, string outPath, string status)
        {
            if (lv == null) return;
            foreach (ListViewItem item in lv.Items)
            {
                if (item.SubItems.Count <= 1) continue;
                if (!string.Equals(item.SubItems[1].Text, sourcePath, StringComparison.OrdinalIgnoreCase)) continue;
                item.SubItems[0].Text = Path.GetFileName(outPath);
                item.SubItems[1].Text = outPath;
                if (item.SubItems.Count > 3) item.SubItems[3].Text = status;
                break;
            }
        }

        /// <summary>自定义 IProgress：每次 Report 都单独 BeginInvoke 到 UI，避免 .NET Progress 只回调最后一次导致单文件只显示 0% 和 100%。</summary>
        private sealed class FileListProgress : IProgress<double>
        {
            private readonly ListView? _lv;
            private readonly string _sourcePath;
            private readonly bool _isDecrypt;

            internal FileListProgress(ListView? lv, string sourcePath, bool isDecrypt)
            {
                _lv = lv;
                _sourcePath = sourcePath;
                _isDecrypt = isDecrypt;
            }

            public void Report(double value)
            {
                if (_lv == null || _lv.IsDisposed) return;
                int percent = Math.Min(100, Math.Max(0, (int)(value * 100)));
                _lv.BeginInvoke(new Action(() => UpdateFileListProgress(_lv, _sourcePath, percent, _isDecrypt)));
            }
        }

        private static IProgress<double> CreateFileListProgress(ListView? lv, string sourcePath, bool isDecrypt)
            => new FileListProgress(lv, sourcePath, isDecrypt);

        private static void UpdateFileListProgress(ListView? lv, string sourcePath, int percent, bool isDecrypt = false)
        {
            if (lv == null) return;
            if (lv.InvokeRequired) { lv.BeginInvoke(new Action(() => UpdateFileListProgress(lv, sourcePath, percent, isDecrypt))); return; }
            int p = Math.Min(100, Math.Max(0, percent));
            int tag = isDecrypt ? (1000 + p) : p;
            string text = p + "%";
            foreach (ListViewItem item in lv.Items)
            {
                if (item.SubItems.Count <= 1) continue;
                if (!string.Equals(item.SubItems[1].Text, sourcePath, StringComparison.OrdinalIgnoreCase)) continue;
                bool percentChanged = !(item.Tag is int existingTag && existingTag == tag);
                if (item.SubItems.Count <= 4) item.SubItems.Add(text);
                else item.SubItems[4].Text = text;
                item.Tag = tag;
                if (percentChanged) { try { lv.Invalidate(lv.GetItemRect(item.Index)); } catch { lv.Invalidate(); } }
                break;
            }
        }

        /// <summary>
        /// 不勾选覆盖时，所有拖入项共用一个 output 目录；若拖入路径已在 output 内则不再嵌套 output/output。
        /// </summary>
        private static string GetCommonOutputRoot(List<string> paths, bool inPlace)
        {
            if (inPlace || paths == null || paths.Count == 0)
                return "";
            string parent = GetCommonParentOnly(paths);
            if (string.IsNullOrEmpty(parent))
                return Path.Combine(Environment.CurrentDirectory, "output");
            string name = Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.Equals(name, "output", StringComparison.OrdinalIgnoreCase))
                return parent;
            return Path.Combine(parent, "output");
        }

        /// <summary>
        /// 在父目录下查找所有符合 "UUID_密码名_output" 的输出目录，用于不覆盖时一次解密多次加密产生的多个目录。
        /// </summary>
        private static List<string> GetNonInPlaceOutputFolders(string parentDir)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir)) return list;
            try
            {
                foreach (string dir in Directory.GetDirectories(parentDir))
                {
                    string name = Path.GetFileName(dir);
                    if (name != null && name.EndsWith("_output", StringComparison.OrdinalIgnoreCase) && name.IndexOf('_') >= 0)
                        list.Add(dir);
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// 取所有路径的公共父目录（不追加 output），用于解密时定位 output 文件夹。
        /// </summary>
        private static string GetCommonParentOnly(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return "";
            var dirs = new List<string>();
            foreach (var p in paths)
            {
                try
                {
                    var full = Path.GetFullPath(p);
                    if (Directory.Exists(full))
                        dirs.Add(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    else
                        dirs.Add(Path.GetDirectoryName(full) ?? full);
                }
                catch { }
            }
            if (dirs.Count == 0) return "";
            string common = dirs[0];
            for (int i = 1; i < dirs.Count; i++)
            {
                var other = dirs[i];
                while (common.Length > 0 && !other.StartsWith(common, StringComparison.OrdinalIgnoreCase))
                {
                    var lastSep = common.LastIndexOf(Path.DirectorySeparatorChar);
                    if (lastSep <= 0) { common = ""; break; }
                    common = common.Substring(0, lastSep);
                }
                if (common.Length > 0 && other.Length > common.Length && other[common.Length] != Path.DirectorySeparatorChar)
                {
                    var lastSep = common.LastIndexOf(Path.DirectorySeparatorChar);
                    if (lastSep > 0) common = common.Substring(0, lastSep);
                }
            }
            if (string.IsNullOrEmpty(common)) common = Path.GetPathRoot(dirs[0]) ?? Environment.CurrentDirectory;
            return common;
        }

        private static bool IsSourceAlreadyEncryptedInOutput(string source, bool isDir, string commonOutputRoot, string encryptedExt)
        {
            if (string.IsNullOrEmpty(commonOutputRoot) || !Directory.Exists(commonOutputRoot))
                return false;
            string ext = string.IsNullOrWhiteSpace(encryptedExt) ? ".enc1" : encryptedExt.Trim();
            if (!isDir)
            {
                string fileName = Path.GetFileName(source);
                bool nameAlreadyHasExt = fileName.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".enc1", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".enc2", StringComparison.OrdinalIgnoreCase);
                string outPath = Path.Combine(commonOutputRoot, nameAlreadyHasExt ? fileName : fileName + ext);
                return File.Exists(outPath);
            }
            try
            {
                string root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string file in Directory.EnumerateFiles(source, "*", System.IO.SearchOption.AllDirectories))
                {
                    string rel = file.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                        ? file.Substring(root.Length)
                        : Path.GetFileName(file);
                    bool relAlreadyHasExt = rel.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".enc1", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".enc2", StringComparison.OrdinalIgnoreCase);
                    string outPath = Path.Combine(commonOutputRoot, relAlreadyHasExt ? rel : rel + ext);
                    if (!File.Exists(outPath))
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int CountEncryptedFilesInFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return 0;
            try
            {
                int n = 0;
                foreach (string f in Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories))
                {
                    if (CryptoService.IsWxEncryptedFile(f))
                        n++;
                }
                return n;
            }
            catch
            {
                return 0;
            }
        }

        private static int CountExePayloadInFolder(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                return 0;
            try
            {
                int n = 0;
                foreach (string f in Directory.GetFiles(dir, "*.exe", SearchOption.AllDirectories))
                {
                    if (ExePayload.HasPayload(f)) n++;
                }
                return n;
            }
            catch { return 0; }
        }

        private async Task ExecuteEncryptWorkspace(WorkspaceContext ctx)
        {
            try
            {
                var paths = GetFilePathsFromContext(ctx);
                if (paths.Count == 0)
                {
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 请先添加文件或选择源。{Environment.NewLine}");
                    _statusLeft.Text = "未选择文件。";
                    return;
                }
                paths = RemoveNestedPaths(paths);
                // 去重相同路径，避免重复处理 / 重复日志
                paths = new List<string>(new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase));

                string? password = GetPasswordFromFileWorkspace(ctx);
                if (string.IsNullOrWhiteSpace(password))
                {
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已取消：密码为空。{Environment.NewLine}");
                    return;
                }

                bool packExe = ctx.ChkPackExe?.Checked ?? false;
                bool inPlace = ctx.ChkOverwrite?.Checked ?? false;
                var algorithm = MapAlgorithm(ctx.CbAlgo);
                var encryptedExt = GetSelectedEncryptedExtension(ctx.CbSuffix, algorithm);
                // 单 exe 兼容：不拦截；加密时自动用 GcmRunner（.NET 8 已装）或 CBC（未装）

                _statusLeft.Text = "执行加密中…";
                var log = new Action<string>(m =>
                {
                    if (string.IsNullOrEmpty(m)) return;
                    try
                    {
                        string line = m.IndexOf(']') > 0 ? m : $"[{DateTime.Now:HH:mm:ss}] {m}";
                        void appendAndScroll()
                        {
                            ctx.LogBox.AppendText(line + Environment.NewLine);
                            ctx.LogBox.SelectionStart = ctx.LogBox.Text.Length;
                            ctx.LogBox.ScrollToCaret();
                        }
                        if (ctx.LogBox.InvokeRequired)
                            ctx.LogBox.BeginInvoke(new Action(appendAndScroll));
                        else
                            appendAndScroll();
                    }
                    catch { }
                });

                string commonOutputRoot;
                if (inPlace)
                    commonOutputRoot = "";
                else
                {
                    string parent = GetCommonParentOnly(paths);
                    if (string.IsNullOrEmpty(parent))
                        parent = Environment.CurrentDirectory;
                    commonOutputRoot = Path.Combine(parent, Guid.NewGuid().ToString("N") + "_" + GetPasswordFileStem(ctx) + "_output");
                    ctx.LastNonInPlaceOutputRoot = commonOutputRoot;
                    Directory.CreateDirectory(commonOutputRoot);
                }

                bool loggedAlreadyEncrypted = false;
                if (ctx.FileListView != null)
                {
                    foreach (ListViewItem it in ctx.FileListView.Items)
                    {
                        if (it.SubItems.Count <= 4) it.SubItems.Add("0%");
                        else it.SubItems[4].Text = "0%";
                        it.Tag = 0;
                    }
                    ctx.FileListView.Invalidate();
                }
                foreach (var source in paths)
                {
                    if (!File.Exists(source) && !Directory.Exists(source)) { continue; }
                    bool isDir = Directory.Exists(source);
                    string baseDir = Path.GetDirectoryName(source) ?? source;
                    string outDir = inPlace ? (isDir ? source : baseDir) : commonOutputRoot;

                    if (!inPlace && !packExe && IsSourceAlreadyEncryptedInOutput(source, isDir, commonOutputRoot, encryptedExt))
                    {
                        string showPath = isDir ? outDir : source;
                        UpdateFileListItemPathStatus(ctx.FileListView, source, showPath, "已加密");
                        // 目录中所有文件都已在 output 中存在时，不再重复输出日志，直接跳过
                        continue;
                    }

                    if (packExe)
                    {
                        var template = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "encryptTools.self.exe");
                        if (!File.Exists(template)) template = Application.ExecutablePath;
                        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                        {
                            log($"[{DateTime.Now:HH:mm:ss}] 封装EXE失败：未找到模板 encryptTools.self.exe 或当前程序。");
                            continue;
                        }

                        var filesToPack = isDir
                            ? Directory.GetFiles(source, "*", SearchOption.AllDirectories).Where(f => !Directory.Exists(f)).ToList()
                            : new List<string> { source };
                        if (filesToPack.Count == 0)
                        {
                            log($"[{DateTime.Now:HH:mm:ss}] 文件夹为空，跳过封装: {source}");
                            continue;
                        }

                        bool useRandomExeName = ctx.ChkRandomFileName?.Checked ?? false;
                        foreach (var oneFile in filesToPack)
                        {
                            if (!File.Exists(oneFile)) continue;
                            string outExe;
                            if (useRandomExeName)
                            {
                                string randomName = Guid.NewGuid().ToString("N") + ".exe";
                                outExe = Path.Combine(outDir, randomName);
                            }
                            else if (isDir)
                            {
                                var rel = GetRelativePathCompat(source, oneFile);
                                rel = Path.ChangeExtension(rel, ".exe");
                                outExe = Path.Combine(outDir, rel);
                            }
                            else
                            {
                                outExe = Path.Combine(outDir, Path.GetFileNameWithoutExtension(oneFile) + ".exe");
                            }
                            try { Directory.CreateDirectory(Path.GetDirectoryName(outExe) ?? outDir); } catch { }

                            log($"[{DateTime.Now:HH:mm:ss}] 开始封装EXE: {oneFile} -> {outExe}");
                            var tmpEnc = Path.Combine(Path.GetTempPath(), "encryptTools_pack_" + Guid.NewGuid().ToString("N") + ".enc");
                            try
                            {
                                // 封装 EXE 的进度：以“生成临时加密文件 tmpEnc”为准，按读取字节实时更新 oneFile 的进度
                                var packProgress = CreateFileListProgress(ctx.FileListView, oneFile, isDecrypt: false);
                                UpdateFileListProgress(ctx.FileListView, oneFile, 0);

                                // 封装 exe 时若非 .NET 8 环境则一律使用 CBC，确保打包后的 exe 在本机可直接解密
                                CryptoAlgorithm packAlgo = RuntimeHelper.IsNet8OrHigher ? algorithm : CryptoAlgorithm.AesCbc;
                                if (packAlgo == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && RuntimeHelper.IsNet8InstalledOnMachine)
                                    packAlgo = CryptoAlgorithm.AesCbc;
                                bool packUseGcm = (packAlgo == CryptoAlgorithm.AesGcm && RuntimeHelper.IsNet8InstalledOnMachine && !RuntimeHelper.IsNet8OrHigher);

                                if (packUseGcm)
                                {
                                    // GCM 走外部进程：用输出文件增长轮询模拟进度
                                    bool ok = await GcmRunner.EncryptAsync(oneFile, tmpEnc, password, packProgress, m => log($"[{DateTime.Now:HH:mm:ss}] {m}")).ConfigureAwait(false);
                                    if (!ok) { log($"[{DateTime.Now:HH:mm:ss}] 封装EXE失败: GCM 加密失败"); continue; }
                                }
                                else
                                {
                                    var crypto = new CryptoService();
                                    long fileLen = 0;
                                    try { fileLen = new FileInfo(oneFile).Length; } catch { }
                                    long processed = 0;
                                    int lastPct = -1;
                                    await crypto.EncryptFileAsync(oneFile, tmpEnc, packAlgo, password, 200_000, 256, new Progress<long>(bytes =>
                                    {
                                        processed += bytes;
                                        int pct = fileLen <= 0 ? 0 : Math.Min(100, (int)((double)processed / fileLen * 100));
                                        if (pct != lastPct) { lastPct = pct; packProgress.Report(pct / 100.0); }
                                    }), CancellationToken.None);
                                }
                                log($"[{DateTime.Now:HH:mm:ss}] 已生成临时加密文件: {tmpEnc}");
                                UpdateFileListProgress(ctx.FileListView, oneFile, 100);

                                var encBytes = File.ReadAllBytes(tmpEnc);
                                var meta = new ExePayload.PayloadMeta { Type = "file", Note = "encryptTools packed payload" };
                                ExePayload.WritePackedExe(template, outExe, meta, encBytes);

                                log($"[{DateTime.Now:HH:mm:ss}] 已写入封装EXE: {outExe}");
                                UpdateFileListItemPathStatus(ctx.FileListView, oneFile, outExe, "已封装EXE");
                                if (inPlace)
                                {
                                    try
                                    {
                                        File.Delete(oneFile);
                                        log($"[{DateTime.Now:HH:mm:ss}] 已删除源文件: {oneFile}");
                                    }
                                    catch (Exception exDel)
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] 删除源文件失败: {exDel.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                log($"[{DateTime.Now:HH:mm:ss}] 封装EXE失败: {ex.Message}");
                            }
                            finally
                            {
                                try { if (File.Exists(tmpEnc)) File.Delete(tmpEnc); } catch { }
                            }
                        }
                        continue;
                    }

                    string? lastOut = null;
                    var interceptLog = new Action<string>(m =>
                    {
                        if (string.IsNullOrWhiteSpace(m)) return;

                        // 只对真正执行加密的文件输出一行精简日志：已加密 xxx
                        if (m.StartsWith("加密:", StringComparison.Ordinal))
                        {
                            var i = m.IndexOf("->", StringComparison.Ordinal);
                            if (i > 0)
                            {
                                var dest = m.Substring(i + 2).Trim();
                                if (!string.IsNullOrWhiteSpace(dest))
                                {
                                    lastOut = dest;
                                    try
                                    {
                                        void appendAndScroll()
                                        {
                                            ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已加密: {dest}{Environment.NewLine}");
                                            ctx.LogBox.SelectionStart = ctx.LogBox.Text.Length;
                                            ctx.LogBox.ScrollToCaret();
                                        }
                                        if (ctx.LogBox.InvokeRequired) ctx.LogBox.BeginInvoke(new Action(appendAndScroll)); else appendAndScroll();
                                    }
                                    catch { }
                                }
                            }
                            return;
                        }
                        // 其它诸如“已加密，跳过 / 跳过不存在 / 无权限访问”等内部日志全部忽略
                    });
                    var options = new FileEncryptorOptions
                    {
                        SourcePath = source,
                        OutputRoot = outDir,
                        InPlace = inPlace,
                        Recursive = isDir,
                        RandomizeFileName = ctx.ChkRandomFileName?.Checked ?? false,
                        Algorithm = algorithm,
                        Password = password,
                        Iterations = 200_000,
                        AesKeySizeBits = 256,
                        Log = interceptLog,
                        EncryptedExtension = encryptedExt
                    };
                    var enc = new FileEncryptor(options);
                    var progress = CreateFileListProgress(ctx.FileListView, source, isDecrypt: false);
                    UpdateFileListProgress(ctx.FileListView, source, 0);
                    // 加密流程：读取源文件并上报进度 → 写入目标 → 若勾选覆盖则删除源文件；完成后将该项进度置为 100%（列表绘制为绿色）
                    await enc.EncryptAsync(progress, CancellationToken.None);
                    UpdateFileListProgress(ctx.FileListView, source, 100);
                    var ext = encryptedExt;
                    bool pathAlreadyEncrypted = source.EndsWith(".enc1", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".enc2", StringComparison.OrdinalIgnoreCase);
                    var outPath = isDir ? outDir : (pathAlreadyEncrypted ? Path.Combine(outDir, Path.GetFileName(source)) : (lastOut ?? Path.Combine(outDir, Path.GetFileName(source) + ext)));
                    if (!inPlace)
                        outPath = source;
                    UpdateFileListItemPathStatus(ctx.FileListView, source, outPath, "已加密");
                }
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 加密完成。");
                _statusLeft.Text = "加密完成。";
            }
            catch (Exception ex)
            {
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 加密失败: {ex.Message}");
                _statusLeft.Text = "加密失败。";
            }
        }

        private async Task ExecuteDecryptWorkspace(WorkspaceContext ctx)
        {
            try
            {
                var paths = GetFilePathsFromContext(ctx);
                if (paths.Count == 0)
                {
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 请先添加文件或选择源。{Environment.NewLine}");
                    _statusLeft.Text = "未选择文件。";
                    return;
                }
                paths = RemoveNestedPaths(paths);

                string? password = GetPasswordFromFileWorkspace(ctx);
                if (string.IsNullOrWhiteSpace(password))
                {
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已取消：密码为空。{Environment.NewLine}");
                    return;
                }

                bool inPlace = ctx.ChkOverwrite?.Checked ?? false;
                var algorithm = MapAlgorithm(ctx.CbAlgo);
                var encryptedExt = GetSelectedEncryptedExtension(ctx.CbSuffix, algorithm);
                _statusLeft.Text = "执行解密中…";
                var log = new Action<string>(m =>
                {
                    if (string.IsNullOrEmpty(m)) return;
                    try
                    {
                        string line = m.IndexOf(']') > 0 ? m : $"[{DateTime.Now:HH:mm:ss}] {m}";
                        void appendAndScroll()
                        {
                            ctx.LogBox.AppendText(line + Environment.NewLine);
                            ctx.LogBox.SelectionStart = ctx.LogBox.Text.Length;
                            ctx.LogBox.ScrollToCaret();
                        }
                        if (ctx.LogBox.InvokeRequired) ctx.LogBox.BeginInvoke(new Action(appendAndScroll)); else appendAndScroll();
                    }
                    catch { }
                });

                string commonParent = GetCommonParentOnly(paths);
                // 非覆盖模式下，从工作区路径的上级目录中查找所有 UUID_pwd_output 目录；
                // 如果当前公共父目录本身是 *_output，则回退到其上级目录，避免只找到其中一个。
                string searchRoot = commonParent;
                if (!string.IsNullOrEmpty(searchRoot))
                {
                    var name = Path.GetFileName(searchRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (!string.IsNullOrEmpty(name) &&
                        name.EndsWith("_output", StringComparison.OrdinalIgnoreCase))
                    {
                        var parentOfOutput = Path.GetDirectoryName(searchRoot);
                        if (!string.IsNullOrEmpty(parentOfOutput))
                            searchRoot = parentOfOutput;
                    }
                }

                var outputFolders = inPlace ? new List<string>() : GetNonInPlaceOutputFolders(searchRoot);

                // 如果工作区中本身就拖入了某个 *_output 目录，也一并加入解密列表
                if (!inPlace)
                {
                    foreach (var p in paths)
                    {
                        try
                        {
                            if (Directory.Exists(p))
                            {
                                var name = Path.GetFileName(Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                                if (!string.IsNullOrEmpty(name) &&
                                    name.EndsWith("_output", StringComparison.OrdinalIgnoreCase) &&
                                    !outputFolders.Any(d => string.Equals(d, p, StringComparison.OrdinalIgnoreCase)))
                                {
                                    outputFolders.Add(p);
                                }
                            }
                        }
                        catch { }
                    }
                }
                if (!inPlace && outputFolders.Count == 0 && !string.IsNullOrEmpty(ctx.LastNonInPlaceOutputRoot))
                    outputFolders.Add(ctx.LastNonInPlaceOutputRoot);
                // 旧版 common output 目录逻辑不再使用 UUID_output 方案，这里不再强制追加虚构的 output 目录

                if (!inPlace && outputFolders.Count > 0)
                {
                    int totalEnc = 0;
                    int totalExePayload = 0;
                    foreach (var dir in outputFolders)
                    {
                        totalEnc += CountEncryptedFilesInFolder(dir);
                        totalExePayload += CountExePayloadInFolder(dir);
                    }
                    if (totalEnc == 0 && totalExePayload == 0)
                    {
                        foreach (var p in paths)
                            UpdateFileListItemPathStatus(ctx.FileListView, p, p, "已解密");
                        _statusLeft.Text = "已解密。";
                        return;
                    }
                }

                var decryptSources = new List<string>();
                if (inPlace)
                    decryptSources.AddRange(paths);
                else
                {
                    decryptSources.AddRange(outputFolders);
                    foreach (var p in paths)
                    {
                        if (File.Exists(p) && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && ExePayload.HasPayload(p)
                            && !decryptSources.Any(s => string.Equals(s, p, StringComparison.OrdinalIgnoreCase)))
                            decryptSources.Add(p);
                    }
                }
                foreach (var source in decryptSources)
                {
                    if (!File.Exists(source) && !Directory.Exists(source))
                    {
                        continue;
                    }

                    bool isDir = Directory.Exists(source);

                    // 已解密文件 / 目录智能跳过：
                    // - 覆盖模式：文件若无 WXENC001 头或目录内没有任何加密文件，则视为已解密，路径与状态不再修改；
                    // - 非覆盖模式：仅当 *_output 目录内还存在加密文件时才执行解密。
                    if (inPlace)
                    {
                        if (!isDir)
                        {
                            bool isWxEnc = CryptoService.IsWxEncryptedFile(source);
                            bool isExeWithPayload = Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase) && ExePayload.HasPayload(source);
                            if (!isWxEnc && !isExeWithPayload)
                            {
                                continue;
                            }
                        }
                        else
                        {
                            if (CountEncryptedFilesInFolder(source) == 0)
                            {
                                try
                                {
                                    if (!Directory.GetFiles(source, "*.exe").Any(p => ExePayload.HasPayload(p)))
                                        continue;
                                }
                                catch { continue; }
                            }
                        }
                    }
                    else
                    {
                        // 非覆盖模式下，若目录内既无 .enc 也无带载荷的 .exe，则跳过
                        if (isDir && CountEncryptedFilesInFolder(source) == 0)
                        {
                            try
                            {
                                if (!Directory.GetFiles(source, "*.exe").Any(p => ExePayload.HasPayload(p)))
                                    continue;
                            }
                            catch { continue; }
                        }
                    }

                    string outDir = inPlace
                        ? (isDir ? source : (Path.GetDirectoryName(source) ?? source))
                        : (isDir ? source : (Path.GetDirectoryName(source) ?? Environment.CurrentDirectory));

                    if (!inPlace && isDir)
                        isDir = true;

                    if (isDir)
                    {
                        try
                        {
                            foreach (var exePath in Directory.GetFiles(source, "*.exe"))
                            {
                                if (!ExePayload.HasPayload(exePath)) continue;
                                if (!ExePayload.TryReadPayload(exePath, out var meta, out var encBytes, out var errExe) || encBytes == null) { log($"封装EXE载荷读取失败: {exePath}" + (string.IsNullOrEmpty(errExe) ? "" : " (" + errExe + ")")); continue; }
                                var tmpEnc = Path.Combine(Path.GetTempPath(), "encryptTools_exe_dec_" + Guid.NewGuid().ToString("N") + ".enc");
                                await Compat.FileWriteAllBytesAsync(tmpEnc, encBytes);
                                var tmpOut = Path.Combine(outDir, "decrypt_" + Guid.NewGuid().ToString("N") + ".tmp");
                                CryptoService.DecryptResult result;
                                try
                                {
                                    var (peekAlg, peekName) = CryptoService.PeekEncryptedFileInfo(tmpEnc);
                                    if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && RuntimeHelper.IsNet8InstalledOnMachine)
                                    {
                                        bool ok = await GcmRunner.DecryptAsync(tmpEnc, tmpOut, password, null).ConfigureAwait(false);
                                        if (!ok) { log($"封装EXE解密失败: {exePath} - GCM 执行失败"); try { File.Delete(tmpEnc); } catch { } continue; }
                                        result = new CryptoService.DecryptResult { OriginalFileName = peekName };
                                    }
                                    else if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher)
                                    {
                                        log($"封装EXE解密失败: {exePath} - 载荷为 GCM 加密，本机未安装 .NET 8");
                                        try { File.Delete(tmpEnc); } catch { }
                                        continue;
                                    }
                                    else
                                    {
                                        var crypto = new CryptoService();
                                        result = await crypto.DecryptFileAsync(tmpEnc, tmpOut, password, null, CancellationToken.None);
                                    }
                                }
                                finally { try { File.Delete(tmpEnc); } catch { } }
                                var desiredName = string.IsNullOrWhiteSpace(result.OriginalFileName) ? Path.GetFileNameWithoutExtension(exePath) + "_decrypted" : SanitizeFileNameLocal(result.OriginalFileName);
                                var outPath = Path.Combine(outDir, desiredName);
                                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                                Compat.FileMoveOverwrite(tmpOut, outPath);
                                try { ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已解密(EXE): {outPath}{Environment.NewLine}"); } catch { }
                                UpdateFileListItemPathStatus(ctx.FileListView, exePath, outPath, "正常");
                                if (inPlace) { try { File.Delete(exePath); } catch { } }
                            }
                        }
                        catch (Exception ex) { log($"目录内封装EXE解密异常: {ex.Message}"); }
                    }

                    if (!isDir && Path.GetExtension(source).Equals(".exe", StringComparison.OrdinalIgnoreCase) && ExePayload.HasPayload(source))
                    {
                        try
                        {
                            if (!ExePayload.TryReadPayload(source, out var meta, out var encBytes, out var errExe) || encBytes == null)
                            {
                                log($"封装EXE载荷读取失败: {source}" + (string.IsNullOrEmpty(errExe) ? "" : " (" + errExe + ")"));
                                continue;
                            }
                            var tmpEnc = Path.Combine(Path.GetTempPath(), "encryptTools_exe_dec_" + Guid.NewGuid().ToString("N") + ".enc");
                            await Compat.FileWriteAllBytesAsync(tmpEnc, encBytes);
                            var tmpOut = Path.Combine(outDir, "decrypt_" + Guid.NewGuid().ToString("N") + ".tmp");
                            CryptoService.DecryptResult result;
                            try
                            {
                                var (peekAlg, peekName) = CryptoService.PeekEncryptedFileInfo(tmpEnc);
                                if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && RuntimeHelper.IsNet8InstalledOnMachine)
                                {
                                    bool ok = await GcmRunner.DecryptAsync(tmpEnc, tmpOut, password, null).ConfigureAwait(false);
                                    if (!ok) { log($"封装EXE解密失败: {source} - GCM 执行失败"); try { File.Delete(tmpEnc); } catch { } continue; }
                                    result = new CryptoService.DecryptResult { OriginalFileName = peekName };
                                }
                                else if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher)
                                {
                                    log($"封装EXE解密失败: {source} - 载荷为 GCM 加密，本机未安装 .NET 8");
                                    try { File.Delete(tmpEnc); } catch { }
                                    continue;
                                }
                                else
                                {
                                    var crypto = new CryptoService();
                                    result = await crypto.DecryptFileAsync(tmpEnc, tmpOut, password, null, CancellationToken.None);
                                }
                            }
                            finally { try { File.Delete(tmpEnc); } catch { } }
                            var desiredName = string.IsNullOrWhiteSpace(result.OriginalFileName) ? Path.GetFileNameWithoutExtension(source) + "_decrypted" : SanitizeFileNameLocal(result.OriginalFileName);
                            var outPath = Path.Combine(outDir, desiredName);
                            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                            Compat.FileMoveOverwrite(tmpOut, outPath);
                            try { ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已解密(EXE): {outPath}{Environment.NewLine}"); } catch { }
                            UpdateFileListItemPathStatus(ctx.FileListView, source, outPath, "正常");
                            if (inPlace) { try { File.Delete(source); } catch { } }
                        }
                        catch (Exception ex) { log($"封装EXE解密失败: {source} - {ex.Message}"); }
                        continue;
                    }

                    string? finalOut = null;
                    var interceptLog = new Action<string>(m =>
                    {
                        if (string.IsNullOrWhiteSpace(m)) return;

                        if (m.StartsWith("最终输出文件:", StringComparison.Ordinal))
                        {
                            finalOut = m.Substring("最终输出文件:".Length).Trim();
                            return;
                        }
                        if (m.StartsWith("解密:", StringComparison.Ordinal))
                        {
                            var i = m.IndexOf("->", StringComparison.Ordinal);
                            if (i > 0)
                            {
                                finalOut = m.Substring(i + 2).Trim();
                                var dest = finalOut;
                                if (!string.IsNullOrWhiteSpace(dest))
                                {
                                    try
                                    {
                                        void appendAndScroll()
                                        {
                                            ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已解密: {dest}{Environment.NewLine}");
                                            ctx.LogBox.SelectionStart = ctx.LogBox.Text.Length;
                                            ctx.LogBox.ScrollToCaret();
                                        }
                                        if (ctx.LogBox.InvokeRequired) ctx.LogBox.BeginInvoke(new Action(appendAndScroll)); else appendAndScroll();
                                    }
                                    catch { }
                                }
                            }
                            return;
                        }
                        // 其它内部日志全部忽略
                    });
                    var options = new FileEncryptorOptions
                    {
                        SourcePath = source,
                        OutputRoot = outDir,
                        InPlace = inPlace,
                        Recursive = isDir,
                        RandomizeFileName = ctx.ChkRandomFileName?.Checked ?? false,
                        Algorithm = CryptoAlgorithm.AesCbc,
                        Password = password,
                        Iterations = 200_000,
                        AesKeySizeBits = 256,
                        Log = interceptLog,
                        EncryptedExtension = encryptedExt
                    };
                    var enc = new FileEncryptor(options);
                    UpdateFileListProgress(ctx.FileListView, source, 0, isDecrypt: true);
                    var decryptProgress = CreateFileListProgress(ctx.FileListView, source, isDecrypt: true);
                    // 解密流程：读取加密文件并上报进度 → 写入明文；完成后将该项进度置为 100%（列表绘制为绿色）
                    await enc.DecryptAsync(decryptProgress, CancellationToken.None);
                    UpdateFileListProgress(ctx.FileListView, source, 100, isDecrypt: true);
                    var decName = DeriveDecryptedFileName(Path.GetFileName(source), encryptedExt);
                    string decPath;
                    if (finalOut != null)
                        decPath = isDir ? outDir : finalOut;
                    else if (!string.IsNullOrWhiteSpace(encryptedExt) && source.EndsWith(encryptedExt.Trim(), StringComparison.OrdinalIgnoreCase)
                        || source.EndsWith(".enc1", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".enc2", StringComparison.OrdinalIgnoreCase))
                        decPath = isDir ? outDir : Path.Combine(outDir, decName);
                    else
                        decPath = source;
                    if (!inPlace)
                        decPath = source;
                    UpdateFileListItemPathStatus(ctx.FileListView, source, decPath, "正常");
                }
                if (!inPlace && paths.Count > 0)
                {
                    foreach (var p in paths)
                        UpdateFileListItemPathStatus(ctx.FileListView, p, p, "已解密");
                }
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 解密完成。");
                _statusLeft.Text = "解密完成。";
            }
            catch (NotSupportedException ex) when (ex.Message != null && (ex.Message.Contains("AES-GCM") || ex.Message.Contains("需要")))
            {
                MessageBox.Show(RuntimeHelper.GetAesGcmRequirementMessage(), "需要 .NET 8", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 解密失败: {ex.Message}");
                _statusLeft.Text = "解密失败。";
            }
            catch (Exception ex)
            {
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 解密失败: {ex.Message}");
                _statusLeft.Text = "解密失败。";
            }
        }

        private static void AppendLogAndScroll(TextBoxBase logBox, string msg)
        {
            if (logBox == null) return;
            void doAppend()
            {
                logBox.AppendText(msg + Environment.NewLine);
                logBox.SelectionStart = logBox.Text.Length;
                logBox.ScrollToCaret();
            }
            if (logBox.InvokeRequired) logBox.BeginInvoke(new Action(doAppend)); else doAppend();
        }

        private static void AppendLogWithRed(TextBoxBase logBox, string msg)
        {
            const string redKeyword = "本机未安装 .NET 8";
            bool useRed = msg != null && msg.IndexOf(redKeyword, StringComparison.Ordinal) >= 0;
            if (logBox is RichTextBox rtb && useRed)
            {
                rtb.Select(rtb.TextLength, 0);
                rtb.SelectionColor = Color.Red;
                rtb.AppendText(msg + Environment.NewLine);
                rtb.SelectionColor = rtb.ForeColor;
            }
            else
            {
                logBox.AppendText(msg + Environment.NewLine);
            }
        }

        private static string DeriveDecryptedFileName(string encryptedName, string? encryptedExtension)
        {
            if (!string.IsNullOrWhiteSpace(encryptedExtension))
            {
                var ext = encryptedExtension.Trim();
                if (ext.Length > 0 && encryptedName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return encryptedName.Substring(0, encryptedName.Length - ext.Length);
            }
            if (encryptedName.EndsWith(".enc2", StringComparison.OrdinalIgnoreCase))
                return encryptedName.Substring(0, encryptedName.Length - 5);
            if (encryptedName.EndsWith(".enc1", StringComparison.OrdinalIgnoreCase))
                return encryptedName.Substring(0, encryptedName.Length - 5);
            return encryptedName;
        }

        private static string SanitizeFileNameLocal(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "output.bin";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in name) sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            var s = sb.ToString().TrimEnd(' ', '.');
            return string.IsNullOrWhiteSpace(s) ? "output.bin" : s;
        }

        private string PromptPassword()
        {
            using var dlg = new Form
            {
                Text = "输入密码",
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(360, 150),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false
            };
            var txt = new TextBox
            {
                UseSystemPasswordChar = true,
                Dock = DockStyle.Top,
                Margin = new Padding(10),
                Width = 320
            };
            var btnOk = new Button { Text = "确定", DialogResult = DialogResult.OK, Dock = DockStyle.Right, Width = 80 };
            var btnCancel = new Button { Text = "取消", DialogResult = DialogResult.Cancel, Dock = DockStyle.Left, Width = 80 };
            var panelButtons = new Panel { Dock = DockStyle.Bottom, Height = 40 };
            panelButtons.Controls.Add(btnOk);
            panelButtons.Controls.Add(btnCancel);

            dlg.Controls.Add(txt);
            dlg.Controls.Add(panelButtons);
            dlg.AcceptButton = btnOk;
            dlg.CancelButton = btnCancel;

            return dlg.ShowDialog(this) == DialogResult.OK ? txt.Text : string.Empty;
        }

        private void ShowNewPasswordFile()
        {
            PasswordFileService.EnsurePwdDirectory();
            using var dlg = new CreatePasswordFileForm(PasswordFileService.GetPwdDirectory());
            if (dlg.ShowDialog(this) == DialogResult.OK)
                RefreshAllFileWorkspacePwdCombos();
        }

        private void ShowImportPasswordFile()
        {
            PasswordFileService.EnsurePwdDirectory();
            using var dlg = new ImportPasswordFileForm(PasswordFileService.GetPwdDirectory());
            if (dlg.ShowDialog(this) == DialogResult.OK)
                RefreshAllFileWorkspacePwdCombos();
        }

        private void ShowEditPasswordFile()
        {
            PasswordFileService.EnsurePwdDirectory();
            using var dlg = new EditPasswordFileForm(PasswordFileService.GetPwdDirectory());
            dlg.ShowDialog(this);
        }

        private void OpenHelp()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://github.com/xiaowen1448/",
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private void OpenAbout()
        {
            MessageBox.Show(this, "encryptTools\n\n文件快速加密测试壳。", "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

