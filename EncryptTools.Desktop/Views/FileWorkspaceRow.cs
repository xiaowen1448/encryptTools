using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using Avalonia.Media;
using EncryptTools;

namespace EncryptTools.Desktop.Views;

/// <summary>文件工作区列表行：与 Windows 版 ListView 列（名称/路径/大小/状态/进度）对齐，并增加「类型」列。</summary>
public sealed class FileWorkspaceRow : INotifyPropertyChanged
{
    private static readonly IBrush EncryptProgressBrush = new SolidColorBrush(Color.FromRgb(0xD3, 0x32, 0x2F));
    private static readonly IBrush DecryptProgressBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));

    private string _displayName = "";
    private string _fullPath = "";
    private string _sizeText = "";
    private string _typeText = "";
    private string _statusText = "";
    private string _progressLabel = "-";
    private double _progressPercent = -1;
    private bool _isDecryptMode;

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName == value) return; _displayName = value; OnPropertyChanged(nameof(DisplayName)); }
    }

    public string FullPath
    {
        get => _fullPath;
        set { if (_fullPath == value) return; _fullPath = value; OnPropertyChanged(nameof(FullPath)); }
    }

    public string SizeText
    {
        get => _sizeText;
        set { if (_sizeText == value) return; _sizeText = value; OnPropertyChanged(nameof(SizeText)); }
    }

    public string TypeText
    {
        get => _typeText;
        set { if (_typeText == value) return; _typeText = value; OnPropertyChanged(nameof(TypeText)); }
    }

    public string StatusText
    {
        get => _statusText;
        set { if (_statusText == value) return; _statusText = value; OnPropertyChanged(nameof(StatusText)); }
    }

    public string ProgressLabel
    {
        get => _progressLabel;
        set { if (_progressLabel == value) return; _progressLabel = value; OnPropertyChanged(nameof(ProgressLabel)); }
    }

    /// <summary>0–100 有效时显示进度条；否则仅显示 ProgressLabel（如「-」）。</summary>
    public double ProgressPercent
    {
        get => _progressPercent;
        set
        {
            if (Math.Abs(_progressPercent - value) < 0.01) return;
            _progressPercent = value;
            OnPropertyChanged(nameof(ProgressPercent));
            OnPropertyChanged(nameof(ShowProgressBar));
            OnPropertyChanged(nameof(ShowIdleLabel));
            OnPropertyChanged(nameof(ShowEncryptProgressBar));
            OnPropertyChanged(nameof(ShowDecryptProgressBar));
            OnPropertyChanged(nameof(ProgressBarValue));
        }
    }

    /// <summary>供 ProgressBar.Value：&lt;0 时按 0，避免 RangeBase 收到无效值。</summary>
    public double ProgressBarValue => _progressPercent < 0 ? 0 : _progressPercent;

    public bool ShowProgressBar => _progressPercent >= 0;

    /// <summary>加密进度条可见：有进度且非解密模式（红色条）。</summary>
    public bool ShowEncryptProgressBar => _progressPercent >= 0 && !_isDecryptMode;

    /// <summary>解密进度条可见：有进度且为解密模式（绿色条）。</summary>
    public bool ShowDecryptProgressBar => _progressPercent >= 0 && _isDecryptMode;

    /// <summary>未开始进度时显示「-」，与 ShowProgressBar 互斥。</summary>
    public bool ShowIdleLabel => _progressPercent < 0;

    public bool IsDecryptMode
    {
        get => _isDecryptMode;
        set
        {
            if (_isDecryptMode == value) return;
            _isDecryptMode = value;
            OnPropertyChanged(nameof(IsDecryptMode));
            OnPropertyChanged(nameof(ProgressBrush));
            OnPropertyChanged(nameof(ShowEncryptProgressBar));
            OnPropertyChanged(nameof(ShowDecryptProgressBar));
        }
    }

    public IBrush ProgressBrush => _isDecryptMode ? DecryptProgressBrush : EncryptProgressBrush;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static FileWorkspaceRow FromPath(string path)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name))
            name = full;

        string sizeText;
        string typeText;
        string statusText;

        if (Directory.Exists(full))
        {
            sizeText = "<文件夹>";
            typeText = "文件夹";
            statusText = "正常";
            try
            {
                if (Directory.GetFiles(full, "*.*", SearchOption.AllDirectories).Any(f => CryptoService.IsWxEncryptedFile(f)))
                    statusText = "已加密";
            }
            catch { /* ignore */ }
        }
        else if (File.Exists(full))
        {
            long len;
            try { len = new FileInfo(full).Length; }
            catch { len = 0; }
            sizeText = FormatSizeBytes(len);
            var ext = Path.GetExtension(full);
            typeText = string.IsNullOrEmpty(ext) ? "—" : ext.TrimStart('.').ToUpperInvariant();
            statusText = CryptoService.IsWxEncryptedFile(full) ? "已加密" : "正常";
        }
        else
        {
            sizeText = "—";
            typeText = "—";
            statusText = "—";
        }

        return new FileWorkspaceRow
        {
            DisplayName = name,
            FullPath = full,
            SizeText = sizeText,
            TypeText = typeText,
            StatusText = statusText,
            ProgressLabel = "-",
            ProgressPercent = -1
        };
    }

    /// <summary>路径变化后刷新列表元数据（名称/大小/类型/状态）。不修改进度与红绿条，以便加密完成后保持红 100%、解密完成后保持绿 100%。</summary>
    public void RefreshFromPath(string newFullPath)
    {
        var row = FromPath(newFullPath);
        DisplayName = row.DisplayName;
        FullPath = row.FullPath;
        SizeText = row.SizeText;
        TypeText = row.TypeText;
        StatusText = row.StatusText;
    }

    private static string FormatSizeBytes(long len)
    {
        if (len < 1024)
            return $"{len} B";
        if (len < 1024 * 1024)
            return $"{len / 1024} KB";
        return $"{len / (1024.0 * 1024):0.##} MB";
    }
}
