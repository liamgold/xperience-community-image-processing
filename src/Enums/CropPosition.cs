namespace XperienceCommunity.ImageProcessing.Enums;

/// <summary>
/// Defines the position from which to crop when using FitMode.Cover
/// </summary>
public enum CropPosition
{
    /// <summary>
    /// Crop from center (default)
    /// </summary>
    Center,

    /// <summary>
    /// Crop from top center
    /// </summary>
    North,

    /// <summary>
    /// Crop from bottom center
    /// </summary>
    South,

    /// <summary>
    /// Crop from middle left
    /// </summary>
    West,

    /// <summary>
    /// Crop from middle right
    /// </summary>
    East,

    /// <summary>
    /// Crop from top left
    /// </summary>
    NorthWest,

    /// <summary>
    /// Crop from top right
    /// </summary>
    NorthEast,

    /// <summary>
    /// Crop from bottom left
    /// </summary>
    SouthWest,

    /// <summary>
    /// Crop from bottom right
    /// </summary>
    SouthEast
}
