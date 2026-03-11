using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace EncryptTools.Ui
{
    internal sealed class FluentCard : Panel
    {
        private readonly Label _lblIcon;
        private readonly Label _lblTitle;
        private readonly Button _btnPrimary;
        private readonly Label _lblHint;

        private bool _hover;

        public string IconText { get => _lblIcon.Text; set => _lblIcon.Text = value ?? ""; }
        public string TitleText { get => _lblTitle.Text; set => _lblTitle.Text = value ?? ""; }
        public string HintText { get => _lblHint.Text; set => _lblHint.Text = value ?? ""; }
        public string PrimaryButtonText { get => _btnPrimary.Text; set => _btnPrimary.Text = value ?? ""; }
        public bool PrimaryEnabled { get => _btnPrimary.Enabled; set => _btnPrimary.Enabled = value; }

        public event EventHandler? PrimaryClick;
        public event DragEventHandler? FileDropped;

        public FluentCard()
        {
            DoubleBuffered = true;
            BackColor = Color.FromArgb(245, 245, 245);
            ForeColor = Color.Black;
            Padding = new Padding(16);
            Margin = new Padding(10);

            _lblIcon = new Label
            {
                AutoSize = false,
                Width = 32,
                Height = 32,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe Fluent Icons", 18f, FontStyle.Regular, GraphicsUnit.Point),
            };
            _lblTitle = new Label
            {
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold, GraphicsUnit.Point),
            };
            _btnPrimary = new Button
            {
                AutoSize = true,
                FlatStyle = FlatStyle.System,
                Padding = new Padding(6, 2, 6, 2),
            };
            _lblHint = new Label
            {
                AutoSize = true,
                ForeColor = Color.DimGray,
                MaximumSize = new Size(520, 0),
            };

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            top.Controls.Add(_lblIcon);
            top.Controls.Add(new Panel { Width = 8, Height = 1 });
            top.Controls.Add(_lblTitle);

            var bottom = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = false,
                WrapContents = true,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(0),
                Padding = new Padding(0),
            };
            bottom.Controls.Add(new Panel { Height = 10, Width = 1 });
            bottom.Controls.Add(_btnPrimary);
            bottom.Controls.Add(new Panel { Height = 6, Width = 1 });
            bottom.Controls.Add(_lblHint);

            Controls.Add(bottom);
            Controls.Add(top);

            _btnPrimary.Click += (_, __) => PrimaryClick?.Invoke(this, EventArgs.Empty);

            SetStyle(ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;

            AllowDrop = true;
            DragEnter += (_, e) =>
            {
                if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += (_, e) => FileDropped?.Invoke(this, e);

            MouseEnter += (_, __) => { _hover = true; Invalidate(); };
            MouseLeave += (_, __) => { _hover = false; Invalidate(); };
            foreach (Control c in Controls)
            {
                c.MouseEnter += (_, __) => { _hover = true; Invalidate(); };
                c.MouseLeave += (_, __) => { _hover = false; Invalidate(); };
            }
        }

        public void ApplyTheme(bool dark)
        {
            BackColor = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(245, 245, 245);
            ForeColor = dark ? Color.Gainsboro : Color.Black;
            _lblTitle.ForeColor = ForeColor;
            _lblIcon.ForeColor = dark ? Color.White : Color.Black;
            _lblHint.ForeColor = dark ? Color.FromArgb(170, 170, 170) : Color.DimGray;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = ClientRectangle;
            rect.Width -= 1;
            rect.Height -= 1;

            int radius = 12;
            using var path = RoundedRect(rect, radius);

            // shadow
            var shadowRect = rect;
            shadowRect.Offset(0, _hover ? 6 : 4);
            using (var shadowPath = RoundedRect(shadowRect, radius))
            using (var shadowBrush = new SolidBrush(Color.FromArgb(_hover ? 70 : 50, 0, 0, 0)))
            {
                e.Graphics.FillPath(shadowBrush, shadowPath);
            }

            using (var fill = new SolidBrush(BackColor))
                e.Graphics.FillPath(fill, path);

            using (var pen = new Pen(Color.FromArgb(_hover ? 90 : 60, 0, 0, 0), 1f))
                e.Graphics.DrawPath(pen, path);

            base.OnPaint(e);
        }

        private static GraphicsPath RoundedRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}

