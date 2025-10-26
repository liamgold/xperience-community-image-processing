namespace XperienceCommunity.ImageProcessing.Enums;

/// <summary>
/// Defines how an image should fit within the requested dimensions
/// </summary>
public enum FitMode
{
    /// <summary>
    /// Fit image inside dimensions, maintaining aspect ratio (default, letterbox if needed)
    /// </summary>
    Contain,

    /// <summary>
    /// Fill dimensions exactly, cropping excess while maintaining aspect ratio
    /// </summary>
    Cover,

    /// <summary>
    /// Stretch image to exact dimensions, ignoring aspect ratio
    /// </summary>
    Fill
}
