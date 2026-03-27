using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EncryptTools;
using EncryptTools.Desktop.Input;
using EncryptTools.Desktop.Workspace;
using EncryptTools.PasswordFile;

namespace EncryptTools.Desktop.Views;

public partial class FileWorkspaceView : UserControl
{
    private readonly TextBox? _log;
    private readonly Action<string>? _setStatus;
    private readonly ObservableCollection<FileWorkspaceRow> _rows = new();
    private CancellationTokenSource? _cts;
    private string? _lastNonInPlaceOutputRoot;

    /// <summary>供 XAML 加载器 / 设计器（无参构造）；运行时应使用 <see cref="FileWorkspaceView(TextBox?, Action{string}?)" />。</summary>
    public FileWorkspaceView() : this(null, null)
    {
    }

    public FileWorkspaceView(TextBox? logBox, Action<string>? setStatus = null)
    {
        InitializeComponent();
        _log = logBox;
        _setStatus = setStatus;
        FilePathsGrid.ItemsSource = _rows;
        Loaded += (_, _) => DragDropCompat.EnableAllowDropRecursive(this);
        _rows.CollectionChanged += OnRowsCollectionChanged;

        CbAlgo.Items.Add("AES-256-GCM");
        CbAlgo.Items.Add("AES-128-CBC");
        CbAlgo.Items.Add("ChaCha20-Poly1305");
        CbAlgo.Items.Add("SM4");
        CbAlgo.SelectedIndex = Environment.Version.Major >= 8 ? 0 : 1;

        foreach (var s in new[] { ".enc1", ".enc2", ".enc", ".aes", ".bin", ".dat", ".secure" })
            CbSuffix.Items.Add(s);
        CbSuffix.SelectedIndex = 0;

        RefreshPwdCombo();

        BtnAddFiles.Click += async (_, _) => await AddFilesAsync();
        BtnAddFolder.Click += async (_, _) => await AddFolderAsync();
        BtnClear.Click += (_, _) => _rows.Clear();
        BtnPastePaths.Click += async (_, _) => await PastePathsFromClipboardAsync();
        BtnEncrypt.Click += async (_, _) => await RunEncryptAsync();
        BtnDecrypt.Click += async (_, _) => await RunDecryptAsync();
        BtnCancel.Click += (_, _) => CancelRun();

        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(DropZone, true);
        DragDrop.SetAllowDrop(FilePathsGrid, true);
        // 本地 Bubble + handledEventsToo：ListBox 等可能吞掉 Drop 或 DragOver 置为 None，仅依赖主窗口转发不可靠
        DragDropCompat.AttachStandardFileDrop(this, p => ApplyDroppedPaths(p, PathImportKind.DragDrop));
        DragDropCompat.AttachTargetDragOverCopy(FilePathsGrid);

        // 拖入视觉反馈：DragEnter 时高亮，DragLeave / Drop 时恢复
        DropZone.AddHandler(DragDrop.DragEnterEvent, (s, e) =>
        {
            DropZone.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4169E1"));
            DropZone.BorderThickness = new Avalonia.Thickness(2);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        
        DropZone.AddHandler(DragDrop.DragLeaveEvent, (s, e) =>
        {
            DropZone.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#cccccc"));
            DropZone.BorderThickness = new Avalonia.Thickness(1);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        
        DropZone.AddHandler(DragDrop.DropEvent, (s, e) =>
        {
            DropZone.BorderBrush = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#cccccc"));
            DropZone.BorderThickness = new Avalonia.Thickness(1);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
    }


    /// <summary>
    /// 拖入/粘贴：对存在的文件或文件夹取绝对路径；列表展示文件名与完整路径。详细日志写入底部日志区。
    /// </summary>
    public void ApplyDroppedPaths(IReadOnlyList<string> paths, PathImportKind kind = PathImportKind.Paste)
    {
        var tag = LogPrefix(kind);
        AppendLog($"{tag} 开始处理，共 {paths.Count} 条路径项。");

        var added = 0;
        var skippedDup = 0;
        var skippedInvalid = 0;

        foreach (var raw in paths)
        {
            AppendLog($"{tag} 原始项: {ClipForLog(raw)}");
            var resolved = DragDropPaths.TryResolveToExistingLocalPath(raw);
            if (string.IsNullOrEmpty(resolved))
            {
                skippedInvalid++;
                AppendLog($"{tag}   → 无法解析为本地已存在的文件或文件夹，已跳过。");
                continue;
            }

            var kindLabel = Directory.Exists(resolved) ? "文件夹" : (File.Exists(resolved) ? "文件" : "未知");
            AppendLog($"{tag}   → 解析绝对路径: {resolved}");
            AppendLog($"{tag}   → 类型: {kindLabel}");

            if (TryAddPathDistinct(resolved))
            {
                added++;
                try
                {
                    var name = Path.GetFileName(resolved.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    AppendLog($"{tag}   → 已加入列表（显示名: {name ?? resolved}）。");
                }
                catch
                {
                    AppendLog($"{tag}   → 已加入列表。");
                }
            }
            else
            {
                skippedDup++;
                AppendLog($"{tag}   → 与列表中已有项路径相同，跳过重复。");
            }
        }

        AppendLog($"{tag} 结束：新增 {added} 条，重复跳过 {skippedDup}，无效跳过 {skippedInvalid}。");
    }

    private static string LogPrefix(PathImportKind kind) => kind switch
    {
        PathImportKind.DragDrop => "[文件工作区·拖入]",
        PathImportKind.Paste => "[文件工作区·粘贴路径]",
        PathImportKind.RoutedFromMainWindow => "[文件工作区·拖入·主窗口转发]",
        _ => "[文件工作区]"
    };

    private static string ClipForLog(string? s, int maxLen = 500)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        var t = s.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= maxLen ? t : t.Substring(0, maxLen) + "…";
    }

    private void AppendLog(string line)
    {
        void Do()
        {
            if (_log == null) return;
            var prev = _log.Text ?? "";
            _log.Text = prev + $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
        }
        RunUi(Do);
    }

    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RunUi(() => DragDropCompat.EnableAllowDropRecursive(FilePathsGrid));
    }

    internal void RunUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }

    private void SetStatusSafe(string text) => RunUi(() => _setStatus?.Invoke(text));

    public void RefreshPwdCombo()
    {
        PasswordFileService.EnsurePwdDirectory();
        var sel = CbPwdFile.SelectedItem?.ToString();
        CbPwdFile.Items.Clear();
        CbPwdFile.Items.Add("(未选择)");
        foreach (var f in PasswordFileService.ListPwdFiles())
            CbPwdFile.Items.Add(Path.GetFileName(f));
        if (!string.IsNullOrEmpty(sel))
        {
            var i = CbPwdFile.Items.IndexOf(sel);
            if (i >= 0) CbPwdFile.SelectedIndex = i;
            else CbPwdFile.SelectedIndex = 0;
        }
        else
            CbPwdFile.SelectedIndex = 0;
    }

    private async Task AddFilesAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择文件",
            AllowMultiple = true
        });
        foreach (var f in files)
        {
            var p = DragDropPaths.TryGetPathFromStorageItem(f) ?? f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p))
                AddPathDistinct(p);
        }
    }

    private async Task AddFolderAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var dirs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择文件夹", AllowMultiple = false });
        if (dirs.Count == 0) return;
        var p = DragDropPaths.TryGetPathFromStorageItem(dirs[0]) ?? dirs[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(p))
            AddPathDistinct(p);
    }

    private async Task PastePathsFromClipboardAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard == null) return;
        string? text;
        try { text = await top.Clipboard.GetTextAsync(); }
        catch { return; }
        var paths = DragDropPaths.DistinctPaths(DragDropPaths.GetPathsFromClipboardText(text));
        if (paths.Count == 0) return;
        ApplyDroppedPaths(paths, PathImportKind.Paste);
    }

    private void ReplacePathInList(string oldPath, string newPath)
    {
        RunUi(() =>
        {
            var row = FindRow(oldPath);
            if (row == null) return;
            row.RefreshFromPath(newPath);
        });
    }

    private FileWorkspaceRow? FindRow(string path)
    {
        string norm;
        try { norm = Path.GetFullPath(path); }
        catch { return null; }
        foreach (var r in _rows)
        {
            try
            {
                if (string.Equals(Path.GetFullPath(r.FullPath), norm, StringComparison.OrdinalIgnoreCase))
                    return r;
            }
            catch { }
        }
        return null;
    }

    private void AddPathDistinct(string p) => TryAddPathDistinct(p);

    private bool TryAddPathDistinct(string p)
    {
        var norm = Path.GetFullPath(p);
        if (_rows.Any(x => string.Equals(Path.GetFullPath(x.FullPath), norm, StringComparison.OrdinalIgnoreCase)))
            return false;
        _rows.Add(FileWorkspaceRow.FromPath(p));
        return true;
    }

    private void UpdateRowProgress(string sourcePath, int percent, bool decryptMode)
    {
        var row = FindRow(sourcePath);
        if (row == null) return;
        row.IsDecryptMode = decryptMode;
        row.ProgressPercent = percent;
        row.ProgressLabel = percent + "%";
    }

    private sealed class RowProgress : IProgress<double>
    {
        private readonly FileWorkspaceView _owner;
        private readonly string _sourcePath;
        private readonly bool _decrypt;

        public RowProgress(FileWorkspaceView owner, string sourcePath, bool decrypt)
        {
            _owner = owner;
            _sourcePath = sourcePath;
            _decrypt = decrypt;
        }

        public void Report(double value)
        {
            var pct = Math.Max(0, Math.Min(100, (int)(value * 100)));
            _owner.RunUi(() => _owner.UpdateRowProgress(_sourcePath, pct, _decrypt));
        }
    }

    private static string? GetSuffixText(ComboBox cb) => cb.SelectedItem?.ToString();

    private CryptoAlgorithm MapAlgorithm()
    {
        var text = CbAlgo.SelectedItem?.ToString() ?? "";
        if (string.Equals(text, "AES-256-GCM", StringComparison.OrdinalIgnoreCase))
            return CryptoAlgorithm.AesGcm;
        return CryptoAlgorithm.AesCbc;
    }

    private string GetSelectedEncryptedExtension(CryptoAlgorithm alg)
    {
        var s = (GetSuffixText(CbSuffix) ?? "").Trim();
        if (string.IsNullOrEmpty(s))
            return alg == CryptoAlgorithm.AesGcm ? ".enc2" : ".enc1";
        if (!s.StartsWith(".", StringComparison.Ordinal))
            s = "." + s;
        return s.Length > 16 ? s.Substring(0, 16) : s;
    }

    private string? GetPassword()
    {
        if (CbPwdFile.SelectedIndex <= 0) return null;
        if (CbPwdFile.SelectedItem is not string name || !name.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = Path.Combine(PasswordFileService.GetPwdDirectory(), name);
        if (!File.Exists(path)) return null;
        try { return PasswordFileHelper.LoadPasswordFromFile(path); }
        catch { return null; }
    }

    private string? GetSelectedPwdFilePath()
    {
        if (CbPwdFile.SelectedIndex <= 0) return null;
        if (CbPwdFile.SelectedItem is not string name || !name.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
            return null;
        var path = Path.Combine(PasswordFileService.GetPwdDirectory(), name);
        return File.Exists(path) ? path : null;
    }

    private static byte[]? TryPwdHash(string? pwdPath)
    {
        try
        {
            if (string.IsNullOrEmpty(pwdPath) || !File.Exists(pwdPath)) return null;
            return System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(pwdPath));
        }
        catch { return null; }
    }

    private string GetPwdStem()
    {
        if (CbPwdFile.SelectedItem is string name && !string.IsNullOrWhiteSpace(name) && name != "(未选择)")
            return Path.GetFileNameWithoutExtension(name);
        return "pwd";
    }

    private async Task RunEncryptAsync()
    {
        CancelRun();
        _cts = new CancellationTokenSource();
        RunUi(() =>
        {
            BtnEncrypt.IsEnabled = BtnDecrypt.IsEnabled = false;
            BtnCancel.IsEnabled = true;
        });
        try
        {
            var paths = WorkspacePathHelpers.RemoveNestedPaths(_rows.Select(r => r.FullPath).ToList());
            paths = new List<string>(new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase));
            if (paths.Count == 0)
            {
                WriteLog("请先添加文件或文件夹。");
                return;
            }

            var password = GetPassword();
            if (string.IsNullOrWhiteSpace(password))
            {
                WriteLog("已取消：未选择有效密码文件或密码为空。");
                return;
            }

            var pwdPath = GetSelectedPwdFilePath();
            var pwdHash = TryPwdHash(pwdPath);
            bool inPlace = ChkOverwrite.IsChecked == true;
            var algorithm = MapAlgorithm();
            var encryptedExt = GetSelectedEncryptedExtension(algorithm);

            string commonOutputRoot = "";
            if (!inPlace)
            {
                string parent = WorkspacePathHelpers.GetCommonParentOnly(paths);
                if (string.IsNullOrEmpty(parent))
                    parent = Environment.CurrentDirectory;
                commonOutputRoot = Path.Combine(parent, Guid.NewGuid().ToString("N") + "_" + GetPwdStem() + "_output");
                Directory.CreateDirectory(commonOutputRoot);
                _lastNonInPlaceOutputRoot = commonOutputRoot;
                WriteLog($"输出目录: {commonOutputRoot}");
            }

            SetStatusSafe("执行加密中…");
            foreach (var source in paths)
            {
                _cts.Token.ThrowIfCancellationRequested();
                if (!File.Exists(source) && !Directory.Exists(source)) continue;
                bool isDir = Directory.Exists(source);
                string outDir = inPlace
                    ? (isDir ? source : Path.GetDirectoryName(source)!)
                    : commonOutputRoot;

                var options = new FileEncryptorOptions
                {
                    SourcePath = source,
                    OutputRoot = outDir,
                    InPlace = inPlace,
                    Recursive = isDir && ChkRecursive.IsChecked == true,
                    RandomizeFileName = ChkRandomName.IsChecked == true,
                    RandomFileNameLength = 36,
                    RandomFileNameFormat = "hex",
                    Algorithm = algorithm,
                    Password = password,
                    Iterations = 200_000,
                    AesKeySizeBits = 256,
                    Log = WriteLog,
                    EncryptedExtension = encryptedExt,
                    PasswordFileHash = pwdHash
                };
                var enc = new FileEncryptor(options);
                RunUi(() => UpdateRowProgress(source, 0, decryptMode: false));
                var rowProgress = new RowProgress(this, source, decrypt: false);
                await enc.EncryptAsync(rowProgress, _cts.Token);
                RunUi(() => UpdateRowProgress(source, 100, decryptMode: false));
                if (!isDir)
                {
                    var outPath = enc.GetExpectedOutputPath(source, encrypt: true);
                    if (File.Exists(outPath))
                        ReplacePathInList(source, outPath);
                }
            }
            WriteLog("加密完成。");
            SetStatusSafe("加密完成。");
        }
        catch (OperationCanceledException)
        {
            WriteLog("已取消。");
        }
        catch (Exception ex)
        {
            WriteLog("加密失败: " + ex.Message);
            SetStatusSafe("加密失败。");
        }
        finally
        {
            RunUi(() =>
            {
                BtnEncrypt.IsEnabled = BtnDecrypt.IsEnabled = true;
                BtnCancel.IsEnabled = false;
            });
        }
    }

    private async Task RunDecryptAsync()
    {
        CancelRun();
        _cts = new CancellationTokenSource();
        RunUi(() =>
        {
            BtnEncrypt.IsEnabled = BtnDecrypt.IsEnabled = false;
            BtnCancel.IsEnabled = true;
        });
        try
        {
            bool inPlace = ChkOverwrite.IsChecked == true;
            var password = GetPassword();
            if (string.IsNullOrWhiteSpace(password))
            {
                WriteLog("已取消：未选择有效密码文件或密码为空。");
                return;
            }

            var pwdPath = GetSelectedPwdFilePath();
            var pwdHash = TryPwdHash(pwdPath);
            var algorithm = MapAlgorithm();
            var encryptedExt = GetSelectedEncryptedExtension(algorithm);

            SetStatusSafe("执行解密中…");

            if (inPlace)
            {
                var paths = WorkspacePathHelpers.RemoveNestedPaths(_rows.Select(r => r.FullPath).ToList());
                paths = new List<string>(new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase));
                if (paths.Count == 0)
                {
                    WriteLog("请先添加要解密的文件或文件夹。");
                    return;
                }
                foreach (var source in paths)
                {
                    _cts.Token.ThrowIfCancellationRequested();
                    if (!File.Exists(source) && !Directory.Exists(source)) continue;
                    bool isDir = Directory.Exists(source);
                    string outDir = isDir ? source : Path.GetDirectoryName(source)!;
                    var options = new FileEncryptorOptions
                    {
                        SourcePath = source,
                        OutputRoot = outDir,
                        InPlace = true,
                        Recursive = isDir && ChkRecursive.IsChecked == true,
                        RandomizeFileName = false,
                        Algorithm = algorithm,
                        Password = password,
                        Iterations = 200_000,
                        AesKeySizeBits = 256,
                        Log = WriteLog,
                        EncryptedExtension = encryptedExt,
                        PasswordFileHash = pwdHash
                    };
                    var dec = new FileEncryptor(options);
                    RunUi(() => UpdateRowProgress(source, 0, decryptMode: true));
                    var rowProgress = new RowProgress(this, source, decrypt: true);
                    await dec.DecryptAsync(rowProgress, _cts.Token);
                    RunUi(() => UpdateRowProgress(source, 100, decryptMode: true));
                    if (!isDir)
                    {
                        var decPath = dec.GetExpectedOutputPath(source, encrypt: false);
                        if (File.Exists(decPath))
                            ReplacePathInList(source, decPath);
                    }
                }
            }
            else
            {
                if (string.IsNullOrEmpty(_lastNonInPlaceOutputRoot) || !Directory.Exists(_lastNonInPlaceOutputRoot))
                {
                    WriteLog("非覆盖解密：请先在本工作区执行一次加密（生成 *_output），或将输出目录手动加入列表后使用覆盖模式解密。");
                    return;
                }
                var source = _lastNonInPlaceOutputRoot;
                var options = new FileEncryptorOptions
                {
                    SourcePath = source,
                    OutputRoot = source,
                    InPlace = false,
                    Recursive = true,
                    RandomizeFileName = false,
                    Algorithm = algorithm,
                    Password = password,
                    Iterations = 200_000,
                    AesKeySizeBits = 256,
                    Log = WriteLog,
                    EncryptedExtension = encryptedExt,
                    PasswordFileHash = pwdHash
                };
                var row = FindRow(source);
                if (row != null)
                    RunUi(() => UpdateRowProgress(source, 0, decryptMode: true));
                IProgress<double> bulkProgress = row != null
                    ? new RowProgress(this, source, decrypt: true)
                    : new Progress<double>(_ => { });
                await new FileEncryptor(options).DecryptAsync(bulkProgress, _cts.Token);
                if (row != null)
                    RunUi(() => UpdateRowProgress(source, 100, decryptMode: true));
            }

            WriteLog("解密完成。");
            SetStatusSafe("解密完成。");
        }
        catch (OperationCanceledException)
        {
            WriteLog("已取消。");
        }
        catch (Exception ex)
        {
            WriteLog("解密失败: " + ex.Message);
            SetStatusSafe("解密失败。");
        }
        finally
        {
            RunUi(() =>
            {
                BtnEncrypt.IsEnabled = BtnDecrypt.IsEnabled = true;
                BtnCancel.IsEnabled = false;
            });
        }
    }

    private void CancelRun()
    {
        try { _cts?.Cancel(); } catch { }
        try { _cts?.Dispose(); } catch { }
        _cts = null;
    }

    private void WriteLog(string m)
    {
        if (string.IsNullOrEmpty(m)) return;
        var line = m.IndexOf(']') > 0 ? m : $"[{DateTime.Now:HH:mm:ss}] {m}";
        void Do()
        {
            if (_log == null) return;
            _log.Text = (_log.Text ?? "") + line + Environment.NewLine;
        }
        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }
}
