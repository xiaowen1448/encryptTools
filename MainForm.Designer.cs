using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using EncryptTools.Ui;

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
            this.Text = "encryptTools";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Size = new System.Drawing.Size(950, 680);
            this.MinimumSize = new System.Drawing.Size(820, 560);
            this.AutoScaleMode = AutoScaleMode.Font;
            this.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = true;
            this.MinimizeBox = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(24, 20, 24, 20)
            };

            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // header
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // cards
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status

            // Header
            var header = new Panel { Dock = DockStyle.Top, Height = 110 };
            _lblTitle = new Label
            {
                Text = "快速加密 / 解密",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 28f, FontStyle.Bold, GraphicsUnit.Point),
                Location = new Point(0, 0)
            };
            _lblSubTitle = new Label
            {
                Text = "支持文件 · 字符串 · 图片像素化 · 常见规则智能解密",
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = Color.DimGray,
                Location = new Point(2, 60)
            };
            header.Controls.Add(_lblTitle);
            header.Controls.Add(_lblSubTitle);

            // Cards grid (2 columns)
            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(0, 10, 0, 10)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));

            _cardString = new FluentCard
            {
                Dock = DockStyle.Fill,
                IconText = "\uE8C8", // Document with text
                TitleText = "字符串 / 明文加密解密",
                PrimaryButtonText = "加密当前剪贴板",
                HintText = "支持粘贴 · Base64 · AES · 自动检测"
            };
            _cardFile = new FluentCard
            {
                Dock = DockStyle.Fill,
                IconText = "\uE8A5", // Document
                TitleText = "文件加密 / 解密",
                PrimaryButtonText = "选择文件或拖拽这里",
                HintText = "支持批量文件 · 断点续传式处理 · 失败自动跳过"
            };
            _cardImage = new FluentCard
            {
                Dock = DockStyle.Fill,
                IconText = "\uEB9F", // Image
                TitleText = "图片像素化加密",
                PrimaryButtonText = "选择图片开始像素化",
                HintText = "适合分享预览 · 可恢复 · 适配常见格式"
            };
            _cardSmart = new FluentCard
            {
                Dock = DockStyle.Fill,
                IconText = "\uE72E", // Key
                TitleText = "常见规则智能解密",
                PrimaryButtonText = "输入密文 → 智能尝试",
                HintText = "自动尝试常见编码/规则 · 输出最可能结果"
            };

            grid.Controls.Add(_cardString, 0, 0);
            grid.Controls.Add(_cardFile, 1, 0);
            grid.Controls.Add(_cardImage, 0, 1);
            grid.Controls.Add(_cardSmart, 1, 1);

            // Status bar
            var status = new StatusStrip { Dock = DockStyle.Bottom, SizingGrip = true };
            _statusLeft = new ToolStripStatusLabel("就绪") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            _statusRight = new ToolStripStatusLabel("v0.1") { TextAlign = ContentAlignment.MiddleRight };
            status.Items.Add(_statusLeft);
            status.Items.Add(_statusRight);

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(grid, 0, 1);
            root.Controls.Add(status, 0, 2);

            Controls.Add(root);
        }

        private Label _lblTitle = null!;
        private Label _lblSubTitle = null!;
        private FluentCard _cardString = null!;
        private FluentCard _cardFile = null!;
        private FluentCard _cardImage = null!;
        private FluentCard _cardSmart = null!;
        private ToolStripStatusLabel _statusLeft = null!;
        private ToolStripStatusLabel _statusRight = null!;
    }
}