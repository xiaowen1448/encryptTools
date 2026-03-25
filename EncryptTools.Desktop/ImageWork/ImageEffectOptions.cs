namespace EncryptTools.Desktop.ImageWork;

/// <summary>与 Windows 版 ImageWorkspacePanel 中 JSON 序列化字段一致。</summary>
public enum ImageMode
{
    Mosaic = 0,
    Permutation = 1,
    XorStream = 2,
    BlockShuffle = 3,
    ArnoldCat = 4
}

public sealed class ImageEffectOptions
{
    public int Version { get; set; } = 1;
    public ImageMode Mode { get; set; }
    public int BlockSize { get; set; } = 16;
    public int Iterations { get; set; } = 200_000;
    public string SaltBase64 { get; set; } = "";
    public string? PasswordFileName { get; set; }
    public bool PixelationEnabled { get; set; }
    public bool IconOverlayEnabled { get; set; }
    public int OverlayOpacityPercent { get; set; } = 80;
    public int IconOverlayBlockSizeHint { get; set; } = 32;
    public string? IconOverlayBlocksEncryptedBase64 { get; set; }
    public int IconOverlayBlockSize { get; set; }
    /// <summary>是否启用图标无序化（随机旋转、随机偏移、杂乱覆盖）。</summary>
    public bool IconRandomize { get; set; }
}
