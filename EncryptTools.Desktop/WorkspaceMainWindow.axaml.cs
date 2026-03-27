using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using EncryptTools.Desktop.Dialogs;
using EncryptTools.Desktop.Input;
using EncryptTools.Desktop.Ui;
using EncryptTools.Desktop.Views;
using EncryptTools.Desktop.Workspace;

namespace EncryptTools.Desktop;

public partial class WorkspaceMainWindow : Window
{
    public WorkspaceMainWindow()
    {
        InitializeComponent();
        DragDrop.SetAllowDrop(this, true);
        DragDrop.SetAllowDrop(Tabs, true);
        // 在窗口级处理拖放（handledEventsToo），避免 TabControl/ListBox 等拦截后子 UserControl 收不到事件
        AddHandler(DragDrop.DragEnterEvent, OnWindowDragEnter, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnWindowDrop, RoutingStrategies.Bubble, handledEventsToo: true);
        Opened += OnMainWindowOpened;
        StatusLeft.Text = "就绪";

        var welcome = new WelcomeView();
        welcome.WorkspaceKindRequested += NewWorkspace;
        Tabs.Items.Add(new TabItem
        {
            Header = "欢迎",
            Content = welcome,
            Tag = null
        });
    }

    private void OnMainWindowOpened(object? sender, EventArgs e)
    {
        DragDropCompat.EnableAllowDropRecursive(this);
        // TabControl 在 XAML 填充早期会触发 SelectionChanged，当时 LogScrollHost 可能尚未构造
        SyncLogPanelToSelectedTab();
        
        // 加载 Logo 图像（使用根目录 app2.png）
        try
        {
            var logo = this.FindControl<Image>("LogoImage");
            if (logo != null)
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var logoPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "app2.png"));
                if (File.Exists(logoPath))
                {
                    logo.Source = new Bitmap(logoPath);
                }
                else
                {
                    logo.Source = new Bitmap(AssetLoader.Open(new Uri("avares://encryptTools/Assets/app2.png")));
                }
                logo.DoubleTapped += OnLogoDoubleTapped;
            }
        }
        catch { /* 可能 Logo 不存在 */ }
        
        // 设置窗口控制按钮的样式和内容
        SetupWindowControlButtons();
    }

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnLogoDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        // 双击 Logo 最大化/还原窗口
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            var btnMaxRestore = this.FindControl<Button>("BtnMaxRestore");
            if (btnMaxRestore != null && btnMaxRestore.Content is TextBlock tb)
                tb.Text = "🗗";  // 最大化图标
        }
        else
        {
            WindowState = WindowState.Maximized;
            var btnMaxRestore = this.FindControl<Button>("BtnMaxRestore");
            if (btnMaxRestore != null && btnMaxRestore.Content is TextBlock tb)
                tb.Text = "⬜";  // 还原图标
        }
    }

    private void SetupWindowControlButtons()
    {
        var btnMin = this.FindControl<Button>("BtnMinimize");
        var btnMax = this.FindControl<Button>("BtnMaxRestore");
        var btnClose = this.FindControl<Button>("BtnClose");
        
        // 最小化按钮
        if (btnMin != null)
        {
            btnMin.Content = new TextBlock { Text = "−", FontSize = 16, TextAlignment = Avalonia.Media.TextAlignment.Center };
        }
        
        // 最大化/还原按钮
        if (btnMax != null)
        {
            btnMax.Content = new TextBlock { Text = "🗗", FontSize = 14, TextAlignment = Avalonia.Media.TextAlignment.Center };
        }
        
        // 关闭按钮
        if (btnClose != null)
        {
            btnClose.Content = new TextBlock { Text = "✕", FontSize = 14, TextAlignment = Avalonia.Media.TextAlignment.Center };
        }
    }

    private void OnBtnMinimize(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnBtnMaxRestore(object? sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
            var btnMaxRestore = this.FindControl<Button>("BtnMaxRestore");
            if (btnMaxRestore != null && btnMaxRestore.Content is TextBlock tb)
                tb.Text = "🗗";  // 最大化图标
        }
        else
        {
            WindowState = WindowState.Maximized;
            var btnMaxRestore = this.FindControl<Button>("BtnMaxRestore");
            if (btnMaxRestore != null && btnMaxRestore.Content is TextBlock tb)
                tb.Text = "⬜";  // 还原图标
        }
    }

    private void OnBtnClose(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// 部分 Linux 文件管理器在 DragOver 阶段不提供完整 MIME；若先判 LooksLikeFileDrop 会得到 None，系统不会触发 Drop。
    /// 此处一律允许 Copy，由 <see cref="OnWindowDrop"/> 再解析路径（无路径则忽略）。
    /// </summary>
    private void OnWindowDragEnter(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        // 不因为先前 DragEnter/DragOver 已被标记为 Handled 而直接忽略 Drop
        List<string> paths;
        try
        {
            paths = DragDropPaths.DistinctPaths(DragDropPaths.GetPathsFromData(e.Data));
        }
        catch
        {
            return;
        }
        if (paths.Count == 0) return;
        e.Handled = true;
        var sourceVisual = e.Source as Visual;
        void Run()
        {
            try { TryRoutePathsToWorkspace(paths, sourceVisual); }
            catch { /* ignore */ }
        }
        if (Dispatcher.UIThread.CheckAccess())
            Run();
        else
            Dispatcher.UIThread.Post(Run);
    }

    /// <summary>
    /// 欢迎页 / 字符串工作区等无本地 Drop 时仍接收文件；优先当前选中工作区，其次第一个文件/图片工作区，否则新建文件工作区。
    /// </summary>
    private bool TryRoutePathsToWorkspace(IReadOnlyList<string> paths, Visual? sourceVisual)
    {
        FileWorkspaceView? file = null;
        ImageWorkspaceView? img = null;
        if (sourceVisual != null)
        {
            file = sourceVisual.FindAncestorOfType<FileWorkspaceView>(includeSelf: true);
            img = sourceVisual.FindAncestorOfType<ImageWorkspaceView>(includeSelf: true);
        }

        if (file != null)
        {
            file.ApplyDroppedPaths(paths, PathImportKind.RoutedFromMainWindow);
            return true;
        }
        if (img != null)
        {
            img.ApplyDroppedPaths(paths, PathImportKind.RoutedFromMainWindow);
            return true;
        }

        if (Tabs.SelectedItem is TabItem { Content: FileWorkspaceView f2 })
        {
            f2.ApplyDroppedPaths(paths, PathImportKind.RoutedFromMainWindow);
            return true;
        }
        if (Tabs.SelectedItem is TabItem { Content: ImageWorkspaceView i2 })
        {
            i2.ApplyDroppedPaths(paths, PathImportKind.RoutedFromMainWindow);
            return true;
        }

        foreach (var o in Tabs.Items)
        {
            if (o is TabItem { Content: FileWorkspaceView f3 })
            {
                f3.ApplyDroppedPaths(paths, PathImportKind.RoutedFromMainWindow);
                return true;
            }
        }
        foreach (var o in Tabs.Items)
        {
            if (o is TabItem { Content: ImageWorkspaceView i3 })
            {
                i3.ApplyDroppedPaths(paths, PathImportKind.RoutedFromMainWindow);
                return true;
            }
        }

        NewWorkspace("文件");
        if (Tabs.SelectedItem is TabItem { Content: FileWorkspaceView f4 })
        {
            f4.ApplyDroppedPaths(paths, PathImportKind.RoutedFromMainWindow);
            return true;
        }
        return false;
    }

    private void Tabs_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // TabControl.EndInit 时会触发 SelectionChanged，此时父窗口的 Tabs 字段可能尚未赋值，必须用 sender
        var tabControl = sender as TabControl ?? Tabs;
        if (tabControl == null) return;

        if (tabControl.SelectedItem is TabItem { Content: Visual v })
            DragDropCompat.EnableAllowDropRecursive(v);

        SyncLogPanelToSelectedTab(tabControl);
    }

    private void SyncLogPanelToSelectedTab(TabControl? tabControl = null)
    {
        tabControl ??= Tabs;
        if (tabControl == null || LogScrollHost == null || LogPlaceholder == null)
            return;
        if (tabControl.SelectedItem is TabItem ti && ti.Tag is TextBox log)
            LogScrollHost.Content = log;
        else
            LogScrollHost.Content = LogPlaceholder;
    }

    private void NewWorkspace(string kind)
    {
        var logBox = new TextBox
        {
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            FontFamily = "Consolas, monospace",
            MinHeight = 120
        };

        Control content = kind switch
        {
            "文件" => new FileWorkspaceView(logBox, s => StatusLeft.Text = s),
            "字符串" => new StringWorkspaceView(logBox),
            "图片" => new ImageWorkspaceView(logBox),
            _ => new TextBlock { Text = "未知工作区类型" }
        };

        var tab = new TabItem
        {
            Header = $"{kind}工作区 {Tabs.ItemCount}",
            Content = content,
            Tag = logBox
        };
        AttachWorkspaceTabContextMenu(tab);
        Tabs.Items.Add(tab);
        Tabs.SelectedItem = tab;
        if (content is Visual v)
            DragDropCompat.EnableAllowDropRecursive(v);
        StatusLeft.Text = $"已创建新工作区：{kind}";
    }

    private void AttachWorkspaceTabContextMenu(TabItem tab)
    {
        var menu = new ContextMenu();
        var close = new MenuItem { Header = "关闭此工作区" };
        close.Click += (_, _) => CloseWorkspaceTab(tab);
        menu.Items.Add(close);
        tab.ContextMenu = menu;
    }

    private void CloseWorkspaceTab(TabItem tab)
    {
        if (tab.Tag is null)
            return;
        var idx = Tabs.Items.IndexOf(tab);
        if (idx < 0)
            return;
        Tabs.Items.Remove(tab);
        if (Tabs.Items.Count > 0)
        {
            var next = Math.Min(idx, Tabs.Items.Count - 1);
            Tabs.SelectedIndex = next;
        }
        StatusLeft.Text = "就绪";
    }

    private void OnStatusRightPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var src = sender as Visual ?? this;
        if (e.GetCurrentPoint(src).Properties.IsLeftButtonPressed)
            NewWorkspace("文件");
    }

    private async void OnMenuOpenWorkspace(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        if (top == null)
            return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择保存的工作区文件（占位）",
            AllowMultiple = false
        });
        if (files.Count == 0)
            return;
        var p = files[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(p))
            StatusLeft.Text = "打开工作区: " + Path.GetFileName(p);
    }

    private void OnMenuNewFile(object? sender, RoutedEventArgs e) => NewWorkspace("文件");
    private void OnMenuNewString(object? sender, RoutedEventArgs e) => NewWorkspace("字符串");
    private void OnMenuNewImage(object? sender, RoutedEventArgs e) => NewWorkspace("图片");

    private void OnMenuCloseAll(object? sender, RoutedEventArgs e)
    {
        while (Tabs.Items.Count > 1)
            Tabs.Items.RemoveAt(Tabs.Items.Count - 1);
        Tabs.SelectedIndex = 0;
        StatusLeft.Text = "就绪";
    }

    private void OnMenuExit(object? sender, RoutedEventArgs e) => Close();

    private async void OnMenuNewPwd(object? sender, RoutedEventArgs e)
    {
        var dlg = new CreatePasswordFileWindow();
        await dlg.ShowDialog(this);
        if (dlg.Success)
            RefreshAllFileWorkspacePwdCombos();
    }

    private async void OnMenuImportPwd(object? sender, RoutedEventArgs e)
    {
        var dlg = new ImportPasswordWindow();
        await dlg.ShowDialog(this);
        if (dlg.Imported)
            RefreshAllFileWorkspacePwdCombos();
    }

    private async void OnMenuEditPwd(object? sender, RoutedEventArgs e)
    {
        var dlg = new EditPasswordWindow();
        await dlg.ShowDialog(this);
        if (dlg.Saved)
            RefreshAllFileWorkspacePwdCombos();
    }

    private void RefreshAllFileWorkspacePwdCombos()
    {
        foreach (var o in Tabs.Items)
        {
            if (o is TabItem { Content: FileWorkspaceView f })
                f.RefreshPwdCombo();
            else if (o is TabItem { Content: ImageWorkspaceView img })
                img.RefreshPwdCombo();
        }
    }

    private async void OnMenuHelp(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://github.com/xiaowen1448/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            await Messages.ShowAsync(this, "错误", "打开链接失败: " + ex.Message);
        }
    }

    private async void OnMenuAbout(object? sender, RoutedEventArgs e)
    {
        await Messages.ShowAsync(this, "关于", "encryptTools\n\n跨平台工作区（Avalonia）。与 Windows 版工作区结构对齐。");
    }
}
