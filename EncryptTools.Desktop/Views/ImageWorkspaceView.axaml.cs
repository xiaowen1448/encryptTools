using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using EncryptTools;
using EncryptTools.Desktop.ImageWork;
using EncryptTools.Desktop.Imaging;
using EncryptTools.Desktop.Input;
using EncryptTools.Desktop.Workspace;
using EncryptTools.PasswordFile;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using AvaloniaImage = Avalonia.Controls.Image;
using AvaloniaColor = Avalonia.Media.Color;
using AvaloniaSize = Avalonia.Size;
using SixLaborsImage = SixLabors.ImageSharp.Image;

namespace EncryptTools.Desktop.Views;

public partial class ImageWorkspaceView : UserControl
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".jfif", ".jpe", ".bmp", ".gif", ".ico" };

    private readonly TextBox _log;
    private readonly List<string> _imagePaths = new();
    private bool _lastActionWasDecrypt;

    private sealed class PreviewZoomState
    {
        public AvaloniaSize BaseSize { get; set; }
        public AvaloniaSize InitialDisplaySize { get; set; }
        public float Zoom { get; set; } = 1f;
    }

    private sealed class TabImageHost
    {
        public required string FilePath { get; init; }
        public required AvaloniaImage LeftImage { get; init; }
        public required AvaloniaImage RightImage { get; init; }
        public required ScrollViewer LeftScroll { get; init; }
        public required ScrollViewer RightScroll { get; init; }
        public PreviewZoomState LeftZoom { get; } = new();
        public PreviewZoomState RightZoom { get; } = new();
        public ImageEffectOptions? CryptoOptions { get; set; }
        public Image<Rgba32>? EncryptedImageFull { get; set; }
        public Image<Rgba32>? DecryptedImageFull { get; set; }
    }

    public ImageWorkspaceView(TextBox logBox)
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DragDropCompat.EnableAllowDropRecursive(this);
            // XAML 填充早期 SheetTabs 会触发 SelectionChanged，当时 LblImageStatus 可能尚未赋值；Loaded 后再同步状态栏
            UpdateStatus();
        };
        _log = logBox;

        CbMode.Items.Add("不可逆马赛克(仅效果)");
        CbMode.Items.Add("密钥置乱(可逆)");
        CbMode.Items.Add("像素XOR(可逆)");
        CbMode.Items.Add("分块置乱(可逆)");
        CbMode.Items.Add("Arnold猫变换(可逆)");
        CbMode.SelectedIndex = 1;

        foreach (var s in new[] { "4×4", "8×8", "16×16", "24×24", "32×32", "48×48", "64×64" })
            CbBlock.Items.Add(s);
        CbBlock.SelectedIndex = 2;

        foreach (var s in new[] { "8×8", "16×16", "24×24", "32×32", "48×48", "64×64", "96×96", "128×128" })
            CbIconBlock.Items.Add(s);
        CbIconBlock.SelectedIndex = 3;

        RefreshPwdCombo();
        RefreshIconsCombo();
        UpdateToolbarIconPreview();

        ChkPixelation.IsCheckedChanged += (_, _) => UpdateModeEnabled();
        UpdateModeEnabled();

        BtnSelect.Click += async (_, _) => await SelectImagesAsync();
        BtnPastePaths.Click += async (_, _) => await PastePathsFromClipboardAsync();
        BtnImportIcons.Click += async (_, _) => await ImportIconsAsync();
        BtnClearImages.Click += (_, _) => ClearImages();
        BtnEncrypt.Click += async (_, _) => await RunEncryptAsync();
        BtnDecrypt.Click += async (_, _) => await RunDecryptAsync();
        BtnSave.Click += async (_, _) => await SaveBatchAsync();

        CbIcons.SelectionChanged += (_, _) => UpdateToolbarIconPreview();

        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(DropHost, true);
        DragDrop.SetAllowDrop(SheetTabs, true);
        DragDrop.SetAllowDrop(Placeholder, true);
        DragDropCompat.AttachStandardFileDrop(this, p => ApplyDroppedPaths(p, PathImportKind.DragDrop));
        DragDropCompat.AttachTargetDragOverCopy(DropHost);
        DragDropCompat.AttachTargetDragOverCopy(Placeholder);
        DragDropCompat.AttachTargetDragOverCopy(SheetTabs);
    }

    /// <summary>
    /// 拖入/粘贴：文件仅支持图片扩展名，导入并打开预览；文件夹则扫描顶层文件，仅加入图片。详细日志写入底部日志区。
    /// </summary>
    public void ApplyDroppedPaths(IReadOnlyList<string> paths, PathImportKind kind = PathImportKind.Paste)
    {
        var tag = LogPrefix(kind);
        AppendLog($"{tag} 开始处理，共 {paths.Count} 条路径项。");

        var skippedNonImage = 0;
        var skippedInvalid = 0;

        foreach (var raw in paths)
        {
            AppendLog($"{tag} 原始项: {ClipForLog(raw)}");
            var p = DragDropPaths.TryResolveToExistingLocalPath(raw);
            if (string.IsNullOrEmpty(p))
            {
                skippedInvalid++;
                AppendLog($"{tag}   → 无法解析为本地已存在的文件或文件夹，已跳过。");
                continue;
            }

            AppendLog($"{tag}   → 解析绝对路径: {p}");

            if (Directory.Exists(p))
            {
                AppendLog($"{tag}   → 类型: 文件夹（仅扫描顶层文件中的图片）。");
                AddImagesFromFolder(p, tag);
            }
            else if (File.Exists(p))
            {
                AppendLog($"{tag}   → 类型: 文件");
                if (!IsImageFile(p))
                {
                    skippedNonImage++;
                    AppendLog($"{tag}   → 扩展名非图片，已跳过（支持: {string.Join(", ", ImageExtensions)}）。");
                    continue;
                }

                if (TryAddImagePath(p, logTag: tag))
                    AppendLog($"{tag}   → 已导入预览并打开标签页。");
                else
                    AppendLog($"{tag}   → 未新增（可能已在列表中或加载失败，见上文）。");
            }
            else
            {
                skippedInvalid++;
                AppendLog($"{tag}   → 路径存在性异常，已跳过。");
            }
        }

        if (skippedNonImage > 0)
            AppendLog($"{tag} 汇总：共跳过 {skippedNonImage} 个非图片文件。");
        if (skippedInvalid > 0)
            AppendLog($"{tag} 汇总：共 {skippedInvalid} 条无法解析。");
        AppendLog($"{tag} 处理结束。");
    }

    private static string LogPrefix(PathImportKind kind) => kind switch
    {
        PathImportKind.DragDrop => "[图片工作区·拖入]",
        PathImportKind.Paste => "[图片工作区·粘贴路径]",
        PathImportKind.RoutedFromMainWindow => "[图片工作区·拖入·主窗口转发]",
        _ => "[图片工作区]"
    };

    private static string ClipForLog(string? s, int maxLen = 500)
    {
        if (string.IsNullOrEmpty(s)) return "(空)";
        var t = s.Replace('\r', ' ').Replace('\n', ' ');
        return t.Length <= maxLen ? t : t.Substring(0, maxLen) + "…";
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

    private void SheetTabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // TabControl.EndInit 时会触发 SelectionChanged，此时同页中更靠后的 LblImageStatus 等字段可能尚未赋值
        if (LblImageStatus == null || SheetTabs == null) return;
        UpdateStatus();
    }

    private static string GetIcoDirectory()
    {
        try
        {
            return Path.Combine(AppContext.BaseDirectory, "ico");
        }
        catch
        {
            return Path.Combine(Environment.CurrentDirectory, "ico");
        }
    }

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
            CbPwdFile.SelectedIndex = i >= 0 ? i : 0;
        }
        else
            CbPwdFile.SelectedIndex = 0;
    }

    private void RefreshIconsCombo(string? selectFileName = null)
    {
        try
        {
            var dir = GetIcoDirectory();
            Directory.CreateDirectory(dir);
            var names = Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            CbIcons.Items.Clear();
            CbIcons.Items.Add("(未选择)");
            foreach (var n in names)
                CbIcons.Items.Add(n!);

            if (!string.IsNullOrWhiteSpace(selectFileName))
            {
                for (var i = 0; i < CbIcons.Items.Count; i++)
                {
                    if (string.Equals(CbIcons.Items[i]?.ToString(), selectFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        CbIcons.SelectedIndex = i;
                        return;
                    }
                }
            }
            CbIcons.SelectedIndex = 0;
            UpdateToolbarIconPreview();
        }
        catch
        {
            CbIcons.Items.Clear();
            CbIcons.Items.Add("(未选择)");
            CbIcons.SelectedIndex = 0;
            IconPreview.Source = null;
        }
    }

    private void UpdateModeEnabled()
    {
        CbMode.IsEnabled = ChkPixelation.IsChecked == true;
    }

    private void UpdateStatus()
    {
        if (LblImageStatus == null || SheetTabs == null) return;
        if (_imagePaths.Count == 0)
        {
            LblImageStatus.Text = "就绪";
            return;
        }
        var idx = SheetTabs.SelectedIndex;
        if (idx >= 0 && idx < _imagePaths.Count)
            LblImageStatus.Text = _imagePaths[idx];
        else
            LblImageStatus.Text = $"已加载 {_imagePaths.Count} 张图片";
    }

    private static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private string? GetSelectedIcoFullPath()
    {
        if (CbIcons.SelectedItem is not string name || string.IsNullOrWhiteSpace(name) || name == "(未选择)")
            return null;
        var full = Path.Combine(GetIcoDirectory(), name);
        return File.Exists(full) ? full : null;
    }

    private int ParseIconBlockPx()
    {
        var t = CbIconBlock.SelectedItem?.ToString() ?? "32×32";
        var sep = t.Contains('×', StringComparison.Ordinal) ? '×' : 'x';
        var parts = t.Split(sep);
        if (parts.Length > 0 && int.TryParse(parts[0].Trim(), out var n))
            return Math.Clamp(n, 8, 512);
        return 32;
    }

    private void UpdateToolbarIconPreview()
    {
        var ico = GetSelectedIcoFullPath();
        IconPreview.Source = string.IsNullOrEmpty(ico) ? null : ImageBitmapLoader.LoadAvaloniaBitmap(ico, 32);
    }

    private ImageEffectOptions BuildOptionsFromUi()
    {
        int block = CbBlock.SelectedIndex switch
        {
            0 => 4,
            1 => 8,
            2 => 16,
            3 => 24,
            4 => 32,
            5 => 48,
            _ => 64
        };
        var salt = new byte[16];
        Compat.RngFill(salt);
        var opt = new ImageEffectOptions
        {
            Mode = (ImageMode)Math.Max(0, Math.Min(4, CbMode.SelectedIndex)),
            BlockSize = block,
            Iterations = 200_000,
            SaltBase64 = Convert.ToBase64String(salt),
            PixelationEnabled = ChkPixelation.IsChecked == true,
            IconOverlayEnabled = ChkIconOverlay.IsChecked == true,
            OverlayOpacityPercent = (int)SliderOpacity.Value,
            IconOverlayBlockSizeHint = ParseIconBlockPx()
        };
        try
        {
            if (CbPwdFile.SelectedItem is string name && !string.IsNullOrWhiteSpace(name) && name != "(未选择)" &&
                name.EndsWith(".pwd", StringComparison.OrdinalIgnoreCase))
                opt.PasswordFileName = name;
        }
        catch { }
        return opt;
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

    /// <summary>加密时仅使用下拉框选中的图标（与 Windows 单选预期一致）。</summary>
    private List<string> GetSelectedIconPathsForEncrypt()
    {
        var p = GetSelectedIcoFullPath();
        return string.IsNullOrEmpty(p) ? new List<string>() : new List<string> { p };
    }

    private async Task RunEncryptAsync()
    {
        if (SheetTabs.Items.Count == 0)
        {
            AppendLog("请先添加图片。");
            return;
        }
        var pwd = GetPassword();
        if (string.IsNullOrWhiteSpace(pwd))
        {
            AppendLog("请先选择密码文件。");
            return;
        }
        var pwdPath = GetSelectedPwdFilePath();
        var pwdFileName = pwdPath != null ? Path.GetFileName(pwdPath) : null;
        _lastActionWasDecrypt = false;
        var iconPaths = GetSelectedIconPathsForEncrypt();

        foreach (var item in SheetTabs.Items)
        {
            if (item is not TabItem tab || tab.Tag is not TabImageHost host) continue;
            var path = host.FilePath;
            try
            {
                var options = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var o = BuildOptionsFromUi();
                    if (!string.IsNullOrEmpty(pwdFileName))
                        o.PasswordFileName = pwdFileName;
                    return o;
                });
                var applyIconOverlay = await Dispatcher.UIThread.InvokeAsync(() =>
                    ChkIconOverlay.IsChecked == true && options.IconOverlayEnabled && iconPaths.Count > 0);

                await Task.Run(() =>
                {
                    using var img = SixLaborsImage.Load<Rgba32>(path);
                    var processed = ImageSharpPixelEffects.ApplyPixelEffect(img.Clone(), options, pwd, encrypt: true);
                    if (applyIconOverlay)
                        ImageSharpPixelEffects.ApplyIconOverlay(processed, options, iconPaths, pwd);
                    else
                    {
                        options.IconOverlayBlocksEncryptedBase64 = null;
                        options.IconOverlayBlockSize = 0;
                    }

                    host.EncryptedImageFull?.Dispose();
                    host.DecryptedImageFull?.Dispose();
                    host.DecryptedImageFull = null;
                    host.EncryptedImageFull = processed;
                    host.CryptoOptions = options;
                    var bmp = ImageBitmapLoader.LoadAvaloniaBitmapFromImage(processed, 960);
                    Dispatcher.UIThread.Post(() => SetRightPreviewBitmap(host, bmp, preserveZoom: true));
                }).ConfigureAwait(true);
                AppendLog($"加密完成: {Path.GetFileName(path)}");
            }
            catch (Exception ex)
            {
                AppendLog($"加密失败 {Path.GetFileName(path)}: {ex.Message}");
            }
        }
        AppendLog("批量加密完成。");
    }

    private async Task RunDecryptAsync()
    {
        if (SheetTabs.Items.Count == 0)
        {
            AppendLog("请先添加图片。");
            return;
        }
        var pwd = GetPassword();
        if (string.IsNullOrWhiteSpace(pwd))
        {
            AppendLog("请先选择密码文件。");
            return;
        }
        var pwdPath = GetSelectedPwdFilePath();
        var currentPwdFileName = pwdPath != null ? Path.GetFileName(pwdPath) : null;
        _lastActionWasDecrypt = true;

        foreach (var item in SheetTabs.Items)
        {
            if (item is not TabItem tab || tab.Tag is not TabImageHost host) continue;
            if (host.EncryptedImageFull == null || host.CryptoOptions == null)
            {
                AppendLog($"跳过(无加密状态): {Path.GetFileName(host.FilePath)}");
                continue;
            }
            var opt = host.CryptoOptions;
            if (!string.IsNullOrWhiteSpace(opt.PasswordFileName) &&
                !string.IsNullOrWhiteSpace(currentPwdFileName) &&
                !string.Equals(opt.PasswordFileName, currentPwdFileName, StringComparison.OrdinalIgnoreCase))
            {
                AppendLog($"密码文件错误: {Path.GetFileName(host.FilePath)}，请选用加密时使用的密码文件。");
                continue;
            }
            if (opt.Mode == ImageMode.Mosaic)
            {
                AppendLog($"跳过(不可逆模式): {Path.GetFileName(host.FilePath)}");
                continue;
            }
            try
            {
                var encImg = host.EncryptedImageFull;
                await Task.Run(() =>
                {
                    using var src = encImg.Clone();
                    var dec = ImageSharpPixelEffects.DecryptPipeline(src, opt, pwd);
                    host.DecryptedImageFull?.Dispose();
                    host.DecryptedImageFull = dec;
                    var bmp = ImageBitmapLoader.LoadAvaloniaBitmapFromImage(dec, 960);
                    Dispatcher.UIThread.Post(() => SetRightPreviewBitmap(host, bmp, preserveZoom: true));
                }).ConfigureAwait(true);
                AppendLog($"解密完成: {Path.GetFileName(host.FilePath)}");
            }
            catch (Exception ex)
            {
                AppendLog($"解密失败 {Path.GetFileName(host.FilePath)}: {ex.Message}");
            }
        }
        AppendLog("批量解密完成。");
    }

    private async Task SaveBatchAsync()
    {
        if (SheetTabs.Items.Count == 0)
        {
            AppendLog("没有可保存的页。");
            return;
        }
        var pwdPath = GetSelectedPwdFilePath();
        if (string.IsNullOrEmpty(pwdPath) || !File.Exists(pwdPath))
        {
            AppendLog("请先选择密码文件(.pwd)后再保存。");
            return;
        }
        bool saveDecrypted = _lastActionWasDecrypt;
        foreach (var item in SheetTabs.Items)
        {
            if (item is not TabItem tab || tab.Tag is not TabImageHost host) continue;
            var srcPath = host.FilePath;
            if (string.IsNullOrWhiteSpace(srcPath) || !File.Exists(srcPath)) continue;
            var outDir = Path.Combine(Path.GetDirectoryName(srcPath) ?? srcPath, "output");
            Directory.CreateDirectory(outDir);
            var baseName = Path.GetFileNameWithoutExtension(srcPath);
            var outPath = Path.Combine(outDir, baseName + (saveDecrypted ? "_decrypted.png" : "_encrypted.png"));
            try
            {
                if (saveDecrypted)
                {
                    if (host.DecryptedImageFull == null)
                    {
                        AppendLog($"跳过保存(无解密结果): {Path.GetFileName(srcPath)}");
                        continue;
                    }
                    await Task.Run(() => host.DecryptedImageFull!.SaveAsPng(outPath)).ConfigureAwait(true);
                    AppendLog($"已保存: {outPath}");
                }
                else
                {
                    if (host.EncryptedImageFull == null || host.CryptoOptions == null)
                    {
                        AppendLog($"跳过保存(未加密或缺参数): {Path.GetFileName(srcPath)}");
                        continue;
                    }
                    await Task.Run(() => host.EncryptedImageFull!.SaveAsPng(outPath)).ConfigureAwait(true);
                    var metaPath = outPath + ".encmeta.json";
                    var json = JsonSerializer.Serialize(host.CryptoOptions, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(metaPath, json, Encoding.UTF8).ConfigureAwait(true);
                    AppendLog($"已保存: {outPath}");
                    AppendLog($"已写入元数据: {metaPath}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"保存失败: {Path.GetFileName(srcPath)} - {ex.Message}");
            }
        }
        AppendLog("批量保存完成。");
    }

    private async Task SelectImagesAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图片",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("图片")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.jfif", "*.jpe", "*.bmp", "*.gif", "*.ico" }
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        });
        foreach (var f in files)
        {
            var p = DragDropPaths.TryGetPathFromStorageItem(f) ?? f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p))
                TryAddImagePath(p);
        }
    }

    private async Task ImportIconsAsync()
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入图标",
            AllowMultiple = true
        });
        if (files.Count == 0) return;
        var dir = GetIcoDirectory();
        Directory.CreateDirectory(dir);
        string? first = null;
        var n = 0;
        foreach (var f in files)
        {
            var src = f.TryGetLocalPath();
            if (string.IsNullOrEmpty(src) || !File.Exists(src)) continue;
            try
            {
                var name = Path.GetFileName(src);
                var dst = Path.Combine(dir, name);
                File.Copy(src, dst, overwrite: true);
                if (first == null) first = name;
                n++;
            }
            catch { }
        }
        RefreshIconsCombo(first);
        AppendLog($"已导入 {n} 个图标到 ico 目录。");
    }

    private void AddImagesFromFolder(string folder, string logTag)
    {
        string dir;
        try { dir = Path.GetFullPath(folder); }
        catch { return; }
        if (!Directory.Exists(dir)) return;
        try
        {
            var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly).ToList();
            AppendLog($"{logTag}   文件夹绝对路径: {dir}");
            AppendLog($"{logTag}   顶层文件数: {files.Count}");

            var added = 0;
            var skippedNon = 0;
            var skippedDup = 0;
            var failed = 0;

            foreach (var f in files)
            {
                if (!IsImageFile(f))
                {
                    skippedNon++;
                    continue;
                }

                string full;
                try { full = Path.GetFullPath(f); }
                catch
                {
                    failed++;
                    continue;
                }

                if (_imagePaths.Any(p => string.Equals(Path.GetFullPath(p), full, StringComparison.OrdinalIgnoreCase)))
                {
                    skippedDup++;
                    continue;
                }

                if (TryAddImagePath(f, logTag: null))
                    added++;
                else
                    failed++;
            }

            AppendLog($"{logTag}   文件夹扫描结果: 新导入 {added}，非图片跳过 {skippedNon}，已在列表中跳过 {skippedDup}，加载失败 {failed}。");
        }
        catch (Exception ex)
        {
            AppendLog($"{logTag}   读取文件夹失败: {ex.Message}");
        }
    }

    /// <returns>是否新加入了图片并打开预览</returns>
    private bool TryAddImagePath(string path, string? logTag = null)
    {
        string full;
        try { full = Path.GetFullPath(path); }
        catch { return false; }
        if (!IsImageFile(full) || !File.Exists(full)) return false;
        if (_imagePaths.Any(p => string.Equals(Path.GetFullPath(p), full, StringComparison.OrdinalIgnoreCase)))
            return false;
        try
        {
            _imagePaths.Add(full);
            AddTab(full);
            Placeholder.IsVisible = false;
            SheetTabs.IsVisible = true;
            UpdateStatus();
            return true;
        }
        catch (Exception ex)
        {
            _imagePaths.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            var prefix = string.IsNullOrEmpty(logTag) ? "[图片工作区]" : logTag;
            AppendLog($"{prefix} 加载图片失败: {Path.GetFileName(path)} — {ex.Message}");
            return false;
        }
    }

    private void TryLoadMetaSidecar(TabImageHost host)
    {
        var metaPath = host.FilePath + ".encmeta.json";
        if (!File.Exists(metaPath)) return;
        try
        {
            var json = File.ReadAllText(metaPath, Encoding.UTF8);
            var meta = JsonSerializer.Deserialize<ImageEffectOptions>(json);
            if (meta == null) return;
            host.CryptoOptions = meta;
            host.EncryptedImageFull?.Dispose();
            host.EncryptedImageFull = SixLaborsImage.Load<Rgba32>(host.FilePath);
            AppendLog($"检测到元数据: {Path.GetFileName(metaPath)}。请选择密码后点击「解密」。");
        }
        catch { }
    }

    private const int PreviewMaxW = 480;
    private const int PreviewMaxH = 360;

    private static AvaloniaSize FitThumbnailSize(AvaloniaSize imageSize, int maxW, int maxH)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0) return new AvaloniaSize(maxW, maxH);
        var r = Math.Min((double)maxW / imageSize.Width, (double)maxH / imageSize.Height);
        if (r >= 1) return imageSize;
        return new AvaloniaSize(imageSize.Width * r, imageSize.Height * r);
    }

    private static void ApplyZoomToImage(AvaloniaImage img, PreviewZoomState st)
    {
        if (img.Source is not Bitmap bmp) return;
        if (st.BaseSize.Width <= 0 || st.BaseSize.Height <= 0)
        {
            var p = bmp.PixelSize;
            st.BaseSize = new AvaloniaSize(p.Width, p.Height);
        }
        if (st.InitialDisplaySize.Width <= 0 || st.InitialDisplaySize.Height <= 0)
            st.InitialDisplaySize = FitThumbnailSize(st.BaseSize, PreviewMaxW, PreviewMaxH);
        img.Width = Math.Max(1, st.InitialDisplaySize.Width * st.Zoom);
        img.Height = Math.Max(1, st.InitialDisplaySize.Height * st.Zoom);
    }

    private void SetLeftPreviewFromPath(TabImageHost host, string filePath)
    {
        var bmp = ImageBitmapLoader.LoadAvaloniaBitmap(filePath, null);
        if (bmp == null) return;
        host.LeftImage.Source = bmp;
        var ps = bmp.PixelSize;
        host.LeftZoom.BaseSize = new AvaloniaSize(ps.Width, ps.Height);
        host.LeftZoom.InitialDisplaySize = FitThumbnailSize(host.LeftZoom.BaseSize, PreviewMaxW, PreviewMaxH);
        host.LeftZoom.Zoom = 1f;
        ApplyZoomToImage(host.LeftImage, host.LeftZoom);
    }

    private void SetRightPreviewBitmap(TabImageHost host, Bitmap? bmp, bool preserveZoom)
    {
        host.RightImage.Source = bmp;
        if (bmp == null)
        {
            host.RightImage.Width = double.NaN;
            host.RightImage.Height = double.NaN;
            return;
        }
        var ps = bmp.PixelSize;
        host.RightZoom.BaseSize = new AvaloniaSize(ps.Width, ps.Height);
        if (!preserveZoom || host.RightZoom.InitialDisplaySize.Width <= 0)
        {
            host.RightZoom.InitialDisplaySize = FitThumbnailSize(host.RightZoom.BaseSize, PreviewMaxW, PreviewMaxH);
            host.RightZoom.Zoom = 1f;
        }
        else
        {
            // 加密/解密后图像内容变化：按新图重新适配预览尺寸，但保留用户设置的 Zoom 倍率（+/- 与滚轮）
            host.RightZoom.InitialDisplaySize = FitThumbnailSize(host.RightZoom.BaseSize, PreviewMaxW, PreviewMaxH);
        }
        ApplyZoomToImage(host.RightImage, host.RightZoom);
    }

    private static void ResetZoom100(AvaloniaImage img, PreviewZoomState st)
    {
        if (img.Source is not Bitmap bmp) return;
        var p = bmp.PixelSize;
        st.BaseSize = new AvaloniaSize(p.Width, p.Height);
        st.InitialDisplaySize = FitThumbnailSize(st.BaseSize, PreviewMaxW, PreviewMaxH);
        st.Zoom = 1f;
        ApplyZoomToImage(img, st);
    }

    private static void ZoomIn(AvaloniaImage img, PreviewZoomState st)
    {
        if (img.Source is not Bitmap) return;
        if (st.InitialDisplaySize.Width <= 0)
            st.InitialDisplaySize = FitThumbnailSize(st.BaseSize, PreviewMaxW, PreviewMaxH);
        st.Zoom = Math.Min(20f, st.Zoom * 1.1f);
        ApplyZoomToImage(img, st);
    }

    private static void ZoomOut(AvaloniaImage img, PreviewZoomState st)
    {
        if (img.Source is not Bitmap) return;
        if (st.InitialDisplaySize.Width <= 0)
            st.InitialDisplaySize = FitThumbnailSize(st.BaseSize, PreviewMaxW, PreviewMaxH);
        st.Zoom = Math.Max(0.05f, st.Zoom / 1.1f);
        ApplyZoomToImage(img, st);
    }

    private void OnPreviewWheel(PointerWheelEventArgs e, AvaloniaImage img, PreviewZoomState st)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        if (img.Source is not Bitmap) return;
        if (st.InitialDisplaySize.Width <= 0)
            st.InitialDisplaySize = FitThumbnailSize(st.BaseSize, PreviewMaxW, PreviewMaxH);
        st.Zoom = e.Delta.Y > 0 ? Math.Min(20f, st.Zoom * 1.1f) : Math.Max(0.05f, st.Zoom / 1.1f);
        ApplyZoomToImage(img, st);
    }

    private void AddTab(string path)
    {
        var leftImg = new AvaloniaImage
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var rightImg = new AvaloniaImage
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var leftScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = leftImg
        };
        var rightScroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = rightImg
        };

        var leftBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(AvaloniaColor.FromRgb(240, 240, 240))
        };
        leftBar.Children.Add(new TextBlock { Text = "原图", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 4, 8, 4) });
        var btnLl = new Button { Content = "1:1", MinWidth = 40, Padding = new Thickness(6, 2) };
        var btnLp = new Button { Content = "+", MinWidth = 28, Padding = new Thickness(6, 2) };
        var btnLm = new Button { Content = "-", MinWidth = 28, Padding = new Thickness(6, 2) };
        leftBar.Children.Add(btnLl);
        leftBar.Children.Add(btnLp);
        leftBar.Children.Add(btnLm);

        var rightBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Margin = new Thickness(0, 0, 0, 4),
            Background = new SolidColorBrush(AvaloniaColor.FromRgb(240, 240, 240))
        };
        rightBar.Children.Add(new TextBlock { Text = "加密/解密预览", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 4, 8, 4) });
        var btnRl = new Button { Content = "1:1", MinWidth = 40, Padding = new Thickness(6, 2) };
        var btnRp = new Button { Content = "+", MinWidth = 28, Padding = new Thickness(6, 2) };
        var btnRm = new Button { Content = "-", MinWidth = 28, Padding = new Thickness(6, 2) };
        rightBar.Children.Add(btnRl);
        rightBar.Children.Add(btnRp);
        rightBar.Children.Add(btnRm);

        var leftCol = new Grid();
        leftCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        leftCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(leftBar, 0);
        Grid.SetRow(leftScroll, 1);
        leftCol.Children.Add(leftBar);
        leftCol.Children.Add(leftScroll);

        var rightCol = new Grid();
        rightCol.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        rightCol.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(rightBar, 0);
        Grid.SetRow(rightScroll, 1);
        rightCol.Children.Add(rightBar);
        rightCol.Children.Add(rightScroll);

        var splitter = new GridSplitter
        {
            Width = 8,
            ResizeDirection = GridResizeDirection.Columns,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var splitRoot = new Grid { MinHeight = 240 };
        splitRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        splitRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        splitRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(leftCol, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(rightCol, 2);
        splitRoot.Children.Add(leftCol);
        splitRoot.Children.Add(splitter);
        splitRoot.Children.Add(rightCol);

        var host = new TabImageHost
        {
            FilePath = path,
            LeftImage = leftImg,
            RightImage = rightImg,
            LeftScroll = leftScroll,
            RightScroll = rightScroll
        };

        btnLl.Click += (_, _) => ResetZoom100(host.LeftImage, host.LeftZoom);
        btnLp.Click += (_, _) => ZoomIn(host.LeftImage, host.LeftZoom);
        btnLm.Click += (_, _) => ZoomOut(host.LeftImage, host.LeftZoom);
        btnRl.Click += (_, _) => ResetZoom100(host.RightImage, host.RightZoom);
        btnRp.Click += (_, _) => ZoomIn(host.RightImage, host.RightZoom);
        btnRm.Click += (_, _) => ZoomOut(host.RightImage, host.RightZoom);

        leftScroll.PointerWheelChanged += (s, e) => OnPreviewWheel(e, host.LeftImage, host.LeftZoom);
        rightScroll.PointerWheelChanged += (s, e) => OnPreviewWheel(e, host.RightImage, host.RightZoom);

        SetLeftPreviewFromPath(host, path);
        SetRightPreviewBitmap(host, null, preserveZoom: false);
        TryLoadMetaSidecar(host);

        var tab = new TabItem
        {
            Header = Path.GetFileName(path),
            Content = splitRoot,
            Tag = host
        };
        SheetTabs.Items.Add(tab);
        SheetTabs.SelectedItem = tab;
    }

    private void ClearImages()
    {
        foreach (var item in SheetTabs.Items)
        {
            if (item is TabItem ti && ti.Tag is TabImageHost h)
            {
                h.EncryptedImageFull?.Dispose();
                h.DecryptedImageFull?.Dispose();
            }
        }
        _imagePaths.Clear();
        SheetTabs.Items.Clear();
        SheetTabs.IsVisible = false;
        Placeholder.IsVisible = true;
        if (LblImageStatus != null)
            LblImageStatus.Text = "就绪";
        AppendLog("已清空图片列表。");
    }

    private void AppendLog(string line)
    {
        void Do()
        {
            var prev = _log.Text ?? "";
            _log.Text = prev + $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}";
        }
        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }
}
