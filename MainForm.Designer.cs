using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace EncryptTools
{
    partial class MainForm
    {
        private IContainer components = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new Container();
            this.Text = "加密工具 (EncryptTools)";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new System.Drawing.Size(660, 350);
            this.MinimumSize = new System.Drawing.Size(560, 250);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 7,
                Padding = new System.Windows.Forms.Padding(8),
                AutoSize = true
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            // Row 0: Source
            var lblSource = new Label { Text = "源路径:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft };
            txtSourcePath = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            btnBrowseSource = new Button { Text = "浏览...", AutoSize = true };
            layout.Controls.Add(lblSource, 0, 0);
            layout.Controls.Add(txtSourcePath, 1, 0);
            layout.Controls.Add(btnBrowseSource, 2, 0);

            // Row 1: Output
            var lblOutput = new Label { Text = "输出路径(可选):", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft };
            txtOutputPath = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
            btnBrowseOutput = new Button { Text = "选择...", AutoSize = true };
            layout.Controls.Add(lblOutput, 0, 1);
            layout.Controls.Add(txtOutputPath, 1, 1);
            layout.Controls.Add(btnBrowseOutput, 2, 1);

            // Row 2: Options
            var optionsPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            chkInPlace = new CheckBox { Text = "源加密", AutoSize = true,Checked = true};
            chkRecursive = new CheckBox { Text = "递归处理", AutoSize = true,Checked = true };
            chkSelectFile = new CheckBox { Text = "选择文件", AutoSize = true, Checked = false, Margin = new Padding(20, 3, 3, 3) };
            optionsPanel.Controls.Add(chkInPlace);
            optionsPanel.Controls.Add(chkRecursive);
            optionsPanel.Controls.Add(chkSelectFile);
            layout.Controls.Add(new Label { Text = "选项:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
            layout.Controls.Add(optionsPanel, 1, 2);

            // Row 3: Algorithm + AES key size + iterations
            var lblAlgorithm = new Label { Text = "算法:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft };
            cmbAlgorithm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cmbAlgorithm.Items.AddRange(new object[] { "AES-CBC", "AES-GCM(小文件)", "TripleDES", "XOR(演示)" });
            cmbAlgorithm.SelectedIndex = 0;
            var algoPanel = new FlowLayoutPanel { AutoSize = true, Width = 500, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var lblKeySize = new Label { Text = "AES密钥长度:", AutoSize = true, Margin = new Padding(10, 3, 3, 3), TextAlign = ContentAlignment.MiddleLeft };
            cmbKeySize = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
            cmbKeySize.Items.AddRange(new object[] { "128", "192", "256" });
            cmbKeySize.SelectedIndex = 2;
            var lblIterations = new Label { Text = "迭代次数:", AutoSize = true, Margin = new Padding(10, 3, 3, 3), TextAlign = ContentAlignment.MiddleLeft };
            nudIterations = new NumericUpDown { Minimum = 10000, Maximum = 1000000, Value = 200000, Increment = 10000, Width = 70 };
            algoPanel.Controls.Add(cmbAlgorithm);
            algoPanel.Controls.Add(lblKeySize);
            algoPanel.Controls.Add(cmbKeySize);
            algoPanel.Controls.Add(lblIterations);
            algoPanel.Controls.Add(nudIterations);
            layout.Controls.Add(lblAlgorithm, 0, 3);
            layout.Controls.Add(algoPanel, 1, 3);

            // Row 4: Password
            var lblPassword = new Label { Text = "密码:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft };
            var passwordPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            txtPassword = new TextBox { UseSystemPasswordChar = true, Width = 200 };
            cmbPasswordType = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80, Margin = new Padding(10, 3, 3, 3) };
            cmbPasswordType.Items.AddRange(new object[] { "输入密码", "密码文件" });
            cmbPasswordType.SelectedIndex = 0;
            btnSavePassword = new Button { Text = "保存密码", AutoSize = true, Margin = new Padding(10, 3, 3, 3) };
            btnImportPassword = new Button { Text = "导入密码文件", AutoSize = true, Margin = new Padding(10, 3, 3, 3), Visible = false };
            passwordPanel.Controls.Add(txtPassword);
            passwordPanel.Controls.Add(cmbPasswordType);
            passwordPanel.Controls.Add(btnSavePassword);
            passwordPanel.Controls.Add(btnImportPassword);
            layout.Controls.Add(lblPassword, 0, 4);
            layout.Controls.Add(passwordPanel, 1, 4);

            // Row 5: Buttons
            var buttonsPanel = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            btnEncrypt = new Button { Text = "加密", AutoSize = true, Padding = new System.Windows.Forms.Padding(4, 1, 1, 1) };
            btnDecrypt = new Button { Text = "解密", AutoSize = true, Padding = new System.Windows.Forms.Padding(4, 1, 1, 1) };
            btnCancel = new Button { Text = "取消", AutoSize = true, Padding = new System.Windows.Forms.Padding(4, 1, 1, 1) };
            buttonsPanel.Controls.Add(btnEncrypt);
            buttonsPanel.Controls.Add(btnDecrypt);
            buttonsPanel.Controls.Add(btnCancel);
            layout.Controls.Add(new Label { Text = "操作:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
            layout.Controls.Add(buttonsPanel, 1, 5);

            // Row 6: Log
            var lblLog = new Label { Text = "日志:", AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.TopLeft };
            txtLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill
            };
            layout.Controls.Add(lblLog, 0, 6);
            layout.Controls.Add(txtLog, 1, 6);
            layout.SetColumnSpan(txtLog, 2);

            Controls.Add(layout);

            // Events
            btnBrowseSource.Click += (_, __) => BrowseSource();
            btnBrowseOutput.Click += (_, __) => BrowseOutput();
            btnEncrypt.Click += async (_, __) => await StartProcessAsync(true);
            btnDecrypt.Click += async (_, __) => await StartProcessAsync(false);
            btnCancel.Click += (_, __) => CancelProcessing();
            cmbAlgorithm.SelectedIndexChanged += (_, __) => UpdateKeySizeAvailability();
            cmbPasswordType.SelectedIndexChanged += (_, __) => UpdatePasswordTypeUI();
            txtPassword.TextChanged += (_, __) => OnPasswordTextChanged();
            btnSavePassword.Click += (_, __) => SavePassword();
            btnImportPassword.Click += (_, __) => ImportPasswordFile();
        }

        private TextBox txtSourcePath = null!;
        private Button btnBrowseSource = null!;
        private TextBox txtOutputPath = null!;
        private Button btnBrowseOutput = null!;
        private CheckBox chkInPlace = null!;
        private CheckBox chkRecursive = null!;
        private ComboBox cmbAlgorithm = null!;
        private TextBox txtPassword = null!;
        private NumericUpDown nudIterations = null!;
        private Button btnEncrypt = null!;
        private Button btnDecrypt = null!;
        private Button btnCancel = null!;
        private TextBox txtLog = null!;
        private ComboBox cmbKeySize = null!;
        private CheckBox chkSelectFile = null!;
        private ComboBox cmbPasswordType = null!;
        private Button btnSavePassword = null!;
        private Button btnImportPassword = null!;
    }
}