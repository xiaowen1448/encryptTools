using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EncryptTools.Desktop.Input;
using EncryptTools.Desktop.Ui;

namespace EncryptTools.Desktop.Views;

public partial class StringWorkspaceView : UserControl
{
    private readonly TextBox _log;

    public StringWorkspaceView(TextBox logBox)
    {
        InitializeComponent();
        _log = logBox;
        CbMode.Items.Add("对称（AES）");
        CbMode.Items.Add("非对称（RSA）");
        CbMode.Items.Add("混合（PGP）");
        CbMode.SelectedIndex = 0;
        CbEncoding.Items.Add("Base64");
        CbEncoding.Items.Add("Hex");
        CbEncoding.Items.Add("URL编码");
        CbEncoding.Items.Add("Binary");
        CbEncoding.SelectedIndex = 0;

        BtnPaste.Click += async (_, _) => await PasteFromClipboardAsync();
        BtnClear.Click += (_, _) => { TxtIn.Text = ""; TxtOut.Text = ""; };
        BtnEncrypt.Click += (_, _) => RunEncryptSim();
        BtnDecrypt.Click += (_, _) => RunDecryptSim();
        BtnCopyOut.Click += async (_, _) => await CopyOutAsync();
        BtnSave.Click += async (_, _) => await SaveOutAsync();

        // 拖放支持：允许拖入文本文件
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(TxtIn, true);
        DragDropCompat.AttachStandardFileDrop(this, p => ApplyDroppedPathsAsync(p));
        DragDropCompat.AttachTargetDragOverCopy(TxtIn);

        // 拖入视觉反馈
        TxtIn.AddHandler(DragDrop.DragEnterEvent, (s, e) =>
        {
            TxtIn.Background = new SolidColorBrush(Color.Parse("#F0F8FF"));
            TxtIn.BorderBrush = new SolidColorBrush(Color.Parse("#4169E1"));
            TxtIn.BorderThickness = new Avalonia.Thickness(2);
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        TxtIn.AddHandler(DragDrop.DragLeaveEvent, (s, e) =>
        {
            TxtIn.Background = new SolidColorBrush(Colors.White);
            TxtIn.BorderBrush = new SolidColorBrush(Color.Parse("#CCCCCC"));
            TxtIn.BorderThickness = new Avalonia.Thickness(1);
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        TxtIn.AddHandler(DragDrop.DropEvent, (s, e) =>
        {
            TxtIn.Background = new SolidColorBrush(Colors.White);
            TxtIn.BorderBrush = new SolidColorBrush(Color.Parse("#CCCCCC"));
            TxtIn.BorderThickness = new Avalonia.Thickness(1);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private async void ApplyDroppedPathsAsync(IReadOnlyList<string> paths)
    {
        foreach (var raw in paths)
        {
            var resolved = DragDropPaths.TryResolveToExistingLocalPath(raw);
            if (string.IsNullOrEmpty(resolved)) continue;
            if (!File.Exists(resolved)) continue;

            try
            {
                var ext = Path.GetExtension(resolved).ToLowerInvariant();
                var textExtensions = new[] { ".txt", ".json", ".xml", ".csv", ".log", ".md", ".html", ".ts", ".cs", ".py", ".js" };
                if (!textExtensions.Contains(ext))
                {
                    AppendLog($"[拖入·字符串工作区] 跳过非文本文件: {Path.GetFileName(resolved)}");
                    continue;
                }

                var content = await File.ReadAllTextAsync(resolved, Encoding.UTF8);
                TxtIn.Text = content;
                AppendLog($"[拖入·字符串工作区] 已加载: {Path.GetFileName(resolved)} ({content.Length} 字符)");
                return;
            }
            catch (Exception ex)
            {
                AppendLog($"[拖入·字符串工作区] 读取失败: {Path.GetFileName(resolved)} - {ex.Message}");
            }
        }
    }

    private async Task PasteFromClipboardAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard == null) return;
        var t = await top.Clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(t))
            TxtIn.Text = t;
    }

    private void RunEncryptSim()
    {
        TxtOut.Text = $"[加密模拟] {TxtIn.Text}";
        AppendLog("字符串加密占位执行完成。");
    }

    private void RunDecryptSim()
    {
        TxtOut.Text = $"[解密模拟] {TxtIn.Text}";
        AppendLog("字符串解密占位执行完成。");
    }

    private async Task CopyOutAsync()
    {
        if (string.IsNullOrEmpty(TxtOut.Text)) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard != null)
            await top.Clipboard.SetTextAsync(TxtOut.Text);
    }

    private async Task SaveOutAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var path = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存输出结果",
            SuggestedFileName = "output.txt",
            DefaultExtension = "txt"
        });
        if (path == null) return;
        await using var s = await path.OpenWriteAsync();
        var bytes = System.Text.Encoding.UTF8.GetBytes(TxtOut.Text ?? "");
        await s.WriteAsync(bytes);
    }

    private void AppendLog(string line)
    {
        void Do()
        {
            _log.Text += $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
        }
        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }
}
