namespace XperienceCommunity.ImageProcessing.Options;

/// <summary>
/// Configuration options for the Image Processing middleware
/// </summary>
public class ImageProcessingOptions
{
    /// <summary>
    /// Enable or disable processing for Media library images. Default: true
    /// </summary>
    public bool? ProcessMediaLibrary { get; set; } = true;

    /// <summary>
    /// Enable or disable processing for Content hub assets. Default: true
    /// </summary>
    public bool? ProcessContentItemAssets { get; set; } = true;

    /// <summary>
    /// Maximum allowed width in pixels. Requests exceeding this will be capped. Default: 5000
    /// </summary>
    public int MaxWidth { get; set; } = 5000;

    /// <summary>
    /// Maximum allowed height in pixels. Requests exceeding this will be capped. Default: 5000
    /// </summary>
    public int MaxHeight { get; set; } = 5000;

    /// <summary>
    /// Maximum allowed value for maxSideSize parameter. Requests exceeding this will be capped. Default: 5000
    /// </summary>
    public int MaxSideSize { get; set; } = 5000;

    /// <summary>
    /// JPEG/WebP quality (1-100). Higher is better quality but larger file size. Default: 80
    /// </summary>
    public int Quality { get; set; } = 80;
}
