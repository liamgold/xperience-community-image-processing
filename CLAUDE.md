# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a **Xperience by Kentico** middleware library that provides image processing capabilities for the Xperience by Kentico platform. It's packaged as a NuGet package for distribution.

**Note:** Kentico now offers native Image Variants (https://docs.kentico.com/documentation/business-users/content-hub/content-item-assets#image-variants) for Content Hub assets. This package remains useful for Media library support, on-the-fly processing, and dynamic transformations. Future deprecation is planned as Kentico's native solution matures.

**Key Technology Stack:**
- Backend: .NET 8.0, ASP.NET Core, Xperience by Kentico 30.11.1+
- Image Processing: SkiaSharp 3.119.1
- Supported formats: WebP, JPEG, PNG

## Architecture

### ASP.NET Core Middleware Pattern

This project provides an ASP.NET Core middleware that intercepts image requests and performs on-the-fly processing:

1. **Middleware Registration** (`src/Middleware/ImageProcessingMiddleware.cs`)
   - `UseXperienceCommunityImageProcessing()` extension method registers the middleware
   - Intercepts requests to `/getmedia/*` (Media library) and `/getContentAsset/*` (Content hub assets)

2. **Image Processing**
   - Query parameters: `width`, `height`, `maxSideSize`, `format`, `fit`, `crop`
   - Supports format conversion: `webp`, `jpg`, `png`
   - Supports fit modes: `contain` (default), `cover`, `fill`
   - Supports crop positioning: `center` (default), `north`, `south`, `east`, `west`, `northeast`, `northwest`, `southeast`, `southwest`
   - Uses SkiaSharp for high-quality image resizing
   - ETags for client-side caching

3. **Configuration Options** (`ImageProcessingOptions`)
   - `ProcessMediaLibrary`: Enable/disable processing for Media library images (default: true)
   - `ProcessContentItemAssets`: Enable/disable processing for Content hub assets (default: true)
   - `MaxWidth`: Maximum allowed width in pixels, requests exceeding this are capped (default: 5000)
   - `MaxHeight`: Maximum allowed height in pixels, requests exceeding this are capped (default: 5000)
   - `MaxSideSize`: Maximum allowed maxSideSize parameter, requests exceeding this are capped (default: 5000)
   - `Quality`: JPEG/WebP encoding quality 1-100, higher is better quality but larger file size (default: 80)

### Configuration System

The middleware supports optional configuration via ASP.NET Core configuration:

```json
{
  "ImageProcessing": {
    "ProcessMediaLibrary": true,
    "ProcessContentItemAssets": true,
    "MaxWidth": 5000,
    "MaxHeight": 5000,
    "MaxSideSize": 5000,
    "Quality": 80
  }
}
```

Registered in the consuming app via:
```csharp
// Optional: Configure which paths to process
builder.Services.Configure<ImageProcessingOptions>(builder.Configuration.GetSection("ImageProcessing"));

var app = builder.Build();

// Required: Register Kentico first
app.UseKentico();

// Then register image processing middleware
app.UseXperienceCommunityImageProcessing();
```

## Build Commands

### Full Build (for NuGet Package)
```bash
dotnet restore
dotnet build --configuration Release
```

The NuGet package is auto-generated in `src/bin/Release/` due to `<GeneratePackageOnBuild>true</GeneratePackageOnBuild>`.

### Testing
There are no automated tests in this project currently. The `examples/DancingGoat` project serves as an integration test environment.

## Dependency Management

This project uses **Central Package Management** via `Directory.Packages.props`:
- All NuGet package versions are centralized in `Directory.Packages.props`
- Individual `.csproj` files reference packages WITHOUT version attributes
- `packages.lock.json` is generated and committed for reproducible builds
- Kentico packages are currently at 30.11.1

**Important:** When updating Kentico packages, update all three together:
- `Kentico.Xperience.webapp`
- `Kentico.Xperience.admin`
- `Kentico.Xperience.azurestorage`

## Key Implementation Details

### Request Flow
1. Middleware intercepts image requests to `/getmedia/*` or `/getContentAsset/*`
2. Original image is fetched by calling next middleware in pipeline
3. Response is captured in a memory stream
4. If resize/format query parameters are present, image is processed
5. ETag is generated from original image bytes + parameters
6. If client's If-None-Match matches ETag, return 304 Not Modified
7. Otherwise, process image with SkiaSharp and return modified response
8. Cache-Control header set to 1 year for processed images

### Image Processing
- Uses SkiaSharp for high-performance image manipulation
- `SKSamplingOptions` with linear filtering for best resize quality
- Configurable quality setting (default: 80) for JPEG/WebP encoding
- Maintains aspect ratio when only width or height specified
- `maxSideSize` parameter scales largest dimension to specified size
- Automatic parameter validation: dimensions exceeding configured maximums are clamped
- Proper resource disposal with try-finally pattern for bitmap memory management

### Fit Modes
- **contain** (default): Fit image inside dimensions, maintaining aspect ratio (letterbox if needed)
- **cover**: Fill dimensions exactly, cropping excess while maintaining aspect ratio
- **fill**: Stretch image to exact dimensions, ignoring aspect ratio

### Crop Positioning
When using `fit=cover`, the `crop` parameter controls where to crop from:
- **center** (default): Crop from center
- **north**: Crop from top center
- **south**: Crop from bottom center
- **east**: Crop from middle right
- **west**: Crop from middle left
- **northeast**, **northwest**, **southeast**, **southwest**: Corner positions

### Caching Strategy
- ETags generated from MD5 hash of: original image bytes + width + height + maxSideSize + format + fitMode + cropPosition
- Clients can use If-None-Match header for cache validation
- Cache-Control: public, max-age=31536000 (1 year)
- Content-Disposition: inline with proper filename and extension
- Different fit/crop combinations generate unique ETags for proper cache differentiation

## CI/CD

GitHub Actions workflows:
- **CI** (`.github/workflows/ci.yml`): Runs on PRs to main, builds and tests
- **Publish** (`.github/workflows/publish.yml`): Publishes to NuGet.org on GitHub releases using OIDC trusted publishing

Dependabot (`.github/dependabot.yml`):
- Weekly updates for NuGet packages (ignores Kentico.Xperience.*)
- Weekly updates for GitHub Actions

## Example Project

The `examples/DancingGoat` directory contains a sample Xperience by Kentico website that demonstrates the middleware integration. This serves as both documentation and a manual testing environment.

## Common Gotchas

1. **Middleware order matters:** Must be called AFTER `app.UseKentico()` so Kentico can serve the original images
2. **SkiaSharp native dependencies:** The package includes `SkiaSharp.NativeAssets.Linux.NoDependencies` for Linux deployments
3. **Memory usage:** Large images are loaded into memory for processing - monitor memory usage in high-traffic scenarios
4. **ETag caching:** ETags are based on original image content, so clearing CDN/browser cache may be needed when images are updated
5. **Parameter clamping:** Dimension values exceeding configured maximums (MaxWidth, MaxHeight, MaxSideSize) are automatically capped, not rejected
6. **Quality clamping:** Quality values outside 1-100 range are clamped to valid range
