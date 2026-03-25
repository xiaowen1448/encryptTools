using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace EncryptTools.Desktop.Input;

/// <summary>
/// Linux/X11 上部分子控件未正确继承 AllowDrop，导致拖放无法落到目标；显式遍历可视树并开启拖放。
/// ListBox/TabControl 等可能在 DragOver 阶段将效果置为 None，或抢先处理 Drop，需在目标控件上显式注册 Bubble + handledEventsToo。
/// </summary>
public static class DragDropCompat
{
    /// <summary>对根结点及其所有可视子结点设置 <see cref="DragDrop.SetAllowDrop(Interactive, bool)"/>。</summary>
    public static void EnableAllowDropRecursive(Visual? root)
    {
        if (root == null) return;
        if (root is Interactive i)
            DragDrop.SetAllowDrop(i, true);
        foreach (var child in root.GetVisualChildren())
            EnableAllowDropRecursive(child);
    }

    /// <summary>
    /// 在宿主上注册 Enter/Over/Drop：DragOver 一律 Copy，Drop 解析路径后交给 <paramref name="applyPaths"/>。
    /// 使用 handledEventsToo，避免子控件默认行为吞掉 Drop。
    /// </summary>
    public static void AttachStandardFileDrop(Interactive host, Action<IReadOnlyList<string>> applyPaths)
    {
        void OnDragOverAccept(object? s, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        void OnDrop(object? s, DragEventArgs e)
        {
            List<string> paths;
            try
            {
                paths = DragDropPaths.DistinctPaths(DragDropPaths.GetPathsFromData(e.Data));
            }
            catch
            {
                return;
            }
            // 即使解析到 0 条路径也回调，便于工作区日志中区分「Drop 已到达但无路径」与「未收到 Drop」
            // Linux 上 Drop 偶发在非 UI 线程；必须在 UI 线程更新控件，否则会崩溃或列表不刷新
            e.Handled = true;
            void Run()
            {
                try { applyPaths(paths); }
                catch
                {
                    /* 避免未处理异常导致进程退出 */
                }
            }
            if (Dispatcher.UIThread.CheckAccess())
                Run();
            else
                Dispatcher.UIThread.Post(Run);
        }

        host.AddHandler(DragDrop.DragEnterEvent, OnDragOverAccept, RoutingStrategies.Bubble, handledEventsToo: true);
        host.AddHandler(DragDrop.DragOverEvent, OnDragOverAccept, RoutingStrategies.Bubble, handledEventsToo: true);
        host.AddHandler(DragDrop.DropEvent, OnDrop, RoutingStrategies.Bubble, handledEventsToo: true);
    }

    /// <summary>
    /// 对常作为「命中叶子」的控件再挂一层 DragOver=Copy（例如 ListBox、TabControl），避免系统因效果为 None 而不触发 Drop。
    /// </summary>
    public static void AttachTargetDragOverCopy(Interactive target)
    {
        void OnDragOverAccept(object? s, DragEventArgs e)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        target.AddHandler(DragDrop.DragEnterEvent, OnDragOverAccept, RoutingStrategies.Bubble, handledEventsToo: true);
        target.AddHandler(DragDrop.DragOverEvent, OnDragOverAccept, RoutingStrategies.Bubble, handledEventsToo: true);
    }
}
