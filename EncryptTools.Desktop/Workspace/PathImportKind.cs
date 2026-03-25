namespace EncryptTools.Desktop.Workspace;

/// <summary>文件/图片路径进入工作区的方式（用于日志区分）。</summary>
public enum PathImportKind
{
    /// <summary>在工作区控件上拖放</summary>
    DragDrop,
    /// <summary>剪贴板粘贴路径</summary>
    Paste,
    /// <summary>主窗口将拖放转发到当前/目标工作区</summary>
    RoutedFromMainWindow
}
