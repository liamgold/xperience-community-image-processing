using CMS.Core;
using CMS.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using SkiaSharp;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
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
                var eTag = GenerateETag(originalImageBytes, width ?? 0, height ?? 0, maxSideSize ?? 0, format);

                // Check if the ETag matches the client's If-None-Match header
                if (context.Request.Headers.IfNoneMatch == eTag)
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.Headers.ETag = eTag;
                    context.Response.Body = originalResponseBodyStream;
                    return;
                }

                var processedImageBytes = await ProcessImageAsync(originalImageBytes, width ?? 0, height ?? 0, maxSideSize ?? 0, format, contentType, context.Request.Path);

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

    private async Task<byte[]> ProcessImageAsync(byte[] imageBytes, int width, int height, int maxSideSize, string format, string contentType, string path)
    {
        if (imageBytes.Length == 0 || !IsSupportedContentType(contentType))
        {
            return imageBytes;
        }

        if (width <= 0 && height <= 0 && maxSideSize <= 0)
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

            var resizedBitmap = originalBitmap;

            // Resize the image if it is a Content item asset only as Media library images are already resized by XbyK
            if (IsPathContentItemAsset(path))
            {
                var newDims = ImageHelper.EnsureImageDimensions(width, height, maxSideSize, originalBitmap.Width, originalBitmap.Height);
                resizedBitmap = originalBitmap.Resize(new SKImageInfo(newDims[0], newDims[1]), SKFilterQuality.High);
                if (resizedBitmap == null)
                {
                    _eventLogService.LogWarning(nameof(ImageProcessingMiddleware), nameof(ProcessImageAsync), "Failed to resize image.");
                    return imageBytes;
                }
            }

            using var outputStream = new MemoryStream();
            var imageFormat = GetImageFormat(format);
            await Task.Run(() => resizedBitmap.Encode(imageFormat, 80).SaveTo(outputStream));
            return outputStream.ToArray();
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
        // Set default values
        var processMediaLibrary = options.ProcessMediaLibrary ??= true;
        var processContentItemAssets = options.ProcessContentItemAssets ??= true;

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

    private static string GenerateETag(byte[] imageBytes, int width, int height, int maxSideSize, string format)
    {
        var inputBytes = imageBytes
            .Concat(BitConverter.GetBytes(width))
            .Concat(BitConverter.GetBytes(height))
            .Concat(BitConverter.GetBytes(maxSideSize))
            .Concat(Encoding.UTF8.GetBytes(format))
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

public class ImageProcessingOptions
{
    public bool? ProcessMediaLibrary { get; set; } = true;
    public bool? ProcessContentItemAssets { get; set; } = true;
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