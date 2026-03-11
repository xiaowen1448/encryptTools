
using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using EncryptTools.Ui;

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
            public TextBox LogBox = null!;
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
            editMenu.DropDownItems.Add(new ToolStripMenuItem("设置(&S)...", null, (_, __) => ShowSettings()));

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
            var btnWelcomeNew = new Button
            {
                Text = "创建新加密工作区",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Regular),
                Location = new Point(46, 140)
            };
            btnWelcomeNew.Click += (_, __) => NewWorkspace("文件");
            welcomePanel.Controls.Add(title);
            welcomePanel.Controls.Add(subtitle);
            welcomePanel.Controls.Add(btnWelcomeNew);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));   // 工具栏，允许两行换行
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // 主区域
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));   // 底部状态条

            // 顶部工具栏：所有按钮、下拉横向排布，宽度不够自动换行
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                BackColor = SystemColors.ControlLight,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoScroll = false,
                Padding = new Padding(8, 8, 8, 4)
            };

            var btnSelect = new Button { Text = "选择文件/文件夹", AutoSize = true, Margin = new Padding(0, 0, 6, 4) };
            var lblDragHint = new Label { Text = "也可拖拽到列表中", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(0, 6, 16, 0) };

            var cbAlgo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(4, 4, 8, 4) };
            cbAlgo.Items.AddRange(new object[]
            {
                "AES-256-GCM",
                "AES-128-CBC",
                "ChaCha20-Poly1305",
                "SM4"
            });
            cbAlgo.SelectedIndex = 0;
            var cbKdf = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = new Padding(4, 4, 8, 4) };
            cbKdf.Items.AddRange(new object[] { "Argon2", "PBKDF2", "Scrypt" });
            cbKdf.SelectedIndex = 0;
            var lblAlgo = new Label { Text = "加密算法:", AutoSize = true, Margin = new Padding(8, 8, 4, 0) };
            var lblKdf = new Label { Text = "密钥派生:", AutoSize = true, Margin = new Padding(8, 8, 4, 0) };

            var btnEncrypt = new Button { Text = "执行加密", BackColor = Color.RoyalBlue, ForeColor = Color.White, AutoSize = true, Margin = new Padding(16, 0, 4, 4) };
            var btnDecrypt = new Button { Text = "执行解密", BackColor = Color.SeaGreen, ForeColor = Color.White, AutoSize = true, Margin = new Padding(4, 0, 4, 4) };
            var btnSaveCfg = new Button { Text = "保存设置", AutoSize = true, Margin = new Padding(8, 0, 4, 4) };
            var btnReset = new Button { Text = "重置", AutoSize = true, Margin = new Padding(4, 0, 4, 4) };

            // 按顺序加入，同一行空间不够时自动换到下一行
            toolbar.Controls.Add(btnSelect);
            toolbar.Controls.Add(lblDragHint);
            toolbar.Controls.Add(lblAlgo);
            toolbar.Controls.Add(cbAlgo);
            toolbar.Controls.Add(lblKdf);
            toolbar.Controls.Add(cbKdf);
            toolbar.Controls.Add(btnEncrypt);
            toolbar.Controls.Add(btnDecrypt);
            toolbar.Controls.Add(btnSaveCfg);
            toolbar.Controls.Add(btnReset);

            // 中间主区域：左列表 + 右配置（右侧最小宽度保证选项完整显示）
            var split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterWidth = 4
            };

            var lvFiles = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                AllowDrop = true
            };
            lvFiles.Columns.Add("名称", 160);
            lvFiles.Columns.Add("路径", 280);
            lvFiles.Columns.Add("大小", 80);
            lvFiles.Columns.Add("状态", 80);

            lvFiles.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
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

            split.Panel1.Controls.Add(lvFiles);

            // 右侧配置区：表格式布局，保证密钥/密码、输出路径+浏览、文件后缀等完整展示
            var rightConfig = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8), AutoScroll = true };
            var tblConfig = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Padding = Padding.Empty
            };
            tblConfig.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));   // 标签列
            tblConfig.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));  // 控件列宽度减半，避免输入框过长

            var txtPassword = new TextBox
            {
                Width = 140,
                Anchor = AnchorStyles.Left,
                UseSystemPasswordChar = true,
                Margin = new Padding(0, 2, 0, 6)
            };
            try { txtPassword.PlaceholderText = "输入密钥/密码"; } catch { }

            var txtOutput = new TextBox
            {
                Width = 100,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 2, 4, 6)
            };
            try { txtOutput.PlaceholderText = "留空则使用 output"; } catch { }

            var btnBrowseOut = new Button { Text = "浏览...", AutoSize = true, Anchor = AnchorStyles.Left };
            btnBrowseOut.Click += (_, __) =>
            {
                using var dlg = new FolderBrowserDialog { Description = "选择输出目录" };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtOutput.Text = dlg.SelectedPath;
            };

            var rowOutput = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 6), AutoSize = true };
            rowOutput.Controls.Add(txtOutput);
            rowOutput.Controls.Add(btnBrowseOut);

            var chkZip = new CheckBox { Text = "压缩输出（ZIP）", AutoSize = true, Margin = new Padding(0, 4, 0, 2) };
            var chkSelfExe = new CheckBox { Text = "自解压（.exe）", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            var chkOverwrite = new CheckBox { Text = "覆盖原文件（谨慎）", AutoSize = true, Margin = new Padding(0, 2, 0, 2) };

            var cbSuffix = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left, Width = 120 };
            cbSuffix.Items.AddRange(new object[] { ".enc", ".aes", ".secure", "自定义" });
            cbSuffix.SelectedIndex = 0;

            int r = 0;
            tblConfig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tblConfig.Controls.Add(new Label { Text = "密钥/密码：", AutoSize = true }, 0, r);
            tblConfig.Controls.Add(txtPassword, 1, r++);
            tblConfig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tblConfig.Controls.Add(new Label { Text = "输出路径：", AutoSize = true }, 0, r);
            tblConfig.Controls.Add(rowOutput, 1, r++);
            tblConfig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tblConfig.Controls.Add(new Label { Text = "文件后缀：", AutoSize = true }, 0, r);
            tblConfig.Controls.Add(cbSuffix, 1, r++);
            tblConfig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tblConfig.Controls.Add(chkZip, 0, r);
            tblConfig.SetColumnSpan(chkZip, 2);
            r++;
            tblConfig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tblConfig.Controls.Add(chkSelfExe, 0, r);
            tblConfig.SetColumnSpan(chkSelfExe, 2);
            r++;
            tblConfig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tblConfig.Controls.Add(chkOverwrite, 0, r);
            tblConfig.SetColumnSpan(chkOverwrite, 2);

            rightConfig.Controls.Add(tblConfig);
            split.Panel2.Controls.Add(rightConfig);

            // 根据实际宽度延后配置 SplitterDistance / Panel2MinSize，避免运行时异常
            ConfigureFileSplit(split);

            // 底部状态条（仅此页面内部）
            var bottomStatus = new Panel
            {
                Dock = DockStyle.Fill
            };
            var lblStatus = new Label { Text = "就绪", Dock = DockStyle.Left, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Width = 300 };
            var linkHelp = new LinkLabel { Text = "查看算法细节", Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight };
            linkHelp.LinkClicked += (_, __) => OpenHelp();
            bottomStatus.Controls.Add(linkHelp);
            bottomStatus.Controls.Add(lblStatus);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(split, 0, 1);
            root.Controls.Add(bottomStatus, 0, 2);

            tab.Controls.Add(root);

            var logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill
            };
            var ctx = new WorkspaceContext
            {
                Kind = "文件",
                SourcePath = null,
                LogBox = logBox
            };
            tab.Tag = ctx;

            // 事件绑定
            btnSelect.Click += (_, __) => SelectSourceForWorkspace(ctx);
            btnEncrypt.Click += async (_, __) => await ExecuteEncryptWorkspace(ctx);

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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.ControlLight };
            var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 220, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var btnPaste = new Button { Text = "从剪贴板粘贴", AutoSize = true };
            var btnClear = new Button { Text = "清空", AutoSize = true };
            leftButtons.Controls.Add(btnPaste);
            leftButtons.Controls.Add(btnClear);

            var middleOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            cbMode.Items.AddRange(new object[] { "对称（AES）", "非对称（RSA）", "混合（PGP）" });
            cbMode.SelectedIndex = 0;
            var cbEncoding = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            cbEncoding.Items.AddRange(new object[] { "Base64", "Hex", "URL编码", "Binary" });
            cbEncoding.SelectedIndex = 0;
            middleOptions.Controls.Add(new Label { Text = "加密模式:", AutoSize = true, Margin = new Padding(0, 10, 4, 0) });
            middleOptions.Controls.Add(cbMode);
            middleOptions.Controls.Add(new Label { Text = "编码输出:", AutoSize = true, Margin = new Padding(12, 10, 4, 0) });
            middleOptions.Controls.Add(cbEncoding);

            var rightButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 260, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var btnEncrypt = new Button { Text = "加密", BackColor = Color.RoyalBlue, ForeColor = Color.White, AutoSize = true };
            var btnDecrypt = new Button { Text = "解密", BackColor = Color.SeaGreen, ForeColor = Color.White, AutoSize = true };
            var btnCopyOut = new Button { Text = "复制输出", AutoSize = true };
            rightButtons.Controls.Add(btnCopyOut);
            rightButtons.Controls.Add(btnDecrypt);
            rightButtons.Controls.Add(btnEncrypt);

            toolbar.Controls.Add(rightButtons);
            toolbar.Controls.Add(middleOptions);
            toolbar.Controls.Add(leftButtons);

            var mainGrid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2
            };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var leftPanel = new Panel { Dock = DockStyle.Fill };
            var txtIn = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical };
            txtIn.PlaceholderText = "输入明文或密文";
            var cbAutoDetect = new CheckBox { Text = "自动检测格式（Base64/Hex/JSON等）", Dock = DockStyle.Bottom, AutoSize = true };
            leftPanel.Controls.Add(txtIn);
            leftPanel.Controls.Add(cbAutoDetect);

            var rightPanel = new Panel { Dock = DockStyle.Fill };
            var txtOut = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true };
            txtOut.PlaceholderText = "输出结果";
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

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            var toolbar = new Panel { Dock = DockStyle.Fill, BackColor = SystemColors.ControlLight };
            var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Left, Width = 260, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var btnSelectImg = new Button { Text = "选择图片", AutoSize = true };
            var lblHint = new Label { Text = "也可拖拽图片到预览区", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(8, 10, 0, 0) };
            leftButtons.Controls.Add(btnSelectImg);
            leftButtons.Controls.Add(lblHint);

            var middleOptions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var cbMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
            cbMode.Items.AddRange(new object[] { "不可逆马赛克", "像素置乱", "像素XOR", "Arnold猫映射" });
            cbMode.SelectedIndex = 1;
            var cbBlock = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
            cbBlock.Items.AddRange(new object[] { "4x4", "8x8", "16x16", "32x32", "自定义" });
            cbBlock.SelectedIndex = 2;
            middleOptions.Controls.Add(new Label { Text = "像素化模式:", AutoSize = true, Margin = new Padding(0, 10, 4, 0) });
            middleOptions.Controls.Add(cbMode);
            middleOptions.Controls.Add(new Label { Text = "块大小:", AutoSize = true, Margin = new Padding(12, 10, 4, 0) });
            middleOptions.Controls.Add(cbBlock);

            var rightButtons = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 300, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
            var btnEncrypt = new Button { Text = "加密", BackColor = Color.RoyalBlue, ForeColor = Color.White, AutoSize = true };
            var btnDecrypt = new Button { Text = "解密", BackColor = Color.SeaGreen, ForeColor = Color.White, AutoSize = true };
            var btnSaveOut = new Button { Text = "保存输出", AutoSize = true };
            var btnCompare = new Button { Text = "预览对比", AutoSize = true };
            rightButtons.Controls.Add(btnCompare);
            rightButtons.Controls.Add(btnSaveOut);
            rightButtons.Controls.Add(btnDecrypt);
            rightButtons.Controls.Add(btnEncrypt);

            toolbar.Controls.Add(rightButtons);
            toolbar.Controls.Add(middleOptions);
            toolbar.Controls.Add(leftButtons);

            var mainGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            mainGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            var leftPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                AllowDrop = true
            };
            var lblLeft = new Label { Text = "原图预览：拖拽或选择图片加载", ForeColor = Color.DimGray, Dock = DockStyle.Top, Height = 20 };
            var leftContainer = new Panel { Dock = DockStyle.Fill };
            leftContainer.Controls.Add(leftPreview);
            leftContainer.Controls.Add(lblLeft);

            leftPreview.DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effect = DragDropEffects.Copy;
                }
            };
            leftPreview.DragDrop += (s, e) =>
            {
                if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                if (e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    TryLoadImage(leftPreview, paths[0]);
                }
            };

            var rightPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle
            };
            var lblRight = new Label { Text = "加密图预览", ForeColor = Color.DimGray, Dock = DockStyle.Top, Height = 20 };
            var rightContainer = new Panel { Dock = DockStyle.Fill };
            rightContainer.Controls.Add(rightPreview);
            rightContainer.Controls.Add(lblRight);

            mainGrid.Controls.Add(leftContainer, 0, 0);
            mainGrid.Controls.Add(rightContainer, 1, 0);

            var bottomStatus = new Panel { Dock = DockStyle.Fill };
            var lblStatus = new Label { Text = "就绪", Dock = DockStyle.Left, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Width = 260 };
            bottomStatus.Controls.Add(lblStatus);

            root.Controls.Add(toolbar, 0, 0);
            root.Controls.Add(mainGrid, 0, 1);
            root.Controls.Add(bottomStatus, 0, 2);

            tab.Controls.Add(root);

            var logBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
            var ctx = new WorkspaceContext { Kind = "图片", SourcePath = null, LogBox = logBox };
            tab.Tag = ctx;

            btnSelectImg.Click += (_, __) =>
            {
                using var dlg = new OpenFileDialog { Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*" };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    TryLoadImage(leftPreview, dlg.FileName);
                }
            };
            btnEncrypt.Click += (_, __) =>
            {
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 图片加密占位执行。{Environment.NewLine}");
            };
            btnDecrypt.Click += (_, __) =>
            {
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 图片解密占位执行。{Environment.NewLine}");
            };

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

                // 右侧期望至少 260 像素，如果整体宽度太小则保持默认，避免异常
                const int rightPreferredMin = 260;
                if (split.Width <= rightPreferredMin + split.Panel1MinSize + split.SplitterWidth)
                    return;

                int desiredLeft = (int)(split.Width * 0.55);
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
                var item = new ListViewItem(new[] { name, path, sizeText, "就绪" });
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
            _statusLeft.Text = "执行解密（占位，仅结构）";
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

        private async System.Threading.Tasks.Task ExecuteEncryptWorkspace(WorkspaceContext ctx)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(ctx.SourcePath))
                {
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 请先选择源文件。{Environment.NewLine}");
                    _statusLeft.Text = "未选择源文件。";
                    return;
                }

                string password = PromptPassword();
                if (string.IsNullOrWhiteSpace(password))
                {
                    ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 已取消：密码为空。{Environment.NewLine}");
                    return;
                }

                string source = ctx.SourcePath;
                string baseDir = System.IO.Path.GetDirectoryName(source) ?? System.IO.Path.GetPathRoot(source)!;
                string outDir = System.IO.Path.Combine(baseDir, "output");
                System.IO.Directory.CreateDirectory(outDir);

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 开始加密: {source}{Environment.NewLine}");
                _statusLeft.Text = "执行加密中…";

                var options = new FileEncryptorOptions
                {
                    SourcePath = source,
                    OutputRoot = outDir,
                    InPlace = false,
                    Recursive = false,
                    RandomizeFileName = false,
                    Algorithm = CryptoAlgorithm.AesCbc,
                    Password = password,
                    Iterations = 200_000,
                    AesKeySizeBits = 256,
                    Log = msg =>
                    {
                        if (string.IsNullOrWhiteSpace(msg)) return;
                        ctx.LogBox.Invoke(new Action(() =>
                        {
                            ctx.LogBox.AppendText(msg + Environment.NewLine);
                        }));
                    }
                };

                var enc = new FileEncryptor(options);
                var dummyProgress = new System.Progress<double>(_ => { });
                await enc.EncryptAsync(dummyProgress, System.Threading.CancellationToken.None);

                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 加密完成，输出目录: {outDir}{Environment.NewLine}");
                _statusLeft.Text = "加密完成。";
            }
            catch (Exception ex)
            {
                ctx.LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] 加密失败: {ex.Message}{Environment.NewLine}");
                _statusLeft.Text = "加密失败。";
            }
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

        private void ShowSettings()
        {
            MessageBox.Show(this, "设置对话框占位。", "设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

