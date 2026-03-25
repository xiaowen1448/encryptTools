using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
