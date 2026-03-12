using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace EncryptTools.PasswordFile
{
    /// <summary>
    /// 导入密码文件：上方文件列表（程序 pwd 目录 + 浏览添加），下方明文密码展示；支持拖拽；导入到程序固定目录。
    /// </summary>
    internal sealed class ImportPasswordFileForm : Form
    {
        private readonly string _pwdDir;
        private readonly ListBox _listFiles;
        private readonly TextBox _txtPlain;
        private string? _currentPath;

        public string? ImportedFilePath { get; private set; }

        public ImportPasswordFileForm(string pwdDir)
        {
            _pwdDir = pwdDir;
            Text = "导入 / 查看密码文件";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(480, 360);
            MinimumSize = new Size(400, 280);
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            AllowDrop = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

            var lblTop = new Label { Text = "密码文件列表（程序 pwd 目录 + 可浏览添加）", AutoSize = true };
            _listFiles = new ListBox { Dock = DockStyle.Fill, DisplayMember = "Display", ValueMember = "Path", IntegralHeight = true };
            _listFiles.SelectedIndexChanged += ListFiles_SelectedIndexChanged;

            var pnlBtn = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Height = 32, WrapContents = false };
            var btnBrowse = new Button { Text = "浏览添加...", AutoSize = true, MinimumSize = new Size(96, 26) };
            btnBrowse.Click += (_, __) =>
            {
                using var dlg = new OpenFileDialog
                {
                    Title = "选择 .pwd 文件",
                    Filter = "密码文件 (*.pwd)|*.pwd|所有文件 (*.*)|*.*"
                };
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    AddPath(dlg.FileName);
                    SelectLastAddedFile();
                }
            };
            var btnRefresh = new Button { Text = "刷新列表", AutoSize = true, MinimumSize = new Size(80, 26) };
            btnRefresh.Click += (_, __) => RefreshList();
            pnlBtn.Controls.Add(btnBrowse);
            pnlBtn.Controls.Add(btnRefresh);
            pnlBtn.Controls.Add(new Panel { Width = 8 });

            var lblBottom = new Label { Text = "密码明文：", AutoSize = true };
            _txtPlain = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };

            var row3 = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill };
            row3.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            row3.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row3.Controls.Add(lblBottom, 0, 0);
            row3.Controls.Add(_txtPlain, 0, 1);

            root.Controls.Add(lblTop, 0, 0);
            root.Controls.Add(_listFiles, 0, 1);
            root.Controls.Add(pnlBtn, 0, 2);
            root.Controls.Add(row3, 0, 3);

            var btnImport = new Button { Text = "导入", DialogResult = DialogResult.None, AutoSize = true };
            var btnClose = new Button { Text = "关闭", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnBar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40 };
            btnBar.Controls.Add(btnClose);
            btnBar.Controls.Add(btnImport);

            btnImport.Click += (_, __) =>
            {
                // 导入当前选中的密码文件（浏览添加后已自动选中）
                var path = _currentPath ?? (_listFiles.SelectedItem is PwdEntry e ? e.Path : null);
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    MessageBox.Show(this, "请先选择要导入的 .pwd 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (!path.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "请选择 .pwd 格式文件。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var fullPath = Path.GetFullPath(path);
                var fullPwdDir = Path.GetFullPath(_pwdDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullPath.StartsWith(fullPwdDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.Equals(fullPwdDir, StringComparison.OrdinalIgnoreCase))
                {
                    ImportedFilePath = path;
                    MessageBox.Show(this, "该文件已在程序 pwd 目录中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                PasswordFileService.EnsurePwdDirectory();
                var fileName = Path.GetFileName(path);
                var dest = Path.Combine(_pwdDir, fileName);
                if (string.Equals(Path.GetFullPath(dest), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    ImportedFilePath = dest;
                    MessageBox.Show(this, "该文件已在程序 pwd 目录中。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                try
                {
                    File.Copy(path, dest, true);
                    ImportedFilePath = dest;
                    RefreshList();
                    MessageBox.Show(this, "已导入到程序 pwd 目录。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导入失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            };

            DragEnter += (s, e) =>
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                    e.Effect = DragDropEffects.Copy;
            };
            DragDrop += (s, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
                {
                    AddPath(paths[0]);
                    SelectLastAddedFile();
                }
            };

            Controls.Add(root);
            Controls.Add(btnBar);
            CancelButton = btnClose;
            Load += (_, __) => RefreshList();
        }

        private sealed class PwdEntry
        {
            public string Display { get; set; } = "";
            public string Path { get; set; } = "";
        }

        private void RefreshList()
        {
            var sel = _listFiles.SelectedItem as PwdEntry;
            _listFiles.Items.Clear();
            var files = PasswordFileService.ListPwdFiles();
            foreach (var f in files)
                _listFiles.Items.Add(new PwdEntry { Display = Path.GetFileName(f), Path = f });
            if (sel != null)
            {
                for (int i = 0; i < _listFiles.Items.Count; i++)
                {
                    if ((_listFiles.Items[i] as PwdEntry)?.Path == sel.Path)
                    {
                        _listFiles.SelectedIndex = i;
                        break;
                    }
                }
            }
        }

        private void AddPath(string path)
        {
            if (!File.Exists(path) || !path.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase)) return;
            for (int i = 0; i < _listFiles.Items.Count; i++)
            {
                if ((_listFiles.Items[i] as PwdEntry)?.Path?.Equals(path, StringComparison.OrdinalIgnoreCase) == true)
                    return;
            }
            _listFiles.Items.Add(new PwdEntry { Display = Path.GetFileName(path) + " (外部)", Path = path });
        }

        /// <summary>
        /// 浏览添加后自动选中刚添加的密码文件，便于直接点击导入。
        /// </summary>
        private void SelectLastAddedFile()
        {
            if (_listFiles.Items.Count > 0)
            {
                _listFiles.SelectedIndex = _listFiles.Items.Count - 1;
            }
        }

        private void ListFiles_SelectedIndexChanged(object? sender, EventArgs e)
        {
            _txtPlain.Clear();
            _currentPath = null;
            if (_listFiles.SelectedItem is not PwdEntry entry || string.IsNullOrEmpty(entry.Path)) return;
            _currentPath = entry.Path;
            if (!File.Exists(entry.Path)) return;
            try
            {
                _txtPlain.Text = PasswordFileHelper.LoadPasswordFromFile(entry.Path);
            }
            catch (Exception ex)
            {
                _txtPlain.Text = "[无法读取: " + ex.Message + "]";
            }
        }
    }
}
