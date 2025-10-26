using CMS.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text;
using XperienceCommunity.ImageProcessing.Enums;
using XperienceCommunity.ImageProcessing.Options;
using Path = System.IO.Path;

namespace XperienceCommunity.ImageProcessing;

public class ImageProcessingMiddleware(RequestDelegate next, IEventLogService eventLogService, IOptions<ImageProcessingOptions>? options)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly IEventLogService _eventLogService = eventLogService ?? throw new ArgumentNullException(nameof(eventLogService));
    private readonly ImageProcessingOptions _options = options?.Value ?? new ImageProcessingOptions();
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    private readonly string[] _supportedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/webp"
    ];

    /// <summary>
    /// Validates and clamps dimension parameters to configured maximums
    /// </summary>
    private (int? width, int? height, int? maxSideSize) ValidateAndClampDimensions(int? width, int? height, int? maxSideSize)
    {
        int? validatedWidth = width.HasValue && width.Value > 0
            ? Math.Min(width.Value, _options.MaxWidth)
            : null;

        int? validatedHeight = height.HasValue && height.Value > 0
            ? Math.Min(height.Value, _options.MaxHeight)
            : null;

        int? validatedMaxSideSize = maxSideSize.HasValue && maxSideSize.Value > 0
            ? Math.Min(maxSideSize.Value, _options.MaxSideSize)
            : null;

        return (validatedWidth, validatedHeight, validatedMaxSideSize);
    }

    /// <summary>
    /// Calculate target dimensions based on fit mode
    /// </summary>
    private (int width, int height) CalculateDimensions(int requestedWidth, int requestedHeight, int maxSideSize,
        int originalWidth, int originalHeight, FitMode fitMode)
    {
        // Handle maxSideSize parameter
        if (maxSideSize > 0)
        {
            var largestSide = Math.Max(originalWidth, originalHeight);
            if (largestSide > maxSideSize)
            {
                var scale = (double)maxSideSize / largestSide;
                return ((int)(originalWidth * scale), (int)(originalHeight * scale));
            }
            return (originalWidth, originalHeight);
        }

        // If only one dimension specified, calculate the other maintaining aspect ratio
        if (requestedWidth > 0 && requestedHeight <= 0)
        {
            var scale = (double)requestedWidth / originalWidth;
            return (requestedWidth, (int)(originalHeight * scale));
        }

        if (requestedHeight > 0 && requestedWidth <= 0)
        {
            var scale = (double)requestedHeight / originalHeight;
            return ((int)(originalWidth * scale), requestedHeight);
        }

        // Both dimensions specified
        if (requestedWidth > 0 && requestedHeight > 0)
        {
            switch (fitMode)
            {
                case FitMode.Fill:
                    // Stretch to exact dimensions, ignore aspect ratio
                    return (requestedWidth, requestedHeight);

                case FitMode.Cover:
                    // Fill the box, maintain aspect ratio, crop excess
                    var scaleWidth = (double)requestedWidth / originalWidth;
                    var scaleHeight = (double)requestedHeight / originalHeight;
                    var scale = Math.Max(scaleWidth, scaleHeight); // Use larger scale to fill
                    return ((int)(originalWidth * scale), (int)(originalHeight * scale));

                case FitMode.Contain:
                default:
                    // Fit inside box, maintain aspect ratio
                    scaleWidth = (double)requestedWidth / originalWidth;
                    scaleHeight = (double)requestedHeight / originalHeight;
                    scale = Math.Min(scaleWidth, scaleHeight); // Use smaller scale to fit
                    return ((int)(originalWidth * scale), (int)(originalHeight * scale));
            }
        }

        // No dimensions specified
        return (originalWidth, originalHeight);
    }

    /// <summary>
    /// Calculate crop rectangle based on position
    /// </summary>
    private SKRectI CalculateCropRect(int imageWidth, int imageHeight, int targetWidth, int targetHeight, CropPosition position)
    {
        // If image is smaller than target, no cropping needed
        if (imageWidth <= targetWidth && imageHeight <= targetHeight)
        {
            return new SKRectI(0, 0, imageWidth, imageHeight);
        }

        var cropWidth = Math.Min(imageWidth, targetWidth);
        var cropHeight = Math.Min(imageHeight, targetHeight);

        int x = 0, y = 0;

        // Calculate horizontal position
        switch (position)
        {
            case CropPosition.East:
            case CropPosition.NorthEast:
            case CropPosition.SouthEast:
                x = imageWidth - cropWidth;
                break;
            case CropPosition.West:
            case CropPosition.NorthWest:
            case CropPosition.SouthWest:
                x = 0;
                break;
            default: // Center
                x = (imageWidth - cropWidth) / 2;
                break;
        }

        // Calculate vertical position
        switch (position)
        {
            case CropPosition.South:
            case CropPosition.SouthEast:
            case CropPosition.SouthWest:
                y = imageHeight - cropHeight;
                break;
            case CropPosition.North:
            case CropPosition.NorthEast:
            case CropPosition.NorthWest:
                y = 0;
                break;
            default: // Center
                y = (imageHeight - cropHeight) / 2;
                break;
        }

        return new SKRectI(x, y, x + cropWidth, y + cropHeight);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalResponseBodyStream = context.Response.Body;
        var contentType = GetContentTypeFromPath(context.Request.Path);

        using var responseBodyStream = new MemoryStream();
        context.Response.Body = responseBodyStream;

        await _next(context);

        if (!IsSupportedContentType(contentType))
        {
            await CopyStreamAndRestore(responseBodyStream, originalResponseBodyStream, context);
            return;
        }

        if (IsPathToBeProcessed(context.Request.Path, _options))
        {
            int? width = null;
            int? height = null;
            int? maxSideSize = null;
            var format = contentType;
            var fitMode = FitMode.Contain;
            var cropPosition = CropPosition.Center;

            if (context.Request.Query.ContainsKey("width") && int.TryParse(context.Request.Query["width"], out int parsedWidth))
            {
                width = parsedWidth;
            }

            if (context.Request.Query.ContainsKey("height") && int.TryParse(context.Request.Query["height"], out int parsedHeight))
            {
                height = parsedHeight;
            }

            if (context.Request.Query.ContainsKey("maxSideSize") && int.TryParse(context.Request.Query["maxSideSize"], out int parsedMaxSideSize))
            {
                maxSideSize = parsedMaxSideSize;
            }

            // Validate and clamp dimensions to configured maximums
            (width, height, maxSideSize) = ValidateAndClampDimensions(width, height, maxSideSize);

            if (context.Request.Query.ContainsKey("fit"))
            {
                var fitParam = context.Request.Query["fit"].ToString().ToLowerInvariant();
                fitMode = fitParam switch
                {
                    "cover" => FitMode.Cover,
                    "fill" => FitMode.Fill,
                    "contain" => FitMode.Contain,
                    _ => FitMode.Contain
                };
            }

            if (context.Request.Query.ContainsKey("crop"))
            {
                var cropParam = context.Request.Query["crop"].ToString().ToLowerInvariant();
                cropPosition = cropParam switch
                {
                    "north" => CropPosition.North,
                    "south" => CropPosition.South,
                    "east" => CropPosition.East,
                    "west" => CropPosition.West,
                    "northeast" => CropPosition.NorthEast,
                    "northwest" => CropPosition.NorthWest,
                    "southeast" => CropPosition.SouthEast,
                    "southwest" => CropPosition.SouthWest,
                    "center" => CropPosition.Center,
                    _ => CropPosition.Center
                };
            }

            if (context.Request.Query.ContainsKey("format"))
            {
                string? formatParsed = context.Request.Query["format"];

                if (!string.IsNullOrEmpty(formatParsed))
                {
                    if (!formatParsed.StartsWith("image/")) formatParsed = $"image/{formatParsed}";
                    if (formatParsed == "image/jpg") formatParsed = "image/jpeg";
                    if (IsSupportedContentType(formatParsed)) format = formatParsed;
                }
            }

            if (width.HasValue || height.HasValue || maxSideSize.HasValue)
            {
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                var originalImageBytes = responseBodyStream.ToArray();

                // Generate ETag
                var eTag = GenerateETag(originalImageBytes, width ?? 0, height ?? 0, maxSideSize ?? 0, format, fitMode, cropPosition);

                // Check if the ETag matches the client's If-None-Match header
                if (context.Request.Headers.IfNoneMatch == eTag)
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.Headers.ETag = eTag;
                    context.Response.Body = originalResponseBodyStream;
                    return;
                }

                var processedImageBytes = await ProcessImageAsync(originalImageBytes, width ?? 0, height ?? 0, maxSideSize ?? 0, format, contentType, fitMode, cropPosition);

                var filename = $"{Path.GetFileNameWithoutExtension(context.Request.Path)}.{GetFileExtensionByContentType(format)}";

                context.Response.Body = originalResponseBodyStream;
                context.Response.ContentType = format;
                context.Response.Headers.ETag = eTag;
                context.Response.Headers.CacheControl = "public, max-age=31536000";
                context.Response.Headers.ContentLength = processedImageBytes.Length;
                context.Response.Headers.ContentDisposition = $"inline; filename={filename}";

                if (context.Response.StatusCode != StatusCodes.Status304NotModified)
                {
                    await context.Response.Body.WriteAsync(processedImageBytes);
                }
                return;
            }
        }

        await CopyStreamAndRestore(responseBodyStream, originalResponseBodyStream, context);
    }

    private async Task<byte[]> ProcessImageAsync(byte[] imageBytes, int width, int height, int maxSideSize, string format, string contentType, FitMode fitMode, CropPosition cropPosition)
    {
        if (imageBytes.Length == 0 || !IsSupportedContentType(contentType))
        {
            return imageBytes;
        }

        if (width <= 0 && height <= 0 && maxSideSize <= 0 && format == contentType)
        {
            return imageBytes;
        }

        try
        {
            using var inputStream = new MemoryStream(imageBytes);
            using var originalBitmap = SKBitmap.Decode(inputStream);
            if (originalBitmap == null)
            {
                _eventLogService.LogWarning(nameof(ImageProcessingMiddleware), nameof(ProcessImageAsync), "Failed to decode image.");
                return imageBytes;
            }

            SKBitmap? resizedBitmap = null;
            SKBitmap? croppedBitmap = null;
            try
            {
                var bitmapToProcess = originalBitmap;

                if (width > 0 || height > 0 || maxSideSize > 0)
                {
                    // Calculate target dimensions based on fit mode
                    var (targetWidth, targetHeight) = CalculateDimensions(width, height, maxSideSize,
                        originalBitmap.Width, originalBitmap.Height, fitMode);

                    // Resize the image
                    resizedBitmap = originalBitmap.Resize(new SKImageInfo(targetWidth, targetHeight),
                        new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
                    if (resizedBitmap == null)
                    {
                        _eventLogService.LogWarning(nameof(ImageProcessingMiddleware), nameof(ProcessImageAsync), "Failed to resize image.");
                        return imageBytes;
                    }

                    bitmapToProcess = resizedBitmap;

                    // Apply cropping if using cover mode and image is larger than requested dimensions
                    if (fitMode == FitMode.Cover && width > 0 && height > 0 &&
                        (targetWidth > width || targetHeight > height))
                    {
                        var cropRect = CalculateCropRect(targetWidth, targetHeight, width, height, cropPosition);
                        croppedBitmap = new SKBitmap(width, height);

                        using var canvas = new SKCanvas(croppedBitmap);
                        canvas.DrawBitmap(resizedBitmap, cropRect, new SKRect(0, 0, width, height));

                        bitmapToProcess = croppedBitmap;
                    }
                }

                using var outputStream = new MemoryStream();
                var imageFormat = GetImageFormat(format);
                var quality = Math.Clamp(_options.Quality, 1, 100);
                await Task.Run(() => bitmapToProcess.Encode(imageFormat, quality).SaveTo(outputStream));
                return outputStream.ToArray();
            }
            finally
            {
                // Dispose created bitmaps if they were created (different from original)
                resizedBitmap?.Dispose();
                croppedBitmap?.Dispose();
            }
        }
        catch (Exception ex)
        {
            _eventLogService.LogException(nameof(ImageProcessingMiddleware), nameof(ProcessImageAsync), ex);
            return imageBytes;
        }
    }

    private string GetContentTypeFromPath(PathString path)
    {
        var extension = CMS.IO.Path.GetExtension(path.Value).ToLowerInvariant();

        if (_contentTypeProvider.TryGetContentType(extension, out var contentType))
        {
            return contentType;
        }

        return "application/octet-stream";
    }

    private bool IsSupportedContentType(string contentType)
    {
        return Array.Exists(_supportedContentTypes, ct => ct.Equals(contentType, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsPathMediaLibrary(PathString path) => path.StartsWithSegments("/getmedia");
    private static bool IsPathContentItemAsset(PathString path) => path.StartsWithSegments("/getContentAsset");

    private static bool IsPathToBeProcessed(PathString path, ImageProcessingOptions options)
    {
        // Use default values if not set
        var processMediaLibrary = options.ProcessMediaLibrary ?? true;
        var processContentItemAssets = options.ProcessContentItemAssets ?? true;

        if (processMediaLibrary && IsPathMediaLibrary(path))
        {
            return true;
        }

        return processContentItemAssets && IsPathContentItemAsset(path);
    }

    private static SKEncodedImageFormat GetImageFormat(string contentType) => contentType switch
    {
        "image/jpeg" => SKEncodedImageFormat.Jpeg,
        "image/png" => SKEncodedImageFormat.Png,
        "image/webp" => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Webp,
    };

    private static string GetFileExtensionByContentType(string contentType) => contentType switch
    {
        "image/jpeg" => "jpg",
        "image/png" => "png",
        "image/webp" => "webp",
        _ => "webp",
    };

    private static string GenerateETag(byte[] imageBytes, int width, int height, int maxSideSize, string format, FitMode fitMode, CropPosition cropPosition)
    {
        var inputBytes = imageBytes
            .Concat(BitConverter.GetBytes(width))
            .Concat(BitConverter.GetBytes(height))
            .Concat(BitConverter.GetBytes(maxSideSize))
            .Concat(Encoding.UTF8.GetBytes(format))
            .Concat(BitConverter.GetBytes((int)fitMode))
            .Concat(BitConverter.GetBytes((int)cropPosition))
            .ToArray();

        var hash = MD5.HashData(inputBytes);
        return Convert.ToBase64String(hash);
    }

    private static async Task CopyStreamAndRestore(MemoryStream responseBodyStream, Stream originalResponseBodyStream, HttpContext context)
    {
        responseBodyStream.Seek(0, SeekOrigin.Begin);
        await responseBodyStream.CopyToAsync(originalResponseBodyStream);
        context.Response.Body = originalResponseBodyStream;
    }
}

public static class ImageProcessingMiddlewareExtensions
{
    /// <summary>
    ///     Add the Image Processing middleware.
    /// </summary>
    /// <param name="builder">The Microsoft.AspNetCore.Builder.IApplicationBuilder to add the middleware to.</param>
    /// <returns></returns>
    public static IApplicationBuilder UseXperienceCommunityImageProcessing(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ImageProcessingMiddleware>();
    }
}