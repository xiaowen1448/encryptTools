
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
        private WinControlButton _btnMaxRestore = null!;
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
            // 中部就绪/状态文本（位于工作区中部的就绪文字区域），用于显示处理/总计文件数等
            public Label? CenterStatusLabel;
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
            Text = "encryptTools";
            FormBorderStyle = FormBorderStyle.None;
            DoubleBuffered = true;
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(950, 680);
            MinimumSize = new Size(900, 600);
            Font = new Font("Microsoft YaHei UI", 9F);

            // 顶部菜单栏（logo + 菜单 + 窗口按钮，全部在同一行）
            _menu = new MenuStrip
            {
                Dock = DockStyle.Top,
                Padding = new Padding(6, 2, 0, 2),
                BackColor = Color.FromArgb(240, 240, 240),
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            _menu.MouseDown += TitleBar_MouseDown;

            // Logo 图标（双击切换最大化/还原）
            var logoLabel = new ToolStripLabel { Padding = new Padding(2, 0, 12, 0) };
            try
            {
                var ico = LoadAppIcon();
                if (ico != null)
                    logoLabel.Image = ico.ToBitmap().GetThumbnailImage(22, 22, null, IntPtr.Zero) as Image;
            }
            catch { }
            logoLabel.DoubleClick += (_, __) => ToggleMaximize();
            _menu.Items.Add(logoLabel);

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

            // 窗口控制按钮（右对齐，自绘保证图标大小一致）
            var btnSize = new Size(46, 30);
            var btnMargin = new Padding(0);

            var btnClose = new WinControlButton(WinControlButton.ButtonKind.Close)
            {
                Alignment = ToolStripItemAlignment.Right,
                AutoSize = false,
                Size = btnSize,
                Margin = btnMargin
            };
            btnClose.Click += (_, __) => Close();

            _btnMaxRestore = new WinControlButton(WinControlButton.ButtonKind.Maximize)
            {
                Alignment = ToolStripItemAlignment.Right,
                AutoSize = false,
                Size = btnSize,
                Margin = btnMargin
            };
            _btnMaxRestore.Click += (_, __) => ToggleMaximize();

            var btnMin = new WinControlButton(WinControlButton.ButtonKind.Minimize)
            {
                Alignment = ToolStripItemAlignment.Right,
                AutoSize = false,
                Size = btnSize,
                Margin = btnMargin
            };
            btnMin.Click += (_, __) => { WindowState = FormWindowState.Minimized; };

            _menu.Items.Add(btnClose);
            _menu.Items.Add(_btnMaxRestore);
            _menu.Items.Add(btnMin);

            MainMenuStrip = _menu;
            Controls.Add(_menu);

            // 创建外部容器，无边框
            var contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control,
                Padding = new Padding(0)
            };

            // 中部：垂直 SplitContainer（上：工作区 Tab，下：日志区，均可拖拽调整高度）
            _vertSplit = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                SplitterWidth = 4,
                BackColor = Color.FromArgb(180, 180, 180)
            };
            _vertSplit.SplitterDistance = 420;
            
            // 两个面板都不添加边框，让分隔线作为视觉分隔
            _vertSplit.Panel1.Padding = Padding.Empty;
            _vertSplit.Panel1.BorderStyle = BorderStyle.None;
            _vertSplit.Panel1.BackColor = SystemColors.Control;
            
            _vertSplit.Panel2.Padding = Padding.Empty;
            _vertSplit.Panel2.BorderStyle = BorderStyle.None;
            _vertSplit.Panel2.BackColor = SystemColors.Control;

            // 上：TabControl 工作区
            _tabWorkspaces = new TabControl
            {
                Dock = DockStyle.Fill,
                Appearance = TabAppearance.Normal,
                Multiline = false,
                Alignment = TabAlignment.Top,
                HotTrack = true,
                Font = new Font("Microsoft YaHei UI", 9.5F)
            };
            _tabWorkspaces.Padding = new Point(12, 4);
            _tabWorkspaces.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabWorkspaces.SizeMode = TabSizeMode.Fixed;
            _tabWorkspaces.ItemSize = new Size(110, 30);
            _tabWorkspaces.DrawItem += TabWorkspaces_DrawItem;
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
                Font = new Font("Microsoft YaHei UI", 18f, FontStyle.Bold),
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
            var btnMachineCode = new Button
            {
                Text = "创建机器码加密工作区",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
                Location = new Point(46, 254)
            };
            btnFile.Click += (_, __) => NewWorkspace("文件");
            btnString.Click += (_, __) => NewWorkspace("字符串");
            btnImage.Click += (_, __) => NewWorkspace("图片");
            btnMachineCode.Click += (_, __) => NewWorkspace("机器码");
            welcomePanel.Controls.Add(title);
            welcomePanel.Controls.Add(subtitle);
            welcomePanel.Controls.Add(btnFile);
            welcomePanel.Controls.Add(btnString);
            welcomePanel.Controls.Add(btnImage);
            welcomePanel.Controls.Add(btnMachineCode);
            welcomeTab.Controls.Add(welcomePanel);
            _tabWorkspaces.TabPages.Add(welcomeTab);

            _tabWorkspaces.SelectedIndexChanged += TabWorkspaces_SelectedIndexChanged;
            _vertSplit.Panel1.Controls.Add(_tabWorkspaces);

            // 下：日志区域（当前选中工作区的独立日志 TextBox）
            _logHost = new Panel 
            { 
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
            };

            // 为欢迎页初始化一个日志框
            var welcomeLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
            };
            welcomeTab.Tag = new WorkspaceContext
            {
                Kind = "欢迎",
                SourcePath = null,
                LogBox = welcomeLog
            };
            _logHost.Controls.Add(welcomeLog);

            _vertSplit.Panel2.Controls.Add(_logHost);

            // 将 SplitContainer 添加到外部容器
            contentPanel.Controls.Add(_vertSplit);

            // 底部状态栏
            _status = new StatusStrip
            {
                Dock = DockStyle.Bottom
            };
            _statusLeft = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusRight = new ToolStripStatusLabel("新建文件工作区") { IsLink = true };
            _statusRight.Click += (_, __) => NewWorkspace("文件");
            _status.Items.Add(_statusLeft);
            _status.Items.Add(_statusRight);

            SuspendLayout();
            Controls.Add(contentPanel);
            Controls.Add(_status);
            Controls.Add(_menu);
            ResumeLayout(true);

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
                case "机器码":
                    CreateMachineCodeWorkspace();
                    break;
                default:
                    CreateFileWorkspace();
                    break;
            }
        }

        private void CreateFileWorkspace()
        {
            var index = _tabWorkspaces.TabPages.Count;
            var tab = new TabPage($"文件工作区 {index}")
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
            // 后缀名下拉选择（支持手动输入，但使用 Flat 样式避免蓝色编辑UI）
            var cbSuffix = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(2, 2, 8, 2),
                MinimumSize = new Size(70, 0),
                MaximumSize = new Size(comboMaxW, 0),
                Width = 80
            };
            // 常用后缀列表
            cbSuffix.Items.AddRange(new object[]
            {
                ".aes",
                ".enc",
                ".enc1",
                ".enc2",
                ".bin",
                ".dat",
                ".secure"
            });
            cbSuffix.SelectedItem = ".aes";
            SetComboDropDownWidth(cbSuffix, comboMaxW);
            var lblPwd = new Label { Text = "密码文件:", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            var cbPwdFile = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(2, 2, 8, 2), MinimumSize = new Size(100, 0), MaximumSize = new Size(comboMaxW, 0), Width = 120 };
            cbPwdFile.DropDown += (_, __) => SetComboDropDownWidth(cbPwdFile, comboMaxW);
            RefreshFileWorkspacePwdCombo(cbPwdFile);
            var chkSelfExe = new CheckBox { Text = "加密为可运行的exe", AutoSize = true, Margin = new Padding(8, 4, 4, 2) };
            var chkOverwrite = new CheckBox { Text = "覆盖原文件", AutoSize = true, Margin = new Padding(0, 4, 4, 2), Checked = true };
            var chkRandomFileName = new CheckBox { Text = "随机文件名", AutoSize = true, Margin = new Padding(0, 4, 12, 2), Checked = false };
            var btnEncrypt = new Button { Text = "执行加密",  AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnDecrypt = new Button { Text = "执行解密",  AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
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
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
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
            // 将底部中部的就绪 Label 引用保存到上下文中，便于执行时展示每个工作区的处理进度文本
            ctx.CenterStatusLabel = lblStatus;
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
            var btnEncrypt = new Button { Text = "执行加密",  AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnDecrypt = new Button { Text = "执行解密",  AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
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

            var logBox = new TextBox 
            { 
                Multiline = true, 
                ReadOnly = true, 
                ScrollBars = ScrollBars.Vertical, 
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
            };
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

            var logBox = new TextBox 
            { 
                Multiline = true, 
                ReadOnly = true, 
                ScrollBars = ScrollBars.Vertical, 
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
            };
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

        private void CreateMachineCodeWorkspace()
        {
            var index = _tabWorkspaces.TabPages.Count;
            var tab = new TabPage($"机器码工作区 {index}")
            {
                BackColor = SystemColors.Control
            };

            // 简化布局：只有一行工具栏 + 程序列表
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));       // 工具栏
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 主区域（程序预览）
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // 底部状态条

            // 工具栏：选择程序 + 图标选择 + 加密按钮 + 清空按钮
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = SystemColors.ControlLight,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoScroll = false,
                Padding = new Padding(6, 4, 6, 4)
            };

            var btnSelectProgram = new Button { Text = "选择程序", AutoSize = true, Margin = new Padding(0, 0, 6, 2) };
            var lblDragHint = new Label { Text = "可拖拽EXE文件", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 12, 0) };
            
            // 图标选择区域
            var lblIcon = new Label { Text = "程序图标:", AutoSize = true, Margin = new Padding(4, 6, 2, 0) };
            
            // 图标预览框
            var picIconPreview = new PictureBox
            {
                Width = 32,
                Height = 32,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                Margin = new Padding(2, 2, 4, 2),
                BackColor = Color.White
            };
            
            // 图标选择下拉框
            var cmbIconSource = new ComboBox
            {
                Width = 120,
                Margin = new Padding(2, 4, 4, 2),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbIconSource.Items.Add("使用原图标");
            cmbIconSource.Items.Add("导入图标");
            cmbIconSource.SelectedIndex = 0;
            
            // 导入图标按钮
            var btnImportIcon = new Button
            {
                Text = "浏览...",
                AutoSize = true,
                Margin = new Padding(2, 2, 8, 2),
                Enabled = false
            };

            // 加密按钮
            var btnEncrypt = new Button 
            { 
                Text = "执行加密", 
                AutoSize = true, 
                Margin = new Padding(8, 0, 4, 4),
                BackColor = Color.FromArgb(0x4C, 0xAF, 0x50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnEncrypt.FlatAppearance.BorderSize = 0;

            var btnClear = new Button { Text = "清空", AutoSize = true, Margin = new Padding(4, 0, 4, 4) };

            toolbar.Controls.Add(btnSelectProgram);
            toolbar.Controls.Add(lblDragHint);
            toolbar.Controls.Add(lblIcon);
            toolbar.Controls.Add(picIconPreview);
            toolbar.Controls.Add(cmbIconSource);
            toolbar.Controls.Add(btnImportIcon);
            toolbar.Controls.Add(btnEncrypt);
            toolbar.Controls.Add(btnClear);

            // 中部：程序预览列表（简化版，去掉机器码列）
            var lvPrograms = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                AllowDrop = true,
                OwnerDraw = true
            };
            lvPrograms.DrawColumnHeader += (s, e) => e.DrawDefault = true;
            lvPrograms.DrawSubItem += (s, e) =>
            {
                if (e.ColumnIndex != 3) { e.DrawDefault = true; return; }
                int raw = e.Item?.Tag is int v ? v : -1;
                var r = e.Bounds;
                if (r.Width <= 0 || r.Height <= 0) return;
                e.Graphics.FillRectangle(SystemBrushes.Window, r);
                
                string text;
                Color barColor;
                int barW = 0;
                
                if (raw < 0)
                {
                    text = "-";
                    barColor = Color.Gray;
                }
                else if (raw == 0)
                {
                    text = "待加密";
                    barColor = Color.Gray;
                }
                else if (raw == 100)
                {
                    text = "已加密";
                    barColor = Color.FromArgb(0x4C, 0xAF, 0x50); // 绿色
                    barW = r.Width - 4;
                }
                else
                {
                    text = "加密中";
                    barColor = Color.FromArgb(0xD3, 0x32, 0x2F); // 红色
                    barW = (int)((r.Width - 4) * raw / 100.0);
                }
                
                if (barW > 0)
                {
                    using (var brush = new SolidBrush(barColor))
                    {
                        var barRect = new Rectangle(r.X + 2, r.Y + 2, barW, r.Height - 4);
                        e.Graphics.FillRectangle(brush, barRect);
                    }
                }
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                e.Graphics.DrawString(text, e.Item?.ListView?.Font ?? SystemFonts.DefaultFont, SystemBrushes.ControlText, r, sf);
            };
            lvPrograms.Columns.Add("名称", 160);
            lvPrograms.Columns.Add("路径", 380);
            lvPrograms.Columns.Add("大小", 80);
            lvPrograms.Columns.Add("状态", 100);

            // 数据存储
            string selectedProgramPath = "";
            string selectedIconPath = "";
            string originalIconPath = "";

            // 初始化状态显示
          //  lblKeyStatus.Text = "已准备";
            //lblLicStatus.Text = "已准备";

            // 事件处理
            btnSelectProgram.Click += (_, __) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "选择要加密的程序",
                    Filter = "可执行文件 (*.exe)|*.exe"
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    selectedProgramPath = dlg.FileName;
                    var fi = new FileInfo(selectedProgramPath);
                    var item = new ListViewItem(new[]
                    {
                        Path.GetFileName(selectedProgramPath),
                        selectedProgramPath,
                        $"{fi.Length / 1024} KB",
                        "待加密"
                    });
                    item.Tag = 0;
                    lvPrograms.Items.Clear();
                    lvPrograms.Items.Add(item);
                    
                    // 提取并显示原程序图标
                    try
                    {
                        originalIconPath = ExtractIconFromExe(selectedProgramPath);
                        if (!string.IsNullOrEmpty(originalIconPath) && File.Exists(originalIconPath))
                        {
                            using (var icon = Image.FromFile(originalIconPath))
                            {
                                picIconPreview.Image = new Bitmap(icon, 32, 32);
                            }
                            selectedIconPath = originalIconPath;
                        }
                    }
                    catch (Exception ex)
                    {
                        // 暂时不记录日志，ctx还未声明
                        System.Diagnostics.Debug.WriteLine($"提取图标失败: {ex.Message}");
                    }
                }
            };

            lvPrograms.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };

            lvPrograms.DragDrop += (s, e) =>
            {
                if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    var exePath = paths.FirstOrDefault(p => Path.GetExtension(p).Equals(".exe", StringComparison.OrdinalIgnoreCase));
                    if (exePath != null)
                    {
                        selectedProgramPath = exePath;
                        var fi = new FileInfo(selectedProgramPath);
                        var item = new ListViewItem(new[]
                        {
                            Path.GetFileName(selectedProgramPath),
                            selectedProgramPath,
                            $"{fi.Length / 1024} KB",
                            "待加密"
                        });
                        item.Tag = 0;
                        lvPrograms.Items.Clear();
                        lvPrograms.Items.Add(item);
                        
                        // 提取并显示原程序图标
                        try
                        {
                            originalIconPath = ExtractIconFromExe(selectedProgramPath);
                            if (!string.IsNullOrEmpty(originalIconPath) && File.Exists(originalIconPath))
                            {
                                using (var icon = Image.FromFile(originalIconPath))
                                {
                                    picIconPreview.Image = new Bitmap(icon, 32, 32);
                                }
                                selectedIconPath = originalIconPath;
                            }
                        }
                        catch (Exception ex)
                        {
                            // 暂时不记录日志，ctx还未声明
                            System.Diagnostics.Debug.WriteLine($"提取图标失败: {ex.Message}");
                        }
                    }
                }
            };

            btnClear.Click += (_, __) =>
            {
                lvPrograms.Items.Clear();
                selectedProgramPath = "";
                selectedIconPath = "";
                originalIconPath = "";
                picIconPreview.Image = null;
                cmbIconSource.SelectedIndex = 0;
                btnImportIcon.Enabled = false;
            };
            
            // 图标选择下拉框事件
            cmbIconSource.SelectedIndexChanged += (_, __) =>
            {
                if (cmbIconSource.SelectedIndex == 0)
                {
                    // 使用原图标
                    btnImportIcon.Enabled = false;
                    if (!string.IsNullOrEmpty(originalIconPath) && File.Exists(originalIconPath))
                    {
                        try
                        {
                            using (var icon = Image.FromFile(originalIconPath))
                            {
                                picIconPreview.Image = new Bitmap(icon, 32, 32);
                            }
                            selectedIconPath = originalIconPath;
                        }
                        catch { }
                    }
                }
                else
                {
                    // 导入图标
                    btnImportIcon.Enabled = true;
                    if (!string.IsNullOrEmpty(selectedIconPath) && selectedIconPath != originalIconPath)
                    {
                        try
                        {
                            using (var icon = Image.FromFile(selectedIconPath))
                            {
                                picIconPreview.Image = new Bitmap(icon, 32, 32);
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        picIconPreview.Image = null;
                    }
                }
            };
            
            // 导入图标按钮事件
            btnImportIcon.Click += (_, __) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "选择图标文件",
                    Filter = "图标文件 (*.ico)|*.ico|所有图片 (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.bmp"
                };
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    try
                    {
                        selectedIconPath = dlg.FileName;
                        using (var icon = Image.FromFile(selectedIconPath))
                        {
                            picIconPreview.Image = new Bitmap(icon, 32, 32);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"加载图标失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
            };

            // 底部状态
            var bottomStatus = new Panel { Dock = DockStyle.Fill };
            var lblStatus = new Label { Text = "就绪", Dock = DockStyle.Left, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Width = 300 };
            bottomStatus.Controls.Add(lblStatus);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(lvPrograms, 0, 1);
            root.Controls.Add(bottomStatus, 0, 2);

            tab.Controls.Add(root);

            var logBox = new RichTextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = RichTextBoxScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                BackColor = SystemColors.Control
            };

            var ctx = new WorkspaceContext
            {
                Kind = "机器码",
                SourcePath = null,
                LogBox = logBox,
                FileListView = lvPrograms,
                CenterStatusLabel = lblStatus
            };
            tab.Tag = ctx;

            // 捕获ctx变量用于lambda表达式
            var capturedCtx = ctx;

            // 加密按钮点击事件处理程序 - 简化版：直接加密，不依赖外部key/lic
            btnEncrypt.Click += async (_, __) =>
            {
                if (string.IsNullOrEmpty(selectedProgramPath))
                {
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误: 请先选择程序{Environment.NewLine}");
                    return;
                }

                // 备份原文件
                string backupPath = selectedProgramPath + ".bak";
                try
                {
                    if (File.Exists(backupPath)) File.Delete(backupPath);
                    File.Copy(selectedProgramPath, backupPath);
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已备份原程序到: {backupPath}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误: 备份失败: {ex.Message}{Environment.NewLine}");
                    return;
                }

                // 执行加密
                if (lvPrograms.Items.Count > 0)
                {
                    lvPrograms.Items[0].Tag = 50; // 加密中
                    lvPrograms.Invalidate();
                }

                capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 开始加密程序...{Environment.NewLine}");

                try
                {
                    // 创建加密后的程序（包装器）- 使用内置的RSA密钥对
                    await CreateProtectedExecutableSimple(selectedProgramPath, selectedIconPath, capturedCtx);

                    if (lvPrograms.Items.Count > 0)
                    {
                        lvPrograms.Items[0].Tag = 100; // 已加密
                        lvPrograms.Items[0].SubItems[3].Text = "已加密";
                        lvPrograms.Invalidate();
                    }

                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 加密完成!{Environment.NewLine}");
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 原程序已备份为: {backupPath}{Environment.NewLine}");
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 加密后的程序特性：{Environment.NewLine}");
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] • 启动时自动读取机器码{Environment.NewLine}");
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] • 需要卡密激活才能运行{Environment.NewLine}");
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] • 一机一码，防复制防泄密{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    capturedCtx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 错误: 加密失败: {ex.Message}{Environment.NewLine}");
                    if (lvPrograms.Items.Count > 0)
                    {
                        lvPrograms.Items[0].Tag = 0;
                        lvPrograms.Invalidate();
                    }
                }
            };

            _tabWorkspaces.TabPages.Add(tab);
            _tabWorkspaces.SelectedTab = tab;
            _logHost.Controls.Clear();
            _logHost.Controls.Add(logBox);
            _statusLeft.Text = "已创建新工作区：机器码加密";
        }

        private void GenerateLic(string machineCode, string daysText, string keyDirectory, string licDirectory, ref string generatedLicPath)
        {
            try
            {
                if (!int.TryParse(daysText, out int licenseDays))
                    licenseDays = 365;

                string fileName = $"license_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8)}.lic";
                string licPath = Path.Combine(licDirectory, fileName);

                var licenseManager = LicenseManager.FromKeyDirectory(keyDirectory);
                var lic = licenseManager.CreateLicense(machineCode, licenseDays);
                licenseManager.SaveLicense(lic, licPath);

                generatedLicPath = licPath;
            }
            catch { }
        }

        /// <summary>
        /// 创建受保护的程序（简化版）- 内置RSA密钥对，运行时读取机器码，需要卡密激活
        /// </summary>
        private async Task CreateProtectedExecutableSimple(string originalExePath, string iconPath, WorkspaceContext ctx)
        {
            await Task.Run(() =>
            {
                // 读取原程序内容
                byte[] originalExeBytes = File.ReadAllBytes(originalExePath);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已读取原程序: {originalExePath} (大小: {originalExeBytes.Length} 字节){Environment.NewLine}");

                // 生成内置的RSA密钥对
                string publicKeyXml;
                string privateKeyXml;
                using (var rsa = RSA.Create(2048))
                {
                    publicKeyXml = rsa.ToXmlString(false);
                    privateKeyXml = rsa.ToXmlString(true);
                }
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已生成RSA密钥对{Environment.NewLine}");

                // 创建包装器程序代码 - 包含防复制和防泄密功能
                string wrapperCode = GenerateProtectedWrapperCode(publicKeyXml, privateKeyXml, originalExeBytes);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已生成保护包装器代码 (大小: {wrapperCode.Length} 字符){Environment.NewLine}");

                // 保存包装器代码到临时文件
                string tempDir = Path.Combine(Path.GetTempPath(), $"EncryptTools_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已创建临时目录: {tempDir}{Environment.NewLine}");

                string wrapperCsPath = Path.Combine(tempDir, "Program.cs");
                File.WriteAllText(wrapperCsPath, wrapperCode);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已保存包装器代码到: {wrapperCsPath}{Environment.NewLine}");

                // 创建项目文件
                string projContent = "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                    "  <PropertyGroup>\n" +
                    "    <OutputType>WinExe</OutputType>\n" +
                    "    <TargetFramework>net48</TargetFramework>\n" +
                    "    <UseWindowsForms>true</UseWindowsForms>\n" +
                    "    <RuntimeIdentifier>win-x64</RuntimeIdentifier>\n";

                // 如果有图标，添加到项目文件
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    string iconFileName = "app.ico";
                    string tempIconPath = Path.Combine(tempDir, iconFileName);
                    File.Copy(iconPath, tempIconPath, true);
                    projContent += "    <ApplicationIcon>" + iconFileName + "</ApplicationIcon>\n";
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已添加程序图标{Environment.NewLine}");
                }

                projContent += "  </PropertyGroup>\n" +
                    "  <ItemGroup>\n" +
                    "    <Reference Include=\"System.Management\" />\n" +
                    "    <Reference Include=\"System.Web.Extensions\" />\n" +
                    "  </ItemGroup>\n" +
                    "</Project>";
                File.WriteAllText(Path.Combine(tempDir, "Wrapper.csproj"), projContent);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已创建项目文件{Environment.NewLine}");

                // 编译包装器程序
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{tempDir}\" -c Release -o \"{tempDir}\\publish\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 开始编译包装器程序...{Environment.NewLine}");
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 执行命令: {psi.FileName} {psi.Arguments}{Environment.NewLine}");

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                    throw new Exception("无法启动编译进程");

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    string output = process.StandardOutput.ReadToEnd();
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 编译错误输出: {error}{Environment.NewLine}");
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 编译标准输出: {output}{Environment.NewLine}");
                    throw new Exception($"编译失败 (退出代码: {process.ExitCode}): {error}");
                }

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 编译成功{Environment.NewLine}");

                // 复制编译后的程序到原程序位置
                string compiledExe = Path.Combine(tempDir, "publish", "Wrapper.exe");
                if (!File.Exists(compiledExe))
                    throw new Exception($"编译后的程序不存在: {compiledExe}");

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已找到编译后的程序: {compiledExe}{Environment.NewLine}");

                // 替换原程序
                File.Copy(compiledExe, originalExePath, true);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已替换原程序: {originalExePath}{Environment.NewLine}");

                // 清理临时文件
                try { Directory.Delete(tempDir, true); } catch { }
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已清理临时文件{Environment.NewLine}");
            });
        }

        /// <summary>
        /// 创建受保护的程序（包装器）
        /// </summary>
        private async Task CreateProtectedExecutable(string originalExePath, string machineCode, string licPath, string keyPath, string iconPath, WorkspaceContext ctx)
        {
            await Task.Run(() =>
            {
                // 读取原程序内容
                byte[] originalExeBytes = File.ReadAllBytes(originalExePath);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已读取原程序: {originalExePath} (大小: {originalExeBytes.Length} 字节){Environment.NewLine}");

                // 读取lic文件内容
                byte[] licBytes = File.ReadAllBytes(licPath);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已读取lic文件: {licPath} (大小: {licBytes.Length} 字节){Environment.NewLine}");

                // 读取公钥
                string publicKeyPath = Path.Combine(Path.GetDirectoryName(keyPath)!, Path.GetFileNameWithoutExtension(keyPath) + ".pub");
                byte[] publicKeyBytes = File.ReadAllBytes(publicKeyPath);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已读取公钥文件: {publicKeyPath} (大小: {publicKeyBytes.Length} 字节){Environment.NewLine}");

                // 创建包装器程序代码
                string wrapperCode = GenerateWrapperCode(machineCode, licBytes, publicKeyBytes, originalExeBytes);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已生成包装器代码 (大小: {wrapperCode.Length} 字符){Environment.NewLine}");

                // 保存包装器代码到临时文件
                string tempDir = Path.Combine(Path.GetTempPath(), $"EncryptTools_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已创建临时目录: {tempDir}{Environment.NewLine}");

                string wrapperCsPath = Path.Combine(tempDir, "Program.cs");
                File.WriteAllText(wrapperCsPath, wrapperCode);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已保存包装器代码到: {wrapperCsPath}{Environment.NewLine}");

                // 创建项目文件 - 使用.NET 4.6，依赖原程序目录中的DLL
                string projContent = "<Project Sdk=\"Microsoft.NET.Sdk\">\n" +
                    "  <PropertyGroup>\n" +
                    "    <OutputType>WinExe</OutputType>\n" +
                    "    <TargetFramework>net48</TargetFramework>\n" +
                    "    <UseWindowsForms>true</UseWindowsForms>\n" +
                    "    <RuntimeIdentifier>win-x64</RuntimeIdentifier>\n";
                
                // 如果有图标，添加到项目文件
                if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
                {
                    // 复制图标到临时目录
                    string iconFileName = "app.ico";
                    string tempIconPath = Path.Combine(tempDir, iconFileName);
                    File.Copy(iconPath, tempIconPath, true);
                    projContent += "    <ApplicationIcon>" + iconFileName + "</ApplicationIcon>\n";
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已添加程序图标{Environment.NewLine}");
                }
                
                projContent += "  </PropertyGroup>\n" +
                    "  <ItemGroup>\n" +
                    "    <Reference Include=\"System.Management\" />\n" +
                    "  </ItemGroup>\n" +
                    "</Project>";
                File.WriteAllText(Path.Combine(tempDir, "Wrapper.csproj"), projContent);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已创建项目文件{Environment.NewLine}");

                // 编译包装器程序
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"build \"{tempDir}\" -c Release -o \"{tempDir}\\publish\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 开始编译包装器程序...{Environment.NewLine}");
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 执行命令: {psi.FileName} {psi.Arguments}{Environment.NewLine}");

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null)
                    throw new Exception("无法启动编译进程");

                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    string error = process.StandardError.ReadToEnd();
                    string output = process.StandardOutput.ReadToEnd();
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 编译错误输出: {error}{Environment.NewLine}");
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 编译标准输出: {output}{Environment.NewLine}");
                    throw new Exception($"编译失败 (退出代码: {process.ExitCode}): {error}");
                }

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 编译成功{Environment.NewLine}");

                // 复制编译后的程序到原程序位置
                string compiledExe = Path.Combine(tempDir, "publish", "Wrapper.exe");
                if (!File.Exists(compiledExe))
                    throw new Exception($"编译后的程序不存在: {compiledExe}");

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已找到编译后的程序: {compiledExe}{Environment.NewLine}");

                // 替换原程序
                File.Copy(compiledExe, originalExePath, true);
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已替换原程序: {originalExePath}{Environment.NewLine}");

                // 清理临时文件
                try { Directory.Delete(tempDir, true); } catch { }
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已清理临时文件{Environment.NewLine}");
            });
        }

        /// <summary>
        /// 生成受保护的包装器程序代码 - 包含防复制和防泄密功能
        /// 运行时读取机器码，需要卡密激活
        /// </summary>
        private string GenerateProtectedWrapperCode(string publicKeyXml, string privateKeyXml, byte[] originalExeBytes)
        {
            string publicKeyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(publicKeyXml));
            string privateKeyBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(privateKeyXml));
            string originalExeBase64 = Convert.ToBase64String(originalExeBytes);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.IO;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using System.Management;");
            sb.AppendLine("using System.Security.Cryptography;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using System.Text.Json;");
            sb.AppendLine("using System.Windows.Forms;");
            sb.AppendLine("");
            sb.AppendLine("namespace Wrapper");
            sb.AppendLine("{");
            sb.AppendLine("    public class Program");
            sb.AppendLine("    {");
            sb.AppendLine($"        private static readonly string EmbeddedPublicKey = \"{publicKeyBase64}\";");
            sb.AppendLine($"        private static readonly string EmbeddedPrivateKey = \"{privateKeyBase64}\";");
            sb.AppendLine($"        private static readonly string EmbeddedOriginalExe = \"{originalExeBase64}\";");
            sb.AppendLine("        private static string _logFilePath = null;");
            sb.AppendLine("        private static string _machineCode = null;");
            sb.AppendLine("");
            sb.AppendLine("        // 防复制：检测是否被复制到其他机器");
            sb.AppendLine("        private static bool IsCopiedToAnotherMachine()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string configDir = Path.Combine(appDir, \"config\");");
            sb.AppendLine("                string machineFile = Path.Combine(configDir, \".sys\");");
            sb.AppendLine("                string currentMachineCode = GetMachineCode();");
            sb.AppendLine("");
            sb.AppendLine("                if (!File.Exists(machineFile))");
            sb.AppendLine("                {");
            sb.AppendLine("                    // 首次运行，保存机器码");
            sb.AppendLine("                    Directory.CreateDirectory(configDir);");
            sb.AppendLine("                    File.WriteAllText(machineFile, EncryptString(currentMachineCode, currentMachineCode));");
            sb.AppendLine("                    return false;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 读取保存的机器码并解密");
            sb.AppendLine("                string savedEncrypted = File.ReadAllText(machineFile);");
            sb.AppendLine("                string savedMachineCode = DecryptString(savedEncrypted, currentMachineCode);");
            sb.AppendLine("");
            sb.AppendLine("                // 比较机器码");
            sb.AppendLine("                return !string.Equals(savedMachineCode, currentMachineCode, StringComparison.OrdinalIgnoreCase);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                return true; // 出错视为复制");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        // 防泄密：检测调试器和虚拟机");
            sb.AppendLine("        private static bool IsBeingDebugged()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                // 检测调试器");
            sb.AppendLine("                if (System.Diagnostics.Debugger.IsAttached)");
            sb.AppendLine("                    return true;");
            sb.AppendLine("");
            sb.AppendLine("                // 检测常见虚拟机");
            sb.AppendLine("                string[] vmIndicators = new[] { \"vmware\", \"virtualbox\", \"hyper-v\", \"xen\", \"qemu\" };");
            sb.AppendLine("                string manufacturer = GetWmi(\"Win32_ComputerSystem\", \"Manufacturer\").ToLower();");
            sb.AppendLine("                string model = GetWmi(\"Win32_ComputerSystem\", \"Model\").ToLower();");
            sb.AppendLine("                foreach (var vm in vmIndicators)");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (manufacturer.Contains(vm) || model.Contains(vm))");
            sb.AppendLine("                        return true;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static void InitLog()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string logDir = Path.Combine(appDir, \"log\");");
            sb.AppendLine("                if (!Directory.Exists(logDir))");
            sb.AppendLine("                    Directory.CreateDirectory(logDir);");
            sb.AppendLine("                string timestamp = DateTime.Now.ToString(\"yyyyMMdd_HHmmss\");");
            sb.AppendLine("                _logFilePath = Path.Combine(logDir, $\"wrapper_{timestamp}.log\");");
            sb.AppendLine("                File.WriteAllText(_logFilePath, $\"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 日志初始化\\r\\n\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { _logFilePath = null; }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static void Log(string message)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_logFilePath != null)");
            sb.AppendLine("                    File.AppendAllText(_logFilePath, $\"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\\r\\n\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        [STAThread]");
            sb.AppendLine("        public static void Main()");
            sb.AppendLine("        {");
            sb.AppendLine("            Application.EnableVisualStyles();");
            sb.AppendLine("            Application.SetCompatibleTextRenderingDefault(false);");
            sb.AppendLine("            InitLog();");
            sb.AppendLine("            Log(\"程序启动\");");
            sb.AppendLine("");
            sb.AppendLine("            // 防泄密检测");
            sb.AppendLine("            if (IsBeingDebugged())");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"检测到调试器或虚拟机，退出\");");
            sb.AppendLine("                MessageBox.Show(\"程序无法在调试器或虚拟机中运行。\", \"安全检测\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("");
            sb.AppendLine("            // 防复制检测");
            sb.AppendLine("            if (IsCopiedToAnotherMachine())");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"检测到程序被复制到其他机器\");");
            sb.AppendLine("                MessageBox.Show(\"此程序已绑定到其他机器，无法在此设备上运行。\\n\\n请使用正确的授权。\", \"授权验证失败\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("");
            sb.AppendLine("            // 获取当前机器码");
            sb.AppendLine("            _machineCode = GetMachineCode();");
            sb.AppendLine("            Log($\"当前机器码: {_machineCode}\");");
            sb.AppendLine("");
            sb.AppendLine("            // 检查是否已激活");
            sb.AppendLine("            if (!IsActivated())");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"未激活，显示激活窗口\");");
            sb.AppendLine("                using (var activateForm = new ActivationForm(_machineCode, GetPublicKey()))");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (activateForm.ShowDialog() != DialogResult.OK)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Log(\"用户取消激活，退出\");");
            sb.AppendLine("                        return;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("                Log(\"激活成功\");");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"已激活，验证许可证\");");
            sb.AppendLine("                if (!ValidateLicense())");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"许可证验证失败\");");
            sb.AppendLine("                    MessageBox.Show(\"许可证已过期或无效，请重新激活。\", \"许可证验证失败\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    // 删除许可证，要求重新激活");
            sb.AppendLine("                    DeleteLicense();");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("                Log(\"许可证验证通过\");");
            sb.AppendLine("            }");
            sb.AppendLine("");
            sb.AppendLine("            // 启动原程序");
            sb.AppendLine("            Log(\"启动原程序\");");
            sb.AppendLine("            LaunchOriginalProgram();");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static string GetMachineCode()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string cpu = GetWmi(\"Win32_Processor\", \"ProcessorId\");");
            sb.AppendLine("                string board = GetWmi(\"Win32_BaseBoard\", \"SerialNumber\");");
            sb.AppendLine("                string disk = GetWmi(\"Win32_LogicalDisk\", \"VolumeSerialNumber\", \"DeviceID='C:'\");");
            sb.AppendLine("                string raw = $\"{cpu}|{board}|{disk}\";");
            sb.AppendLine("                return Sha256(raw);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                var fallback = Environment.MachineName + Environment.UserName;");
            sb.AppendLine("                using (var sha = SHA256.Create())");
            sb.AppendLine("                    return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(fallback))).Replace(\"-\", \"\");");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static string GetWmi(string cls, string prop, string where = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var q = where == null ? $\"SELECT {prop} FROM {cls}\" : $\"SELECT {prop} FROM {cls} WHERE {where}\";");
            sb.AppendLine("                using (var searcher = new ManagementObjectSearcher(q))");
            sb.AppendLine("                    foreach (ManagementObject mo in searcher.Get())");
            sb.AppendLine("                        return mo[prop]?.ToString()?.Trim();");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("            return \"\";");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static string Sha256(string s)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (var sha = SHA256.Create())");
            sb.AppendLine("                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? \"\"))).Replace(\"-\", \"\");");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static RSA GetPublicKey()");
            sb.AppendLine("        {");
            sb.AppendLine("            string keyXml = Encoding.UTF8.GetString(Convert.FromBase64String(EmbeddedPublicKey));");
            sb.AppendLine("            var rsa = RSA.Create();");
            sb.AppendLine("            rsa.FromXmlString(keyXml);");
            sb.AppendLine("            return rsa;");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static RSA GetPrivateKey()");
            sb.AppendLine("        {");
            sb.AppendLine("            string keyXml = Encoding.UTF8.GetString(Convert.FromBase64String(EmbeddedPrivateKey));");
            sb.AppendLine("            var rsa = RSA.Create();");
            sb.AppendLine("            rsa.FromXmlString(keyXml);");
            sb.AppendLine("            return rsa;");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static bool IsActivated()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string licPath = Path.Combine(appDir, \"config\", \"license.lic\");");
            sb.AppendLine("                return File.Exists(licPath);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { return false; }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static void DeleteLicense()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string licPath = Path.Combine(appDir, \"config\", \"license.lic\");");
            sb.AppendLine("                if (File.Exists(licPath)) File.Delete(licPath);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static bool ValidateLicense()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string licPath = Path.Combine(appDir, \"config\", \"license.lic\");");
            sb.AppendLine("                if (!File.Exists(licPath)) return false;");
            sb.AppendLine("");
            sb.AppendLine("                string licJson = File.ReadAllText(licPath);");
            sb.AppendLine("                var lic = ParseLicense(licJson);");
            sb.AppendLine("                if (lic == null) return false;");
            sb.AppendLine("");
            sb.AppendLine("                // 验证机器码匹配");
            sb.AppendLine("                if (!string.Equals(lic.machine, _machineCode, StringComparison.OrdinalIgnoreCase))");
            sb.AppendLine("                    return false;");
            sb.AppendLine("");
            sb.AppendLine("                // 验证签名");
            sb.AppendLine("                string signData = $\"{lic.machine}|{lic.days}|{lic.licId}\";");
            sb.AppendLine("                using (var rsa = GetPublicKey())");
            sb.AppendLine("                {");
            sb.AppendLine("                    byte[] data = Encoding.UTF8.GetBytes(signData);");
            sb.AppendLine("                    byte[] sign = Convert.FromBase64String(lic.sign);");
            sb.AppendLine("                    if (!rsa.VerifyData(data, sign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))");
            sb.AppendLine("                        return false;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 验证有效期");
            sb.AppendLine("                DateTime createTime = File.GetCreationTime(licPath);");
            sb.AppendLine("                DateTime expireDate = createTime.AddDays(int.Parse(lic.days));");
            sb.AppendLine("                return DateTime.Now <= expireDate;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { return false; }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static License ParseLicense(string json)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                return System.Text.Json.JsonSerializer.Deserialize<License>(json);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { return null; }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static string EncryptString(string plainText, string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (var aes = Aes.Create())");
            sb.AppendLine("            {");
            sb.AppendLine("                byte[] keyBytes = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));");
            sb.AppendLine("                aes.Key = keyBytes;");
            sb.AppendLine("                aes.GenerateIV();");
            sb.AppendLine("                using (var encryptor = aes.CreateEncryptor())");
            sb.AppendLine("                {");
            sb.AppendLine("                    byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);");
            sb.AppendLine("                    byte[] encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);");
            sb.AppendLine("                    return Convert.ToBase64String(aes.IV.Concat(encrypted).ToArray());");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static string DecryptString(string cipherText, string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                byte[] fullBytes = Convert.FromBase64String(cipherText);");
            sb.AppendLine("                byte[] iv = fullBytes.Take(16).ToArray();");
            sb.AppendLine("                byte[] encrypted = fullBytes.Skip(16).ToArray();");
            sb.AppendLine("                using (var aes = Aes.Create())");
            sb.AppendLine("                {");
            sb.AppendLine("                    byte[] keyBytes = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));");
            sb.AppendLine("                    aes.Key = keyBytes;");
            sb.AppendLine("                    aes.IV = iv;");
            sb.AppendLine("                    using (var decryptor = aes.CreateDecryptor())");
            sb.AppendLine("                    {");
            sb.AppendLine("                        byte[] decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);");
            sb.AppendLine("                        return Encoding.UTF8.GetString(decrypted);");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { return null; }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static void LaunchOriginalProgram()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string tempDir = Path.Combine(Path.GetTempPath(), $\"ProtectedApp_{Guid.NewGuid():N}\");");
            sb.AppendLine("                Directory.CreateDirectory(tempDir);");
            sb.AppendLine("");
            sb.AppendLine("                // 解密并保存原程序");
            sb.AppendLine("                byte[] exeBytes = Convert.FromBase64String(EmbeddedOriginalExe);");
            sb.AppendLine("                string exePath = Path.Combine(tempDir, \"Original.exe\");");
            sb.AppendLine("                File.WriteAllBytes(exePath, exeBytes);");
            sb.AppendLine("");
            sb.AppendLine("                // 启动原程序");
            sb.AppendLine("                var psi = new System.Diagnostics.ProcessStartInfo");
            sb.AppendLine("                {");
            sb.AppendLine("                    FileName = exePath,");
            sb.AppendLine("                    WorkingDirectory = tempDir,");
            sb.AppendLine("                    UseShellExecute = false");
            sb.AppendLine("                };");
            sb.AppendLine("                var process = System.Diagnostics.Process.Start(psi);");
            sb.AppendLine("                if (process != null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    process.WaitForExit();");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 清理临时文件");
            sb.AppendLine("                try { Directory.Delete(tempDir, true); } catch { }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Log($\"启动原程序失败: {ex.Message}\");");
            sb.AppendLine("                MessageBox.Show($\"启动失败: {ex.Message}\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    public class License");
            sb.AppendLine("    {");
            sb.AppendLine("        public string machine { get; set; }");
            sb.AppendLine("        public string days { get; set; }");
            sb.AppendLine("        public string licId { get; set; }");
            sb.AppendLine("        public string sign { get; set; }");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    // 激活窗口");
            sb.AppendLine("    public class ActivationForm : Form");
            sb.AppendLine("    {");
            sb.AppendLine("        private string _machineCode;");
            sb.AppendLine("        private RSA _publicKey;");
            sb.AppendLine("        private TextBox _txtCardKey;");
            sb.AppendLine("");
            sb.AppendLine("        public ActivationForm(string machineCode, RSA publicKey)");
            sb.AppendLine("        {");
            sb.AppendLine("            _machineCode = machineCode;");
            sb.AppendLine("            _publicKey = publicKey;");
            sb.AppendLine("            InitializeComponent();");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private void InitializeComponent()");
            sb.AppendLine("        {");
            sb.AppendLine("            Text = \"软件激活\";");
            sb.AppendLine("            Size = new System.Drawing.Size(500, 280);");
            sb.AppendLine("            FormBorderStyle = FormBorderStyle.FixedDialog;");
            sb.AppendLine("            MaximizeBox = false;");
            sb.AppendLine("            MinimizeBox = false;");
            sb.AppendLine("            StartPosition = FormStartPosition.CenterScreen;");
            sb.AppendLine("");
            sb.AppendLine("            var lblInfo = new Label");
            sb.AppendLine("            {");
            sb.AppendLine("                Text = \"请输入卡密进行激活：\",");
            sb.AppendLine("                Location = new System.Drawing.Point(20, 20),");
            sb.AppendLine("                AutoSize = true");
            sb.AppendLine("            };");
            sb.AppendLine("");
            sb.AppendLine("            var lblMachine = new Label");
            sb.AppendLine("            {");
            sb.AppendLine("                Text = $\"机器码: {_machineCode}\",");
            sb.AppendLine("                Location = new System.Drawing.Point(20, 50),");
            sb.AppendLine("                AutoSize = true,");
            sb.AppendLine("                ForeColor = System.Drawing.Color.Gray");
            sb.AppendLine("            };");
            sb.AppendLine("");
            sb.AppendLine("            _txtCardKey = new TextBox");
            sb.AppendLine("            {");
            sb.AppendLine("                Location = new System.Drawing.Point(20, 85),");
            sb.AppendLine("                Size = new System.Drawing.Size(440, 25)");
            sb.AppendLine("            };");
            sb.AppendLine("");
            sb.AppendLine("            var btnActivate = new Button");
            sb.AppendLine("            {");
            sb.AppendLine("                Text = \"激活\",");
            sb.AppendLine("                Location = new System.Drawing.Point(20, 130),");
            sb.AppendLine("                Size = new System.Drawing.Size(100, 30),");
            sb.AppendLine("                DialogResult = DialogResult.OK");
            sb.AppendLine("            };");
            sb.AppendLine("            btnActivate.Click += BtnActivate_Click;");
            sb.AppendLine("");
            sb.AppendLine("            var btnCancel = new Button");
            sb.AppendLine("            {");
            sb.AppendLine("                Text = \"取消\",");
            sb.AppendLine("                Location = new System.Drawing.Point(140, 130),");
            sb.AppendLine("                Size = new System.Drawing.Size(100, 30),");
            sb.AppendLine("                DialogResult = DialogResult.Cancel");
            sb.AppendLine("            };");
            sb.AppendLine("");
            sb.AppendLine("            var lblTip = new Label");
            sb.AppendLine("            {");
            sb.AppendLine("                Text = \"提示：请使用LicenseMaker根据机器码生成卡密\",");
            sb.AppendLine("                Location = new System.Drawing.Point(20, 180),");
            sb.AppendLine("                AutoSize = true,");
            sb.AppendLine("                ForeColor = System.Drawing.Color.Gray");
            sb.AppendLine("            };");
            sb.AppendLine("");
            sb.AppendLine("            Controls.Add(lblInfo);");
            sb.AppendLine("            Controls.Add(lblMachine);");
            sb.AppendLine("            Controls.Add(_txtCardKey);");
            sb.AppendLine("            Controls.Add(btnActivate);");
            sb.AppendLine("            Controls.Add(btnCancel);");
            sb.AppendLine("            Controls.Add(lblTip);");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private void BtnActivate_Click(object sender, EventArgs e)");
            sb.AppendLine("        {");
            sb.AppendLine("            string cardKey = _txtCardKey.Text.Trim();");
            sb.AppendLine("            if (string.IsNullOrEmpty(cardKey))");
            sb.AppendLine("            {");
            sb.AppendLine("                MessageBox.Show(\"请输入卡密\", \"提示\", MessageBoxButtons.OK, MessageBoxIcon.Warning);");
            sb.AppendLine("                DialogResult = DialogResult.None;");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("");
            sb.AppendLine("            // 验证卡密");
            sb.AppendLine("            if (ValidateAndSaveLicense(cardKey))");
            sb.AppendLine("            {");
            sb.AppendLine("                MessageBox.Show(\"激活成功！\", \"成功\", MessageBoxButtons.OK, MessageBoxIcon.Information);");
            sb.AppendLine("                DialogResult = DialogResult.OK;");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                MessageBox.Show(\"卡密无效或已过期，请检查后重试。\", \"激活失败\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                DialogResult = DialogResult.None;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private bool ValidateAndSaveLicense(string cardKey)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                // 解密卡密");
            sb.AppendLine("                string key = GenerateEncryptionKey(_machineCode);");
            sb.AppendLine("                string licJson = DecryptCardKey(cardKey, key);");
            sb.AppendLine("                if (string.IsNullOrEmpty(licJson)) return false;");
            sb.AppendLine("");
            sb.AppendLine("                var lic = System.Text.Json.JsonSerializer.Deserialize<License>(licJson);");
            sb.AppendLine("                if (lic == null) return false;");
            sb.AppendLine("");
            sb.AppendLine("                // 验证机器码");
            sb.AppendLine("                if (!string.Equals(lic.machine, _machineCode, StringComparison.OrdinalIgnoreCase))");
            sb.AppendLine("                    return false;");
            sb.AppendLine("");
            sb.AppendLine("                // 验证签名");
            sb.AppendLine("                string signData = $\"{lic.machine}|{lic.days}|{lic.licId}\";");
            sb.AppendLine("                byte[] data = Encoding.UTF8.GetBytes(signData);");
            sb.AppendLine("                byte[] sign = Convert.FromBase64String(lic.sign);");
            sb.AppendLine("                if (!_publicKey.VerifyData(data, sign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))");
            sb.AppendLine("                    return false;");
            sb.AppendLine("");
            sb.AppendLine("                // 保存许可证");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string licDir = Path.Combine(appDir, \"config\");");
            sb.AppendLine("                Directory.CreateDirectory(licDir);");
            sb.AppendLine("                string licPath = Path.Combine(licDir, \"license.lic\");");
            sb.AppendLine("                File.WriteAllText(licPath, licJson);");
            sb.AppendLine("");
            sb.AppendLine("                return true;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { return false; }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static string GenerateEncryptionKey(string machineCode)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (var sha = SHA256.Create())");
            sb.AppendLine("            {");
            sb.AppendLine("                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(machineCode));");
            sb.AppendLine("                return BitConverter.ToString(hash).Replace(\"-\", \"\").Substring(0, 32);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static string DecryptCardKey(string cardKey, string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                byte[] fullBytes = Convert.FromBase64String(cardKey);");
            sb.AppendLine("                byte[] iv = fullBytes.Take(16).ToArray();");
            sb.AppendLine("                byte[] encrypted = fullBytes.Skip(16).ToArray();");
            sb.AppendLine("                using (var aes = Aes.Create())");
            sb.AppendLine("                {");
            sb.AppendLine("                    byte[] keyBytes = Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32));");
            sb.AppendLine("                    aes.Key = keyBytes;");
            sb.AppendLine("                    aes.IV = iv;");
            sb.AppendLine("                    using (var decryptor = aes.CreateDecryptor())");
            sb.AppendLine("                    {");
            sb.AppendLine("                        byte[] decrypted = decryptor.TransformFinalBlock(encrypted, 0, encrypted.Length);");
            sb.AppendLine("                        return Encoding.UTF8.GetString(decrypted);");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { return null; }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 生成包装器程序代码
        /// </summary>
        private string GenerateWrapperCode(string expectedMachineCode, byte[] licBytes, byte[] publicKeyBytes, byte[] originalExeBytes)
        {
            string licBase64 = Convert.ToBase64String(licBytes);
            string publicKeyBase64 = Convert.ToBase64String(publicKeyBytes);
            string originalExeBase64 = Convert.ToBase64String(originalExeBytes);

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.IO;");
            sb.AppendLine("using System.Security.Cryptography;");
            sb.AppendLine("using System.Text;");
            sb.AppendLine("using System.Windows.Forms;");
            sb.AppendLine("");
            sb.AppendLine("namespace Wrapper");
            sb.AppendLine("{");
            sb.AppendLine("    public class Program");
            sb.AppendLine("    {");
            sb.AppendLine($"        private static readonly string ExpectedMachineCode = \"{expectedMachineCode}\";");
            sb.AppendLine($"        private static readonly string EmbeddedLic = \"{licBase64}\";");
            sb.AppendLine($"        private static readonly string EmbeddedPublicKey = \"{publicKeyBase64}\";");
            sb.AppendLine($"        private static readonly string EmbeddedOriginalExe = \"{originalExeBase64}\";");
            sb.AppendLine("        private static string _logFilePath = null;");
            sb.AppendLine("");
            sb.AppendLine("        private static void InitLog()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string logDir = Path.Combine(appDir, \"log\");");
            sb.AppendLine("                if (!Directory.Exists(logDir))");
            sb.AppendLine("                    Directory.CreateDirectory(logDir);");
            sb.AppendLine("                string timestamp = DateTime.Now.ToString(\"yyyyMMdd_HHmmss\");");
            sb.AppendLine("                _logFilePath = Path.Combine(logDir, $\"wrapper_{timestamp}.log\");");
            sb.AppendLine("                File.WriteAllText(_logFilePath, $\"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 日志初始化\\r\\n\");");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                _logFilePath = null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static void Log(string message)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                if (_logFilePath != null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    string line = $\"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\\r\\n\";");
            sb.AppendLine("                    File.AppendAllText(_logFilePath, line);");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        [STAThread]");
            sb.AppendLine("        public static void Main()");
            sb.AppendLine("        {");
            sb.AppendLine("            Application.EnableVisualStyles();");
            sb.AppendLine("            Application.SetCompatibleTextRenderingDefault(false);");
            sb.AppendLine("");
            sb.AppendLine("            InitLog();");
            sb.AppendLine("            Log(\"程序启动\");");
            sb.AppendLine("");
            sb.AppendLine("            // 验证机器码");
            sb.AppendLine("            string currentMachineCode = GetMachineCode();");
            sb.AppendLine("            Log($\"当前机器码: {currentMachineCode}\");");
            sb.AppendLine("            Log($\"期望机器码: {ExpectedMachineCode}\");");
            sb.AppendLine("            if (currentMachineCode != ExpectedMachineCode)");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"机器码不匹配，退出\");");
            sb.AppendLine("                MessageBox.Show($\"机器码不匹配，无法运行此程序!\\n\\n当前机器码: {currentMachineCode}\\n授权机器码: {ExpectedMachineCode}\\n\\n请使用正确的授权文件或联系管理员。\", \"机器码验证失败\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("            Log(\"机器码验证通过\");");
            sb.AppendLine("");
            sb.AppendLine("            // 验证lic文件");
            sb.AppendLine("            Log(\"开始验证lic文件\");");
            sb.AppendLine("            if (!ValidateLicense())");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"内置lic验证失败，显示导入窗口\");");
            sb.AppendLine("                // 显示校验lic窗体");
            sb.AppendLine("                using (var licenseForm = new LicenseForm(EmbeddedPublicKey))");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (licenseForm.ShowDialog() != DialogResult.OK)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Log(\"用户取消lic导入，退出\");");
            sb.AppendLine("                        return;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("                Log(\"lic导入验证通过\");");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"内置lic验证通过\");");
            sb.AppendLine("            }");
            sb.AppendLine("");
            sb.AppendLine("            // 启动原程序");
            sb.AppendLine("            Log(\"开始启动原程序\");");
            sb.AppendLine("            var process = LaunchOriginalProgram();");
            sb.AppendLine("            ");
            sb.AppendLine("            // 如果进程成功启动，等待它退出");
            sb.AppendLine("            if (process != null && !process.HasExited)");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"等待原程序退出...\");");
            sb.AppendLine("                // 隐藏当前窗口");
            sb.AppendLine("                try { (Application.OpenForms[0])?.Hide(); } catch { }");
            sb.AppendLine("                // 等待进程退出");
            sb.AppendLine("                process.WaitForExit();");
            sb.AppendLine("                Log(\"原程序已退出，退出当前程序\");");
            sb.AppendLine("            }");
            sb.AppendLine("            else");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"进程启动失败或已退出\");");
            sb.AppendLine("            }");
            sb.AppendLine("            ");
            sb.AppendLine("            Application.Exit();");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static string GetMachineCode()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 使用MachineCodeTool的机器码生成逻辑");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string cpu = GetWmi(\"Win32_Processor\", \"ProcessorId\");");
            sb.AppendLine("                string board = GetWmi(\"Win32_BaseBoard\", \"SerialNumber\");");
            sb.AppendLine("                string disk = GetWmi(\"Win32_LogicalDisk\", \"VolumeSerialNumber\", \"DeviceID='C:'\");");
            sb.AppendLine("");
            sb.AppendLine("                string raw = $\"{cpu}|{board}|{disk}\";");
            sb.AppendLine("                return Sha256(raw);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                // 如果无法获取硬件信息，使用环境变量");
            sb.AppendLine("                var fallback = Environment.MachineName + Environment.UserName;");
            sb.AppendLine("                using (var sha = SHA256.Create())");
            sb.AppendLine("                {");
            sb.AppendLine("                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fallback));");
            sb.AppendLine("                    return BitConverter.ToString(hash).Replace(\"-\", \"\");");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static string GetWmi(string cls, string prop, string where = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var q = where == null");
            sb.AppendLine("                    ? $\"SELECT {prop} FROM {cls}\"");
            sb.AppendLine("                    : $\"SELECT {prop} FROM {cls} WHERE {where}\";");
            sb.AppendLine("                using (var searcher = new System.Management.ManagementObjectSearcher(q))");
            sb.AppendLine("                {");
            sb.AppendLine("                    foreach (System.Management.ManagementObject mo in searcher.Get())");
            sb.AppendLine("                        return mo[prop]?.ToString()?.Trim();");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("            return \"\";");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static string Sha256(string s)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (var sha = SHA256.Create())");
            sb.AppendLine("            {");
            sb.AppendLine("                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? \"\"));");
            sb.AppendLine("                return BitConverter.ToString(b).Replace(\"-\", \"\");");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static bool ValidateLicense()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"ValidateLicense 开始\");");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string configDir = Path.Combine(appDir, \"config\");");
            sb.AppendLine("                Directory.CreateDirectory(configDir);");
            sb.AppendLine("                string licPath = Path.Combine(configDir, \"license.lic\");");
            sb.AppendLine("                Log($\"lic文件路径: {licPath}\");");
            sb.AppendLine("");
            sb.AppendLine("                if (!File.Exists(licPath))");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"lic文件不存在\");");
            sb.AppendLine("                    return false;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                var licJson = File.ReadAllText(licPath);");
            sb.AppendLine("                Log($\"lic文件内容长度: {licJson.Length}\");");
            sb.AppendLine("                var lic = ParseLicense(licJson);");
            sb.AppendLine("");
            sb.AppendLine("                if (lic == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"解析lic文件失败\");");
            sb.AppendLine("                    return false;");
            sb.AppendLine("                }");
            sb.AppendLine("                Log($\"lic机器码: {lic.MachineCode}\");");
            sb.AppendLine("");
            sb.AppendLine("                if (lic.MachineCode != ExpectedMachineCode)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"lic机器码不匹配\");");
            sb.AppendLine("                    return false;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                if (lic.ExpirationDate < DateTime.Now)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log($\"lic已过期: {lic.ExpirationDate}\");");
            sb.AppendLine("                    return false;");
            sb.AppendLine("                }");
            sb.AppendLine("                Log($\"lic有效期至: {lic.ExpirationDate}\");");
            sb.AppendLine("");
            sb.AppendLine("                bool sigValid = Program.VerifyLicenseSignature(lic, EmbeddedPublicKey);");
            sb.AppendLine("                Log($\"签名验证结果: {sigValid}\");");
            sb.AppendLine("                return sigValid;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Log($\"ValidateLicense异常: {ex.Message}\");");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static LicenseInfo ParseLicense(string json)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var lic = new LicenseInfo();");
            sb.AppendLine("                ");
            sb.AppendLine("                // 解析 JSON 格式的 lic 文件，同时支持 PascalCase 和 camelCase");
            sb.AppendLine("                lic.MachineCode = ExtractJsonValue(json, \"MachineCode\") ?? ExtractJsonValue(json, \"machineCode\");");
            sb.AppendLine("                lic.LicenseKey = ExtractJsonValue(json, \"LicenseKey\") ?? ExtractJsonValue(json, \"licenseKey\");");
            sb.AppendLine("                lic.Signature = ExtractJsonValue(json, \"Signature\") ?? ExtractJsonValue(json, \"signature\");");
            sb.AppendLine("                ");
            sb.AppendLine("                var expDateStr = ExtractJsonValue(json, \"ExpirationDate\") ?? ExtractJsonValue(json, \"expirationDate\");");
            sb.AppendLine("                if (!string.IsNullOrEmpty(expDateStr))");
            sb.AppendLine("                {");
            sb.AppendLine("                    lic.ExpirationDate = DateTime.Parse(expDateStr);");
            sb.AppendLine("                }");
            sb.AppendLine("                ");
            sb.AppendLine("                var issueDateStr = ExtractJsonValue(json, \"IssueDate\") ?? ExtractJsonValue(json, \"issueDate\");");
            sb.AppendLine("                if (!string.IsNullOrEmpty(issueDateStr))");
            sb.AppendLine("                {");
            sb.AppendLine("                    lic.IssueDate = DateTime.Parse(issueDateStr);");
            sb.AppendLine("                }");
            sb.AppendLine("                ");
            sb.AppendLine("                return lic;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        static string ExtractJsonValue(string json, string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                // 使用 Split 方法解析 JSON");
            sb.AppendLine("                // 查找 key 的位置");
            sb.AppendLine("                string searchKey = key;");
            sb.AppendLine("                int keyPos = json.IndexOf(searchKey);");
            sb.AppendLine("                if (keyPos < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 从 key 后面开始查找");
            sb.AppendLine("                string afterKey = json.Substring(keyPos + searchKey.Length);");
            sb.AppendLine("                ");
            sb.AppendLine("                // 查找冒号");
            sb.AppendLine("                int colonPos = afterKey.IndexOf(':');");
            sb.AppendLine("                if (colonPos < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 获取冒号后面的内容");
            sb.AppendLine("                string afterColon = afterKey.Substring(colonPos + 1).Trim();");
            sb.AppendLine("                ");
            sb.AppendLine("                // 查找第一个双引号");
            sb.AppendLine("                int quote1 = afterColon.IndexOf((char)34);");
            sb.AppendLine("                if (quote1 < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 查找第二个双引号");
            sb.AppendLine("                int quote2 = afterColon.IndexOf((char)34, quote1 + 1);");
            sb.AppendLine("                if (quote2 < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 提取值");
            sb.AppendLine("                return afterColon.Substring(quote1 + 1, quote2 - quote1 - 1);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static bool VerifyLicenseSignature(LicenseInfo lic, string publicKeyBase64)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);");
            sb.AppendLine("                var publicKeyXml = Encoding.UTF8.GetString(publicKeyBytes);");
            sb.AppendLine("");
            sb.AppendLine("                using (var rsa = RSA.Create())");
            sb.AppendLine("                {");
            sb.AppendLine("                    rsa.FromXmlString(publicKeyXml);");
            sb.AppendLine("");
            sb.AppendLine("                    var data = lic.MachineCode + \"|\" + lic.ExpirationDate.ToString(\"O\") + \"|\" + lic.LicenseKey;");
            sb.AppendLine("                    var dataBytes = Encoding.UTF8.GetBytes(data);");
            sb.AppendLine("                    var signature = Convert.FromBase64String(lic.Signature);");
            sb.AppendLine("");
            sb.AppendLine("                    return rsa.VerifyData(dataBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private static System.Diagnostics.Process LaunchOriginalProgram()");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                Log(\"LaunchOriginalProgram 开始（安全模式）\");");
            sb.AppendLine("                var exeBytes = Convert.FromBase64String(EmbeddedOriginalExe);");
            sb.AppendLine("                Log($\"EXE字节数: {exeBytes.Length}\");");
            sb.AppendLine("                ");
            sb.AppendLine("                // 获取原程序所在目录");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                Log($\"程序目录: {appDir}\");");
            sb.AppendLine("                if (string.IsNullOrEmpty(appDir))");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"错误: 无法获取程序目录\");");
            sb.AppendLine("                    MessageBox.Show(\"无法获取程序目录\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return null;");
            sb.AppendLine("                }");
            sb.AppendLine("                ");
            sb.AppendLine("                // 在原程序目录创建临时文件（使用原程序名称，避免依赖问题）");
            sb.AppendLine("                string originalExeName = Path.GetFileName(Application.ExecutablePath);");
            sb.AppendLine("                string tempPath = Path.Combine(appDir, \"_temp_\" + Guid.NewGuid().ToString(\"N\").Substring(0, 8) + \"_\" + originalExeName);");
            sb.AppendLine("                Log($\"写入临时EXE: {tempPath}\");");
            sb.AppendLine("                File.WriteAllBytes(tempPath, exeBytes);");
            sb.AppendLine("                try { File.SetAttributes(tempPath, FileAttributes.Hidden); } catch { }");
            sb.AppendLine("                ");
            sb.AppendLine("                // 验证EXE文件是否写入成功");
            sb.AppendLine("                if (!File.Exists(tempPath))");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"错误: 临时文件创建失败\");");
            sb.AppendLine("                    MessageBox.Show(\"临时文件创建失败\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return null;");
            sb.AppendLine("                }");
            sb.AppendLine("                Log(\"临时文件创建成功\");");
            sb.AppendLine("");
            sb.AppendLine("                // 添加防调试检测");
            sb.AppendLine("                if (System.Diagnostics.Debugger.IsAttached)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"检测到调试器，拒绝启动\");");
            sb.AppendLine("                    MessageBox.Show(\"检测到调试器，程序拒绝启动\", \"安全警告\", MessageBoxButtons.OK, MessageBoxIcon.Warning);");
            sb.AppendLine("                    try { File.Delete(tempPath); } catch { }");
            sb.AppendLine("                    return null;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 检测常见调试器进程");
            sb.AppendLine("                string[] debuggerProcesses = { \"dnspy\", \"x64dbg\", \"ollydbg\", \"ida\", \"windbg\", \"devenv\" };");
            sb.AppendLine("                foreach (var proc in System.Diagnostics.Process.GetProcesses())");
            sb.AppendLine("                {");
            sb.AppendLine("                    string procName = proc.ProcessName.ToLower();");
            sb.AppendLine("                    bool isDebugger = false;");
            sb.AppendLine("                    foreach (var d in debuggerProcesses)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        if (procName.Contains(d))");
            sb.AppendLine("                        {");
            sb.AppendLine("                            isDebugger = true;");
            sb.AppendLine("                            break;");
            sb.AppendLine("                        }");
            sb.AppendLine("                    }");
            sb.AppendLine("                    if (isDebugger)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Log($\"检测到调试器进程: {proc.ProcessName}\");");
            sb.AppendLine("                        MessageBox.Show($\"检测到调试器进程: {proc.ProcessName}\\n程序拒绝启动\", \"安全警告\", MessageBoxButtons.OK, MessageBoxIcon.Warning);");
            sb.AppendLine("                        try { File.Delete(tempPath); } catch { }");
            sb.AppendLine("                        return null;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                var psi = new System.Diagnostics.ProcessStartInfo");
            sb.AppendLine("                {");
            sb.AppendLine("                    FileName = tempPath,");
            sb.AppendLine("                    UseShellExecute = true,");
            sb.AppendLine("                    WorkingDirectory = appDir  // 使用原程序目录");
            sb.AppendLine("                };");
            sb.AppendLine("                Log($\"启动进程: {tempPath}\");");
            sb.AppendLine("");
            sb.AppendLine("                var process = System.Diagnostics.Process.Start(psi);");
            sb.AppendLine("");
            sb.AppendLine("                if (process == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"错误: Process.Start 返回 null\");");
            sb.AppendLine("                    MessageBox.Show(\"启动程序失败：无法创建进程\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return null;");
            sb.AppendLine("                }");
            sb.AppendLine("                Log($\"进程已启动，ID: {process.Id}\");");
            sb.AppendLine("");
            sb.AppendLine("                // 等待一下检查进程是否真的在运行");
            sb.AppendLine("                System.Threading.Thread.Sleep(500);");
            sb.AppendLine("                try");
            sb.AppendLine("                {");
            sb.AppendLine("                    process.Refresh();");
            sb.AppendLine("                    Log($\"进程状态: {process.Responding}, 已退出: {process.HasExited}\");");
            sb.AppendLine("                    if (process.HasExited)");
            sb.AppendLine("                    {");
            sb.AppendLine("                        Log($\"进程已退出，退出码: {process.ExitCode}\");");
            sb.AppendLine("                        MessageBox.Show($\"程序启动后立即退出，退出码: {process.ExitCode}\\n请检查程序依赖或配置。\", \"启动失败\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                        return null;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("                catch (Exception checkEx)");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log($\"检查进程状态时出错: {checkEx.Message}\");");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 等待进程退出后立即清理临时文件");
            sb.AppendLine("                process.EnableRaisingEvents = true;");
            sb.AppendLine("                process.Exited += (s, e) =>");
            sb.AppendLine("                {");
            sb.AppendLine("                    Log(\"原程序已退出，立即清理临时文件\");");
            sb.AppendLine("                    try { File.Delete(tempPath); } catch (Exception delEx) { Log($\"清理临时文件失败: {delEx.Message}\"); }");
            sb.AppendLine("                };");
            sb.AppendLine("                Log(\"LaunchOriginalProgram 完成，返回进程对象\");");
            sb.AppendLine("                return process;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                Log($\"启动程序异常: {ex.Message}\\n{ex.StackTrace}\");");
            sb.AppendLine("                MessageBox.Show(\"启动程序失败: \" + ex.Message + \"\\n\" + ex.StackTrace, \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    public class LicenseInfo");
            sb.AppendLine("    {");
            sb.AppendLine("        public string MachineCode { get; set; }");
            sb.AppendLine("        public DateTime ExpirationDate { get; set; }");
            sb.AppendLine("        public DateTime IssueDate { get; set; }");
            sb.AppendLine("        public string LicenseKey { get; set; }");
            sb.AppendLine("        public string Signature { get; set; }");
            sb.AppendLine("    }");
            sb.AppendLine("");
            sb.AppendLine("    public class LicenseForm : Form");
            sb.AppendLine("    {");
            sb.AppendLine("        private TextBox txtLicenseFile;");
            sb.AppendLine("        private Button btnBrowse;");
            sb.AppendLine("        private Button btnOK;");
            sb.AppendLine("        private Button btnCancel;");
            sb.AppendLine("        private Label lblStatus;");
            sb.AppendLine("        private string _publicKeyBase64;");
            sb.AppendLine("");
            sb.AppendLine("        public LicenseForm(string publicKeyBase64)");
            sb.AppendLine("        {");
            sb.AppendLine("            _publicKeyBase64 = publicKeyBase64;");
            sb.AppendLine("            InitializeComponent();");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private void InitializeComponent()");
            sb.AppendLine("        {");
            sb.AppendLine("            this.Text = \"授权文件校验\";");
            sb.AppendLine("            this.Size = new System.Drawing.Size(500, 200);");
            sb.AppendLine("            this.StartPosition = FormStartPosition.CenterScreen;");
            sb.AppendLine("            this.FormBorderStyle = FormBorderStyle.FixedDialog;");
            sb.AppendLine("            this.MaximizeBox = false;");
            sb.AppendLine("            this.MinimizeBox = false;");
            sb.AppendLine("");
            sb.AppendLine("            // 授权文件路径文本框");
            sb.AppendLine("            txtLicenseFile = new TextBox();");
            sb.AppendLine("            txtLicenseFile.Location = new System.Drawing.Point(20, 30);");
            sb.AppendLine("            txtLicenseFile.Size = new System.Drawing.Size(300, 23);");
            sb.AppendLine("            txtLicenseFile.ReadOnly = true;");
            sb.AppendLine("            this.Controls.Add(txtLicenseFile);");
            sb.AppendLine("");
            sb.AppendLine("            // 浏览按钮");
            sb.AppendLine("            btnBrowse = new Button();");
            sb.AppendLine("            btnBrowse.Text = \"浏览...\";");
            sb.AppendLine("            btnBrowse.Location = new System.Drawing.Point(330, 30);");
            sb.AppendLine("            btnBrowse.Size = new System.Drawing.Size(75, 23);");
            sb.AppendLine("            btnBrowse.Click += BtnBrowse_Click;");
            sb.AppendLine("            this.Controls.Add(btnBrowse);");
            sb.AppendLine("");
            sb.AppendLine("            // 状态标签");
            sb.AppendLine("            lblStatus = new Label();");
            sb.AppendLine("            lblStatus.Location = new System.Drawing.Point(20, 70);");
            sb.AppendLine("            lblStatus.Size = new System.Drawing.Size(450, 20);");
            sb.AppendLine("            lblStatus.Text = \"请选择授权文件(.lic)\";");
            sb.AppendLine("            this.Controls.Add(lblStatus);");
            sb.AppendLine("");
            sb.AppendLine("            // 确定按钮");
            sb.AppendLine("            btnOK = new Button();");
            sb.AppendLine("            btnOK.Text = \"确定\";");
            sb.AppendLine("            btnOK.Location = new System.Drawing.Point(300, 120);");
            sb.AppendLine("            btnOK.Size = new System.Drawing.Size(75, 23);");
            sb.AppendLine("            btnOK.Click += BtnOK_Click;");
            sb.AppendLine("            this.Controls.Add(btnOK);");
            sb.AppendLine("");
            sb.AppendLine("            // 取消按钮");
            sb.AppendLine("            btnCancel = new Button();");
            sb.AppendLine("            btnCancel.Text = \"取消\";");
            sb.AppendLine("            btnCancel.Location = new System.Drawing.Point(400, 120);");
            sb.AppendLine("            btnCancel.Size = new System.Drawing.Size(75, 23);");
            sb.AppendLine("            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };");
            sb.AppendLine("            this.Controls.Add(btnCancel);");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private void BtnBrowse_Click(object sender, EventArgs e)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (var dlg = new OpenFileDialog())");
            sb.AppendLine("            {");
            sb.AppendLine("                dlg.Title = \"选择授权文件\";");
            sb.AppendLine("                dlg.Filter = \"授权文件 (*.lic)|*.lic\";");
            sb.AppendLine("                ");
            sb.AppendLine("                if (dlg.ShowDialog() == DialogResult.OK)");
            sb.AppendLine("                {");
            sb.AppendLine("                    txtLicenseFile.Text = dlg.FileName;");
            sb.AppendLine("                    lblStatus.Text = \"已选择授权文件，点击确定进行校验\";");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private void BtnOK_Click(object sender, EventArgs e)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (string.IsNullOrEmpty(txtLicenseFile.Text))");
            sb.AppendLine("            {");
            sb.AppendLine("                MessageBox.Show(\"请先选择授权文件\", \"提示\", MessageBoxButtons.OK, MessageBoxIcon.Warning);");
            sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine("");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var licJson = File.ReadAllText(txtLicenseFile.Text);");
            sb.AppendLine("                var lic = ParseLicense(licJson);");
            sb.AppendLine("");
            sb.AppendLine("                if (lic == null)");
            sb.AppendLine("                {");
            sb.AppendLine("                    MessageBox.Show(\"授权文件格式错误\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 验证机器码");
            sb.AppendLine("                string currentMachineCode = GetMachineCode();");
            sb.AppendLine("                if (string.IsNullOrEmpty(lic.MachineCode))");
            sb.AppendLine("                {");
            sb.AppendLine("                    MessageBox.Show(\"无法从lic文件中读取机器码，请检查lic文件格式是否正确。\", \"lic文件错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("                if (lic.MachineCode != currentMachineCode)");
            sb.AppendLine("                {");
            sb.AppendLine("                    MessageBox.Show($\"lic文件与当前机器码不一致!\\\\n\\\\n当前机器码: {currentMachineCode}\\\\n授权机器码: {lic.MachineCode}\\\\n\\\\n请使用与此机器码匹配的lic文件。\", \"lic文件不匹配\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 验证过期时间");
            sb.AppendLine("                if (lic.ExpirationDate < DateTime.Now)");
            sb.AppendLine("                {");
            sb.AppendLine("                    MessageBox.Show($\"授权已过期!\\\\n过期时间: {lic.ExpirationDate}\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 验证签名");
            sb.AppendLine("                if (!VerifyLicenseSignature(lic))");
            sb.AppendLine("                {");
            sb.AppendLine("                    MessageBox.Show(\"授权文件签名验证失败!\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("                    return;");
            sb.AppendLine("                }");
            sb.AppendLine("");
            sb.AppendLine("                // 保存到config目录");
            sb.AppendLine("                string appDir = Path.GetDirectoryName(Application.ExecutablePath);");
            sb.AppendLine("                string configDir = Path.Combine(appDir, \"config\");");
            sb.AppendLine("                Directory.CreateDirectory(configDir);");
            sb.AppendLine("                string licPath = Path.Combine(configDir, \"license.lic\");");
            sb.AppendLine("                File.WriteAllText(licPath, licJson);");
            sb.AppendLine("");
            sb.AppendLine("                MessageBox.Show(\"授权文件校验成功并已保存\", \"成功\", MessageBoxButtons.OK, MessageBoxIcon.Information);");
            sb.AppendLine("                this.DialogResult = DialogResult.OK;");
            sb.AppendLine("                this.Close();");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                MessageBox.Show($\"导入授权文件失败: {ex.Message}\", \"错误\", MessageBoxButtons.OK, MessageBoxIcon.Error);");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private string GetMachineCode()");
            sb.AppendLine("        {");
            sb.AppendLine("            // 使用MachineCodeTool的机器码生成逻辑");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                string cpu = GetWmi(\"Win32_Processor\", \"ProcessorId\");");
            sb.AppendLine("                string board = GetWmi(\"Win32_BaseBoard\", \"SerialNumber\");");
            sb.AppendLine("                string disk = GetWmi(\"Win32_LogicalDisk\", \"VolumeSerialNumber\", \"DeviceID='C:'\");");
            sb.AppendLine("");
            sb.AppendLine("                string raw = $\"{cpu}|{board}|{disk}\";");
            sb.AppendLine("                return Sha256(raw);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                var fallback = Environment.MachineName + Environment.UserName;");
            sb.AppendLine("                using (var sha = SHA256.Create())");
            sb.AppendLine("                {");
            sb.AppendLine("                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fallback));");
            sb.AppendLine("                    return BitConverter.ToString(hash).Replace(\"-\", \"\");");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static string GetWmi(string cls, string prop, string where = null)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var q = where == null");
            sb.AppendLine("                    ? $\"SELECT {prop} FROM {cls}\"");
            sb.AppendLine("                    : $\"SELECT {prop} FROM {cls} WHERE {where}\";");
            sb.AppendLine("                using (var searcher = new System.Management.ManagementObjectSearcher(q))");
            sb.AppendLine("                {");
            sb.AppendLine("                    foreach (System.Management.ManagementObject mo in searcher.Get())");
            sb.AppendLine("                        return mo[prop]?.ToString()?.Trim();");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("            return \"\";");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static string Sha256(string s)");
            sb.AppendLine("        {");
            sb.AppendLine("            using (var sha = SHA256.Create())");
            sb.AppendLine("            {");
            sb.AppendLine("                var b = sha.ComputeHash(Encoding.UTF8.GetBytes(s ?? \"\"));");
            sb.AppendLine("                return BitConverter.ToString(b).Replace(\"-\", \"\");");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        static LicenseInfo ParseLicense(string json)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                // 首先尝试解析 JSON 格式");
            sb.AppendLine("                if (json.Trim().StartsWith(\"{\") || json.Trim().StartsWith(\"[\"))");
            sb.AppendLine("                {");
            sb.AppendLine("                    return ParseLicenseJson(json);");
            sb.AppendLine("                }");
            sb.AppendLine("                // 否则解析行格式");
            sb.AppendLine("                var lines = json.Split('\\n');");
            sb.AppendLine("                var lic = new LicenseInfo();");
            sb.AppendLine("                foreach (var line in lines)");
            sb.AppendLine("                {");
            sb.AppendLine("                    if (line.StartsWith(\"MachineCode:\"))");
            sb.AppendLine("                        lic.MachineCode = line.Substring(12).Trim();");
            sb.AppendLine("                    else if (line.StartsWith(\"ExpirationDate:\"))");
            sb.AppendLine("                        lic.ExpirationDate = DateTime.Parse(line.Substring(15).Trim());");
            sb.AppendLine("                    else if (line.StartsWith(\"LicenseKey:\"))");
            sb.AppendLine("                        lic.LicenseKey = line.Substring(11).Trim();");
            sb.AppendLine("                    else if (line.StartsWith(\"Signature:\"))");
            sb.AppendLine("                        lic.Signature = line.Substring(10).Trim();");
            sb.AppendLine("                }");
            sb.AppendLine("                return lic;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        static LicenseInfo ParseLicenseJson(string json)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var lic = new LicenseInfo();");
            sb.AppendLine("                // 同时支持 PascalCase 和 camelCase");
            sb.AppendLine("                lic.MachineCode = ExtractJsonValue(json, \"MachineCode\") ?? ExtractJsonValue(json, \"machineCode\");");
            sb.AppendLine("                lic.LicenseKey = ExtractJsonValue(json, \"LicenseKey\") ?? ExtractJsonValue(json, \"licenseKey\");");
            sb.AppendLine("                lic.Signature = ExtractJsonValue(json, \"Signature\") ?? ExtractJsonValue(json, \"signature\");");
            sb.AppendLine("                ");
            sb.AppendLine("                var expDateStr = ExtractJsonValue(json, \"ExpirationDate\") ?? ExtractJsonValue(json, \"expirationDate\");");
            sb.AppendLine("                if (!string.IsNullOrEmpty(expDateStr))");
            sb.AppendLine("                {");
            sb.AppendLine("                    lic.ExpirationDate = DateTime.Parse(expDateStr);");
            sb.AppendLine("                }");
            sb.AppendLine("                ");
            sb.AppendLine("                var issueDateStr = ExtractJsonValue(json, \"IssueDate\") ?? ExtractJsonValue(json, \"issueDate\");");
            sb.AppendLine("                if (!string.IsNullOrEmpty(issueDateStr))");
            sb.AppendLine("                {");
            sb.AppendLine("                    lic.IssueDate = DateTime.Parse(issueDateStr);");
            sb.AppendLine("                }");
            sb.AppendLine("                ");
            sb.AppendLine("                return lic;");
            sb.AppendLine("            }");
            sb.AppendLine("            catch");
            sb.AppendLine("            {");
            sb.AppendLine("                return null;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        static string ExtractJsonValue(string json, string key)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                // 查找 key 的位置");
            sb.AppendLine("                string searchKey = key;");
            sb.AppendLine("                int keyPos = json.IndexOf(searchKey);");
            sb.AppendLine("                if (keyPos < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 从 key 后面开始查找");
            sb.AppendLine("                string afterKey = json.Substring(keyPos + searchKey.Length);");
            sb.AppendLine("                ");
            sb.AppendLine("                // 查找冒号");
            sb.AppendLine("                int colonPos = afterKey.IndexOf(':');");
            sb.AppendLine("                if (colonPos < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 获取冒号后面的内容");
            sb.AppendLine("                string afterColon = afterKey.Substring(colonPos + 1).Trim();");
            sb.AppendLine("                ");
            sb.AppendLine("                // 查找第一个双引号");
            sb.AppendLine("                int quote1 = afterColon.IndexOf((char)34);");
            sb.AppendLine("                if (quote1 < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 查找第二个双引号");
            sb.AppendLine("                int quote2 = afterColon.IndexOf((char)34, quote1 + 1);");
            sb.AppendLine("                if (quote2 < 0) return null;");
            sb.AppendLine("                ");
            sb.AppendLine("                // 提取值并解码JSON转义字符");
            sb.AppendLine("                string value = afterColon.Substring(quote1 + 1, quote2 - quote1 - 1);");
            sb.AppendLine("                return DecodeJsonString(value);");
            sb.AppendLine("            }");
            sb.AppendLine("            catch { }");
            sb.AppendLine("            return null;");
            sb.AppendLine("        }");
            sb.AppendLine("        ");
            sb.AppendLine("        static string DecodeJsonString(string value)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (string.IsNullOrEmpty(value)) return value;");
            sb.AppendLine("            ");
            sb.AppendLine("            // 解码常见的JSON转义字符");
            sb.AppendLine("            value = value.Replace(\"\\\\\\\"\", \"\\\"\");");
            sb.AppendLine("            value = value.Replace(\"\\\\\\\\\", \"\\\\\");");
            sb.AppendLine("            value = value.Replace(\"\\\\/\", \"/\");");
            sb.AppendLine("            value = value.Replace(\"\\\\b\", \"\\b\");");
            sb.AppendLine("            value = value.Replace(\"\\\\f\", \"\\f\");");
            sb.AppendLine("            value = value.Replace(\"\\\\n\", \"\\n\");");
            sb.AppendLine("            value = value.Replace(\"\\\\r\", \"\\r\");");
            sb.AppendLine("            value = value.Replace(\"\\\\t\", \"\\t\");");
            sb.AppendLine("            ");
            sb.AppendLine("            // 解码Unicode转义序列(如 \\u002B -> +)");
            sb.AppendLine("            var sb = new System.Text.StringBuilder();");
            sb.AppendLine("            for (int i = 0; i < value.Length; i++)");
            sb.AppendLine("            {");
            sb.AppendLine("                if (i < value.Length - 5 && value[i] == '\\\\' && value[i + 1] == 'u')");
            sb.AppendLine("                {");
            sb.AppendLine("                    string hex = value.Substring(i + 2, 4);");
            sb.AppendLine("                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int code))");
            sb.AppendLine("                    {");
            sb.AppendLine("                        sb.Append((char)code);");
            sb.AppendLine("                        i += 5;");
            sb.AppendLine("                        continue;");
            sb.AppendLine("                    }");
            sb.AppendLine("                }");
            sb.AppendLine("                sb.Append(value[i]);");
            sb.AppendLine("            }");
            sb.AppendLine("            ");
            sb.AppendLine("            return sb.ToString();");
            sb.AppendLine("        }");
            sb.AppendLine("");
            sb.AppendLine("        private bool VerifyLicenseSignature(LicenseInfo lic)");
            sb.AppendLine("        {");
            sb.AppendLine("            try");
            sb.AppendLine("            {");
            sb.AppendLine("                var publicKeyBytes = Convert.FromBase64String(_publicKeyBase64);");
            sb.AppendLine("                var publicKeyXml = Encoding.UTF8.GetString(publicKeyBytes);");
            sb.AppendLine("");
            sb.AppendLine("                using (var rsa = RSA.Create())");
            sb.AppendLine("                {");
            sb.AppendLine("                    rsa.FromXmlString(publicKeyXml);");
            sb.AppendLine("");
            sb.AppendLine("                    var data = lic.MachineCode + \"|\" + lic.ExpirationDate.ToString(\"O\") + \"|\" + lic.LicenseKey;");
            sb.AppendLine("                    var dataBytes = Encoding.UTF8.GetBytes(data);");
            sb.AppendLine("                    var signature = Convert.FromBase64String(lic.Signature);");
            sb.AppendLine("");
            sb.AppendLine("                    return rsa.VerifyData(dataBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);");
            sb.AppendLine("                }");
            sb.AppendLine("            }");
            sb.AppendLine("            catch (Exception ex)");
            sb.AppendLine("            {");
            sb.AppendLine("                MessageBox.Show(\"签名验证异常: \" + ex.Message, \"调试信息\", MessageBoxButtons.OK, MessageBoxIcon.Information);");
            sb.AppendLine("                return false;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        /// <summary>
        /// 从EXE文件中提取图标
        /// </summary>
        private string ExtractIconFromExe(string exePath)
        {
            try
            {
                // 使用Icon.ExtractAssociatedIcon提取图标
                using (var icon = Icon.ExtractAssociatedIcon(exePath))
                {
                    if (icon != null)
                    {
                        // 保存到临时目录
                        string tempDir = Path.Combine(Path.GetTempPath(), "EncryptTools_Icons");
                        Directory.CreateDirectory(tempDir);
                        string iconPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.ico");
                        
                        using (var fs = new FileStream(iconPath, FileMode.Create))
                        {
                            icon.Save(fs);
                        }
                        
                        return iconPath;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"提取图标失败: {ex.Message}");
            }
            return null;
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

        private void TabWorkspaces_DrawItem(object? sender, DrawItemEventArgs e)
        {
            var tc = (TabControl)sender!;
            var page = tc.TabPages[e.Index];
            var bounds = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) != 0;

            using var bgBrush = new SolidBrush(selected ? SystemColors.Window : SystemColors.Control);
            e.Graphics.FillRectangle(bgBrush, bounds);

            var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using var textBrush = new SolidBrush(SystemColors.ControlText);
            e.Graphics.DrawString(page.Text, tc.Font, textBrush, bounds, sf);
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

        private static string? GetSelectedPwdFilePath(WorkspaceContext ctx)
        {
            try
            {
                if (ctx.CbPwdFile == null || ctx.CbPwdFile.SelectedIndex <= 0) return null;
                if (ctx.CbPwdFile.SelectedItem is string name && name.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
                {
                    var path = Path.Combine(PasswordFileService.GetPwdDirectory(), name);
                    return File.Exists(path) ? path : null;
                }
            }
            catch { }
            return null;
        }

        private static byte[]? TryComputePwdFileHash(string? pwdFilePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(pwdFilePath) || !File.Exists(pwdFilePath)) return null;
                var bytes = File.ReadAllBytes(pwdFilePath);
                return Compat.Sha256Hash(bytes);
            }
            catch { return null; }
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

        /// <summary>封装可运行 EXE 时使用当前进程主程序为壳（不做 dotnet publish 单文件模板）。</summary>
        private static string GetPackExeTemplatePath(Action<string> log)
        {
            try
            {
                var path = Application.ExecutablePath;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }
            catch { }
            log($"[{DateTime.Now:HH:mm:ss}] 封装EXE失败：无法取得当前程序路径。");
            return string.Empty;
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
                var pwdPath = GetSelectedPwdFilePath(ctx);
                bool packExe = ctx.ChkPackExe?.Checked ?? false;
                // 可运行 exe 常在 net48 环境解密：将 .pwd 规范为 CBC，且哈希须在改写后计算
                if (packExe && PasswordFileHelper.EnsurePwdFileCbcForPortableExe(pwdPath))
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已为封装 EXE 将密码文件升级为兼容格式（AES-CBC/.pwd）。{Environment.NewLine}");
                var pwdHash = TryComputePwdFileHash(pwdPath);

                bool inPlace = ctx.ChkOverwrite?.Checked ?? false;
                var algorithm = MapAlgorithm(ctx.CbAlgo);
                var encryptedExt = GetSelectedEncryptedExtension(ctx.CbSuffix, algorithm);
                // 单 exe 兼容：不拦截；加密时自动用 GcmRunner（.NET 8 已装）或 CBC（未装）

                // 先在后台统计工作区内所有要处理的文件总数（文件=1，目录=递归计数），用于统一的 processed/total 文本显示
                long workspaceTotalFiles = 0;
                try
                {
                    var pathsToCount = new List<string>(paths);
                    workspaceTotalFiles = await Task.Run(() =>
                    {
                        long tot = 0;
                        foreach (var p in pathsToCount)
                        {
                            try
                            {
                                if (File.Exists(p)) { tot += 1; continue; }
                                if (Directory.Exists(p))
                                {
                                    try { tot += Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).LongCount(); } catch { }
                                }
                            }
                            catch { }
                        }
                        return tot;
                    }).ConfigureAwait(true);
                }
                catch { }

                if (workspaceTotalFiles <= 0) workspaceTotalFiles = paths.Count;

                // 将状态文本展示到工作区中部的就绪位置（若存在），否则回退到底部状态栏，显示统一 total
                try
                {
                    var initTxt = $"执行加密中… (0/{workspaceTotalFiles})";
                    if (ctx.CenterStatusLabel != null)
                    {
                        if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = initTxt));
                        else ctx.CenterStatusLabel.Text = initTxt;
                    }
                    else
                    {
                        if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = initTxt)); else _statusLeft.Text = initTxt;
                    }
                }
                catch { }
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

                string packExeTemplate = "";
                if (packExe)
                    packExeTemplate = GetPackExeTemplatePath(log);

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
                long globalProcessed = 0;
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
                        var template = packExeTemplate;
                        if (string.IsNullOrWhiteSpace(template) || !File.Exists(template))
                        {
                            log($"[{DateTime.Now:HH:mm:ss}] 封装EXE失败：模板不存在或无效。");
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
                        // 为了在封装 EXE 路径时也能在工作区中部展示进度，维护文件计数与每文件百分比的复合进度显示
                        int packTotal = filesToPack.Count;
                        int packIndex = 0;
                        foreach (var oneFile in filesToPack)
                        {
                            packIndex++;
                            // 在开始处理每个文件时先更新中部状态文本
                            try
                            {
                                var startTxt = $"封装EXE: {packIndex}/{packTotal} {Path.GetFileName(oneFile)} (0%)";
                                if (ctx.CenterStatusLabel != null)
                                {
                                    if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = startTxt));
                                    else ctx.CenterStatusLabel.Text = startTxt;
                                }
                                else
                                {
                                    if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = startTxt)); else _statusLeft.Text = startTxt;
                                }
                            }
                            catch { }
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

                            // 源文件本身已是带载荷的封装 exe：避免重复封装；inPlace 下同路径会 Write 后 Delete 把成品删掉
                            if (string.Equals(Path.GetExtension(oneFile), ".exe", StringComparison.OrdinalIgnoreCase) && ExePayload.HasPayload(oneFile))
                            {
                                log($"[{DateTime.Now:HH:mm:ss}] 已是封装EXE，跳过: {oneFile}");
                                UpdateFileListItemPathStatus(ctx.FileListView, oneFile, oneFile, "已封装EXE");
                                continue;
                            }

                            // 目标封装产物已存在（非随机名）：视为已加密，不重复处理
                            if (!useRandomExeName && File.Exists(outExe) && ExePayload.HasPayload(outExe))
                            {
                                log($"[{DateTime.Now:HH:mm:ss}] 已存在封装EXE，跳过: {outExe}");
                                UpdateFileListItemPathStatus(ctx.FileListView, oneFile, outExe, "已封装EXE");
                                continue;
                            }

                            log($"[{DateTime.Now:HH:mm:ss}] 开始封装EXE: {oneFile} -> {outExe}");
                            var tmpEnc = Path.Combine(Path.GetTempPath(), "encryptTools_pack_" + Guid.NewGuid().ToString("N") + ".enc");
                            try
                            {
                                // 封装 EXE 的进度：以“生成临时加密文件 tmpEnc”为准，按读取字节实时更新 oneFile 的进度
                                var basePackProgress = CreateFileListProgress(ctx.FileListView, oneFile, isDecrypt: false);
                                // capture current file index/name for the closure so UI updates reflect correct item
                                int currentPackIndex = packIndex;
                                string currentPackName = Path.GetFileName(oneFile);
                                IProgress<double> packProgress = new Progress<double>(p =>
                                {
                                    try { basePackProgress.Report(p); } catch { }
                                    try
                                    {
                                        int pct = Math.Min(100, Math.Max(0, (int)(p * 100)));
                                        var txt = $"封装EXE: {currentPackIndex}/{packTotal} {currentPackName} ({pct}%)";
                                        if (ctx.CenterStatusLabel != null)
                                        {
                                            if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = txt));
                                            else ctx.CenterStatusLabel.Text = txt;
                                        }
                                        else
                                        {
                                            if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = txt)); else _statusLeft.Text = txt;
                                        }
                                    }
                                    catch { }
                                });
                                UpdateFileListProgress(ctx.FileListView, oneFile, 0);

                                // 封装 exe 时若非 .NET 8 环境则一律使用 CBC，确保打包后的 exe 在本机可直接解密
                                CryptoAlgorithm packAlgo = RuntimeHelper.IsNet8OrHigher ? algorithm : CryptoAlgorithm.AesCbc;
                                if (packAlgo == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && RuntimeHelper.IsNet8InstalledOnMachine)
                                    packAlgo = CryptoAlgorithm.AesCbc;
                                bool packUseGcm = (packAlgo == CryptoAlgorithm.AesGcm && RuntimeHelper.IsNet8InstalledOnMachine && !RuntimeHelper.IsNet8OrHigher);

                                if (packUseGcm)
                                {
                                    // GCM 走外部进程：用输出文件增长轮询模拟进度
                                    bool ok = await GcmRunner.EncryptAsync(oneFile, tmpEnc, password, packProgress, m => log($"[{DateTime.Now:HH:mm:ss}] {m}"), CancellationToken.None, pwdHash).ConfigureAwait(false);
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
                                    }), CancellationToken.None, pwdHash);
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
                                        var fullSrc = Path.GetFullPath(oneFile);
                                        var fullOut = Path.GetFullPath(outExe);
                                        // 源与输出为同一文件时禁止删除（否则会删掉刚写入的封装 exe）
                                        if (!string.Equals(fullSrc, fullOut, StringComparison.OrdinalIgnoreCase))
                                        {
                                            File.Delete(oneFile);
                                            log($"[{DateTime.Now:HH:mm:ss}] 已删除源文件: {oneFile}");
                                        }
                                    }
                                    catch (Exception exDel)
                                    {
                                        log($"[{DateTime.Now:HH:mm:ss}] 删除源文件失败: {exDel.Message}");
                                    }
                                }
                                // 当前文件封装成功：计入全局已处理数并更新中部文本
                                try
                                {
                                    Interlocked.Increment(ref globalProcessed);
                                    var txt = $"加密中… ({globalProcessed}/{workspaceTotalFiles})";
                                    if (ctx.CenterStatusLabel != null)
                                    {
                                        if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = txt));
                                        else ctx.CenterStatusLabel.Text = txt;
                                    }
                                    else
                                    {
                                        if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = txt)); else _statusLeft.Text = txt;
                                    }
                                }
                                catch { }
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
                        FileProgress = null,
                        EncryptedExtension = encryptedExt,
                        PasswordFileHash = pwdHash
                    };
                    // wrap per-source FileProgress so we convert per-source processedFiles into increments of globalProcessed
                    long prevForThisSource = 0;
                    options.FileProgress = (p, t) =>
                    {
                        try
                        {
                            long delta = p - prevForThisSource;
                            if (delta < 0) delta = 0;
                            prevForThisSource = p;
                            Interlocked.Add(ref globalProcessed, delta);
                            var txt = $"加密中… ({globalProcessed}/{workspaceTotalFiles})";
                            if (ctx.CenterStatusLabel != null)
                            {
                                if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = txt));
                                else ctx.CenterStatusLabel.Text = txt;
                            }
                            else
                            {
                                if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = txt)); else _statusLeft.Text = txt;
                            }
                        }
                        catch { }
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
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 已完成加密");
                if (ctx.CenterStatusLabel != null)
                {
                    try
                    {
                        if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = "已完成加密"));
                        else ctx.CenterStatusLabel.Text = "已完成加密";
                    }
                    catch { }
                }
                else
                {
                    _statusLeft.Text = "已完成加密";
                }
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
                var pwdPath = GetSelectedPwdFilePath(ctx);
                var pwdHash = TryComputePwdFileHash(pwdPath);

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
                        _statusLeft.Text = "已完成解密";
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
                // 先统计工作区内所有解密目标的文件总数（文件=1，目录递归计数），用于统一显示已处理/总计
                long workspaceTotalFiles = 0;
                try
                {
                    var snaps = new List<string>(decryptSources);
                    workspaceTotalFiles = await Task.Run(() =>
                    {
                        long tot = 0;
                        foreach (var p in snaps)
                        {
                            try
                            {
                                if (File.Exists(p)) { tot += 1; continue; }
                                if (Directory.Exists(p))
                                {
                                    try { tot += Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).LongCount(); } catch { }
                                }
                            }
                            catch { }
                        }
                        return tot;
                    }).ConfigureAwait(true);
                }
                catch { }

                if (workspaceTotalFiles <= 0) workspaceTotalFiles = decryptSources.Count;

                // 在工作区中部显示统一解密中… (0/total)
                try
                {
                    var initTxt = $"执行解密中… (0/{workspaceTotalFiles})";
                    if (ctx.CenterStatusLabel != null)
                    {
                        if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = initTxt));
                        else ctx.CenterStatusLabel.Text = initTxt;
                    }
                    else
                    {
                        if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = initTxt)); else _statusLeft.Text = initTxt;
                    }
                }
                catch { }

                long globalProcessed = 0;

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
                                UpdateFileListProgress(ctx.FileListView, exePath, 0, isDecrypt: true);
                                var exeDecProgress = CreateFileListProgress(ctx.FileListView, exePath, isDecrypt: true);
                                CryptoService.DecryptResult result;
                                try
                                {
                                    var (peekAlg, peekName) = CryptoService.PeekEncryptedFileInfo(tmpEnc);
                                    if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && RuntimeHelper.IsNet8InstalledOnMachine)
                                    {
                                        bool ok = await GcmRunner.DecryptAsync(tmpEnc, tmpOut, password, exeDecProgress).ConfigureAwait(false);
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
                                        long inLen = 0;
                                        try { inLen = new FileInfo(tmpEnc).Length; } catch { }
                                        long proc = 0;
                                        int lastPct = -1;
                                        result = await crypto.DecryptFileAsync(tmpEnc, tmpOut, password, new Progress<long>(bytes =>
                                        {
                                            proc += bytes;
                                            int pct = inLen <= 0 ? 0 : Math.Min(100, (int)((double)proc / inLen * 100));
                                            if (pct != lastPct) { lastPct = pct; exeDecProgress.Report(pct / 100.0); }
                                        }), CancellationToken.None, pwdHash);
                                    }
                                }
                                finally { try { File.Delete(tmpEnc); } catch { } }
                                var desiredName = string.IsNullOrWhiteSpace(result.OriginalFileName) ? Path.GetFileNameWithoutExtension(exePath) + "_decrypted" : SanitizeFileNameLocal(result.OriginalFileName);
                                var outPath = Path.Combine(outDir, desiredName);
                                try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                                Compat.FileMoveOverwrite(tmpOut, outPath);
                                UpdateFileListProgress(ctx.FileListView, exePath, 100, isDecrypt: true);
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
                            UpdateFileListProgress(ctx.FileListView, source, 0, isDecrypt: true);
                            var exeDecProgress = CreateFileListProgress(ctx.FileListView, source, isDecrypt: true);
                            CryptoService.DecryptResult result;
                            try
                            {
                                var (peekAlg, peekName) = CryptoService.PeekEncryptedFileInfo(tmpEnc);
                                if (peekAlg == CryptoAlgorithm.AesGcm && !RuntimeHelper.IsNet8OrHigher && RuntimeHelper.IsNet8InstalledOnMachine)
                                {
                                    bool ok = await GcmRunner.DecryptAsync(tmpEnc, tmpOut, password, exeDecProgress).ConfigureAwait(false);
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
                                    long inLen = 0;
                                    try { inLen = new FileInfo(tmpEnc).Length; } catch { }
                                    long proc = 0;
                                    int lastPct = -1;
                                    result = await crypto.DecryptFileAsync(tmpEnc, tmpOut, password, new Progress<long>(bytes =>
                                    {
                                        proc += bytes;
                                        int pct = inLen <= 0 ? 0 : Math.Min(100, (int)((double)proc / inLen * 100));
                                        if (pct != lastPct) { lastPct = pct; exeDecProgress.Report(pct / 100.0); }
                                    }), CancellationToken.None, pwdHash);
                                }
                            }
                            finally { try { File.Delete(tmpEnc); } catch { } }
                            var desiredName = string.IsNullOrWhiteSpace(result.OriginalFileName) ? Path.GetFileNameWithoutExtension(source) + "_decrypted" : SanitizeFileNameLocal(result.OriginalFileName);
                            var outPath = Path.Combine(outDir, desiredName);
                            try { if (File.Exists(outPath)) File.Delete(outPath); } catch { }
                            Compat.FileMoveOverwrite(tmpOut, outPath);
                            UpdateFileListProgress(ctx.FileListView, source, 100, isDecrypt: true);
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
                        // Wrap per-source file progress into workspace-level aggregation
                        FileProgress = null,
                        EncryptedExtension = encryptedExt,
                        PasswordFileHash = pwdHash
                    };
                    // wrap FileProgress to aggregate into globalProcessed
                    long prevForThisSource = 0;
                    options.FileProgress = (p, t) =>
                    {
                        try
                        {
                            long delta = p - prevForThisSource;
                            if (delta < 0) delta = 0;
                            prevForThisSource = p;
                            Interlocked.Add(ref globalProcessed, delta);
                            var txt = $"解密中… ({globalProcessed}/{workspaceTotalFiles})";
                            if (ctx.CenterStatusLabel != null)
                            {
                                if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = txt));
                                else ctx.CenterStatusLabel.Text = txt;
                            }
                            else
                            {
                                if (this.InvokeRequired) this.BeginInvoke(new Action(() => _statusLeft.Text = txt)); else _statusLeft.Text = txt;
                            }
                        }
                        catch { }
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
                if (ctx.CenterStatusLabel != null)
                {
                    try
                    {
                        if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = "解密完成。"));
                        else ctx.CenterStatusLabel.Text = "解密完成。";
                    }
                    catch { }
                }
                else
                {
                    _statusLeft.Text = "解密完成。";
                }
            }
            catch (NotSupportedException ex) when (ex.Message != null && (ex.Message.Contains("AES-GCM") || ex.Message.Contains("需要")))
            {
                MessageBox.Show(RuntimeHelper.GetAesGcmRequirementMessage(), "需要 .NET 8", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 解密失败: {ex.Message}");
                if (ctx.CenterStatusLabel != null)
                {
                    try
                    {
                        if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = "解密失败。"));
                        else ctx.CenterStatusLabel.Text = "解密失败。";
                    }
                    catch { }
                }
                else
                {
                    _statusLeft.Text = "解密失败。";
                }
            }
            catch (Exception ex)
            {
                AppendLogAndScroll(ctx.LogBox, $"[{DateTime.Now:HH:mm:ss}] 解密失败: {ex.Message}");
                if (ctx.CenterStatusLabel != null)
                {
                    try
                    {
                        if (ctx.CenterStatusLabel.InvokeRequired) ctx.CenterStatusLabel.BeginInvoke(new Action(() => ctx.CenterStatusLabel.Text = "解密失败。"));
                        else ctx.CenterStatusLabel.Text = "解密失败。";
                    }
                    catch { }
                }
                else
                {
                    _statusLeft.Text = "解密失败。";
                }
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

        #region Borderless window support — drag / resize / maximize

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int RESIZE_GRIP = 6;

        private void TitleBar_MouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (WindowState == FormWindowState.Maximized)
            {
                var pct = (double)e.X / Width;
                WindowState = FormWindowState.Normal;
                Left = (int)(MousePosition.X - Width * pct);
                Top = MousePosition.Y - e.Y;
            }
            ReleaseCapture();
            SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized
                ? FormWindowState.Normal
                : FormWindowState.Maximized;
            _btnMaxRestore.Kind = WindowState == FormWindowState.Maximized
                ? WinControlButton.ButtonKind.Restore
                : WinControlButton.ButtonKind.Maximize;
            _btnMaxRestore.Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.Style |= 0x00020000; // WS_MINIMIZEBOX — enable taskbar minimize
                return cp;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public Point ptReserved;
            public Point ptMaxSize;
            public Point ptMaxPosition;
            public Point ptMinTrackSize;
            public Point ptMaxTrackSize;
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int WM_GETMINMAXINFO = 0x0024;

            if (m.Msg == WM_GETMINMAXINFO)
            {
                base.WndProc(ref m);
                var screen = Screen.FromHandle(Handle);
                var info = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO))!;
                info.ptMaxPosition = new Point(
                    screen.WorkingArea.Left - screen.Bounds.Left,
                    screen.WorkingArea.Top - screen.Bounds.Top);
                info.ptMaxSize = new Point(screen.WorkingArea.Width, screen.WorkingArea.Height);
                Marshal.StructureToPtr(info, m.LParam, true);
                return;
            }

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
            {
                var pt = PointToClient(new Point(
                    (int)(m.LParam.ToInt64() & 0xFFFF),
                    (int)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
                int w = ClientSize.Width, h = ClientSize.Height;
                const int g = RESIZE_GRIP;

                if      (pt.X <= g && pt.Y <= g)          m.Result = (IntPtr)13; // HTTOPLEFT
                else if (pt.X >= w - g && pt.Y <= g)      m.Result = (IntPtr)14; // HTTOPRIGHT
                else if (pt.X <= g && pt.Y >= h - g)      m.Result = (IntPtr)16; // HTBOTTOMLEFT
                else if (pt.X >= w - g && pt.Y >= h - g)  m.Result = (IntPtr)17; // HTBOTTOMRIGHT
                else if (pt.X <= g)                        m.Result = (IntPtr)10; // HTLEFT
                else if (pt.X >= w - g)                    m.Result = (IntPtr)11; // HTRIGHT
                else if (pt.Y <= g)                        m.Result = (IntPtr)12; // HTTOP
                else if (pt.Y >= h - g)                    m.Result = (IntPtr)15; // HTBOTTOM
            }
        }

        #endregion
    }

    /// <summary>
    /// 自绘窗口控制按钮（最小化 / 最大化 / 还原 / 关闭），确保图标大小完全一致。
    /// </summary>
    internal sealed class WinControlButton : ToolStripButton
    {
        public enum ButtonKind { Minimize, Maximize, Restore, Close }

        private ButtonKind _kind;
        private bool _hovered;

        public ButtonKind Kind
        {
            get => _kind;
            set { _kind = value; Invalidate(); }
        }

        public WinControlButton(ButtonKind kind) : base(string.Empty)
        {
            _kind = kind;
            DisplayStyle = ToolStripItemDisplayStyle.None;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hovered = true;
            base.OnMouseEnter(e);
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hovered = false;
            base.OnMouseLeave(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var rect = new Rectangle(0, 0, Width, Height);

            if (_hovered)
            {
                var bgColor = _kind == ButtonKind.Close
                    ? Color.FromArgb(232, 17, 35)
                    : Color.FromArgb(229, 229, 229);
                using var bgBrush = new SolidBrush(bgColor);
                g.FillRectangle(bgBrush, rect);
            }

            var penColor = _hovered && _kind == ButtonKind.Close
                ? Color.White
                : Color.FromArgb(51, 51, 51);
            using var pen = new Pen(penColor, 1.2f);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int cx = rect.Width / 2;
            int cy = rect.Height / 2;
            const int s = 5;

            switch (_kind)
            {
                case ButtonKind.Minimize:
                    g.DrawLine(pen, cx - s, cy, cx + s, cy);
                    break;

                case ButtonKind.Maximize:
                    g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                    break;

                case ButtonKind.Restore:
                    g.DrawRectangle(pen, cx - s + 2, cy - s, s * 2 - 2, s * 2 - 2);
                    g.DrawLine(pen, cx - s, cy - s + 2, cx - s, cy + s);
                    g.DrawLine(pen, cx - s, cy + s, cx + s - 2, cy + s);
                    g.DrawLine(pen, cx - s, cy - s + 2, cx - s + 2, cy - s + 2);
                    break;

                case ButtonKind.Close:
                    g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                    g.DrawLine(pen, cx + s, cy - s, cx - s, cy + s);
                    break;
            }
        }
    }

    /// <summary>
    /// 许可证管理器 - 用于生成和验证机器码授权
    /// </summary>
    public class LicenseManager
    {
        private string _privateKeyPath;
        private string _publicKeyPath;

        public LicenseManager()
        {
            _privateKeyPath = "";
            _publicKeyPath = "";
        }

        public LicenseManager(string privateKeyPath, string publicKeyPath)
        {
            _privateKeyPath = privateKeyPath;
            _publicKeyPath = publicKeyPath;
        }

        public static LicenseManager FromKeyDirectory(string keyDirectory)
        {
            var manager = new LicenseManager();
            if (!string.IsNullOrEmpty(keyDirectory) && Directory.Exists(keyDirectory))
            {
                // 使用key目录下最新的key文件
                var keyFiles = Directory.GetFiles(keyDirectory, "*.key");
                if (keyFiles.Length > 0)
                {
                    // 按修改时间排序，取最新的
                    manager._privateKeyPath = keyFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
                    manager._publicKeyPath = Path.Combine(keyDirectory, Path.GetFileNameWithoutExtension(manager._privateKeyPath) + ".pub");
                }
            }
            return manager;
        }

        /// <summary>
        /// 创建许可证
        /// </summary>
        public LicenseInfo CreateLicense(string machineCode, int validDays)
        {
            var license = new LicenseInfo
            {
                MachineCode = machineCode,
                ExpirationDate = DateTime.Now.AddDays(validDays),
                IssueDate = DateTime.Now,
                LicenseKey = Guid.NewGuid().ToString("N")
            };

            // 签名
            SignLicense(license);

            return license;
        }

        /// <summary>
        /// 验证许可证
        /// </summary>
        public bool ValidateLicense(LicenseInfo license, string machineCode)
        {
            if (license == null) return false;
            if (license.MachineCode != machineCode) return false;
            if (license.ExpirationDate < DateTime.Now) return false;

            return VerifySignature(license);
        }

        /// <summary>
        /// 保存许可证到文件（手动构建JSON，避免依赖System.Text.Json）
        /// </summary>
        public void SaveLicense(LicenseInfo license, string filePath)
        {
            var json = new System.Text.StringBuilder();
            json.AppendLine("{");
            json.AppendLine($"  \"MachineCode\": \"{EscapeJsonString(license.MachineCode)}\",");
            json.AppendLine($"  \"ExpirationDate\": \"{license.ExpirationDate:O}\",");
            json.AppendLine($"  \"IssueDate\": \"{license.IssueDate:O}\",");
            json.AppendLine($"  \"LicenseKey\": \"{EscapeJsonString(license.LicenseKey)}\",");
            json.AppendLine($"  \"Signature\": \"{EscapeJsonString(license.Signature)}\"");
            json.AppendLine("}");
            File.WriteAllText(filePath, json.ToString());
        }

        /// <summary>
        /// 转义JSON字符串中的特殊字符
        /// </summary>
        private string EscapeJsonString(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            var sb = new System.Text.StringBuilder();
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append($"\\u{(int)c:X4}");
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// 从文件加载许可证（手动解析JSON，避免依赖System.Text.Json）
        /// </summary>
        public LicenseInfo LoadLicense(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            var json = File.ReadAllText(filePath);
            return ParseLicenseJsonManual(json);
        }

        /// <summary>
        /// 手动解析JSON格式的lic文件
        /// </summary>
        private LicenseInfo ParseLicenseJsonManual(string json)
        {
            try
            {
                var lic = new LicenseInfo();
                lic.MachineCode = ExtractJsonValueManual(json, "MachineCode") ?? ExtractJsonValueManual(json, "machineCode");
                lic.LicenseKey = ExtractJsonValueManual(json, "LicenseKey") ?? ExtractJsonValueManual(json, "licenseKey");
                lic.Signature = ExtractJsonValueManual(json, "Signature") ?? ExtractJsonValueManual(json, "signature");

                var expDateStr = ExtractJsonValueManual(json, "ExpirationDate") ?? ExtractJsonValueManual(json, "expirationDate");
                if (!string.IsNullOrEmpty(expDateStr))
                {
                    lic.ExpirationDate = DateTime.Parse(expDateStr);
                }

                var issueDateStr = ExtractJsonValueManual(json, "IssueDate") ?? ExtractJsonValueManual(json, "issueDate");
                if (!string.IsNullOrEmpty(issueDateStr))
                {
                    lic.IssueDate = DateTime.Parse(issueDateStr);
                }

                return lic;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 手动从JSON中提取字段值
        /// </summary>
        private string ExtractJsonValueManual(string json, string key)
        {
            try
            {
                string searchKey = "\"" + key + "\"";
                int keyPos = json.IndexOf(searchKey);
                if (keyPos < 0) return null;

                string afterKey = json.Substring(keyPos + searchKey.Length);
                int colonPos = afterKey.IndexOf(':');
                if (colonPos < 0) return null;

                string afterColon = afterKey.Substring(colonPos + 1).Trim();
                int quote1 = afterColon.IndexOf('"');
                if (quote1 < 0) return null;

                int quote2 = afterColon.IndexOf('"', quote1 + 1);
                if (quote2 < 0) return null;

                return afterColon.Substring(quote1 + 1, quote2 - quote1 - 1);
            }
            catch
            {
                return null;
            }
        }

        private void SignLicense(LicenseInfo license)
        {
            if (!File.Exists(_privateKeyPath)) return;

            var privateKeyXml = File.ReadAllText(_privateKeyPath);
            using var rsa = RSA.Create();
            rsa.FromXmlString(privateKeyXml);

            var data = $"{license.MachineCode}|{license.ExpirationDate:O}|{license.LicenseKey}";
            var dataBytes = System.Text.Encoding.UTF8.GetBytes(data);
            var signature = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            license.Signature = Convert.ToBase64String(signature);
        }

        private bool VerifySignature(LicenseInfo license)
        {
            if (string.IsNullOrEmpty(license.Signature)) return false;
            if (!File.Exists(_publicKeyPath)) return false;

            var publicKeyXml = File.ReadAllText(_publicKeyPath);
            using var rsa = RSA.Create();
            rsa.FromXmlString(publicKeyXml);

            var data = $"{license.MachineCode}|{license.ExpirationDate:O}|{license.LicenseKey}";
            var dataBytes = System.Text.Encoding.UTF8.GetBytes(data);
            var signature = Convert.FromBase64String(license.Signature);

            return rsa.VerifyData(dataBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
    }

    /// <summary>
    /// 许可证信息
    /// </summary>
    public class LicenseInfo
    {
        public string MachineCode { get; set; } = "";
        public DateTime ExpirationDate { get; set; }
        public DateTime IssueDate { get; set; }
        public string LicenseKey { get; set; } = "";
        public string Signature { get; set; } = "";
    }
}

