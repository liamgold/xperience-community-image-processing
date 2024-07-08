using CMS.Core;
using CMS.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using SkiaSharp;
using System.Security.Cryptography;

namespace XperienceCommunity.ImageProcessing;

public class ImageProcessingMiddleware(RequestDelegate next, IEventLogService eventLogService)
{
    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
    private readonly IEventLogService _eventLogService = eventLogService ?? throw new ArgumentNullException(nameof(eventLogService));
    private readonly FileExtensionContentTypeProvider _contentTypeProvider = new();

    private readonly string[] _supportedContentTypes =
    [
        "image/jpeg",
        "image/png",
        "image/gif",
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

        if (context.Request.Path.StartsWithSegments("/getContentAsset"))
        {
            int? width = null;
            int? height = null;
            int? maxSideSize = null;

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

            if (width.HasValue || height.HasValue || maxSideSize.HasValue)
            {
                responseBodyStream.Seek(0, SeekOrigin.Begin);
                var originalImageBytes = responseBodyStream.ToArray();

                // Generate ETag
                var eTag = GenerateETag(originalImageBytes, width ?? 0, height ?? 0, maxSideSize ?? 0);

                // Check if the ETag matches the client's If-None-Match header
                if (context.Request.Headers.IfNoneMatch == eTag)
                {
                    context.Response.StatusCode = StatusCodes.Status304NotModified;
                    context.Response.Headers.ETag = eTag;
                    context.Response.Body = originalResponseBodyStream;
                    return;
                }

                var resizedImageBytes = await ResizeImageAsync(originalImageBytes, width ?? 0, height ?? 0, maxSideSize ?? 0, contentType);

                context.Response.Body = originalResponseBodyStream;
                context.Response.ContentType = contentType;
                context.Response.Headers.ETag = eTag;
                context.Response.Headers.CacheControl = "public, max-age=31536000";
                context.Response.Headers.ContentLength = resizedImageBytes.Length;

                if (context.Response.StatusCode != StatusCodes.Status304NotModified)
                {
                    await context.Response.Body.WriteAsync(resizedImageBytes);
                }
                return;
            }
        }

        await CopyStreamAndRestore(responseBodyStream, originalResponseBodyStream, context);
    }

    private async Task<byte[]> ResizeImageAsync(byte[] imageBytes, int width, int height, int maxSideSize, string contentType)
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
                _eventLogService.LogWarning(nameof(ImageProcessingMiddleware), nameof(ResizeImageAsync), "Failed to decode image.");
                return imageBytes;
            }

            var newDims = ImageHelper.EnsureImageDimensions(width, height, maxSideSize, originalBitmap.Width, originalBitmap.Height);

            using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(newDims[0], newDims[1]), SKFilterQuality.High);
            if (resizedBitmap == null)
            {
                _eventLogService.LogWarning(nameof(ImageProcessingMiddleware), nameof(ResizeImageAsync), "Failed to resize image.");
                return imageBytes;
            }

            using var outputStream = new MemoryStream();
            var imageFormat = GetImageFormat(contentType);
            await Task.Run(() => resizedBitmap.Encode(imageFormat, 80).SaveTo(outputStream));
            return outputStream.ToArray();
        }
        catch (Exception ex)
        {
            _eventLogService.LogException(nameof(ImageProcessingMiddleware), nameof(ResizeImageAsync), ex);
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

    private static SKEncodedImageFormat GetImageFormat(string contentType) => contentType switch
    {
        "image/jpeg" => SKEncodedImageFormat.Jpeg,
        "image/png" => SKEncodedImageFormat.Png,
        "image/gif" => SKEncodedImageFormat.Gif,
        "image/webp" => SKEncodedImageFormat.Webp,
        _ => SKEncodedImageFormat.Png,
    };

    private static string GenerateETag(byte[] imageBytes, int width, int height, int maxSideSize)
    {
        var inputBytes = imageBytes
            .Concat(BitConverter.GetBytes(width))
            .Concat(BitConverter.GetBytes(height))
            .Concat(BitConverter.GetBytes(maxSideSize))
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