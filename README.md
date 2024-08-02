a # Xperience Community: Image Processing

## Description

This package provides a way to resize images and convert them to `webp`, `jpg`, and `png` formats. It supports images from *Media libraries* and Content hub items stored as *Content item assets*.

## Library Version Matrix

| Xperience Version | Library Version |
| ----------------- | --------------- |
| >= 29.1.4         | 1.0.0           |

## Dependencies

- [ASP.NET Core 8.0](https://dotnet.microsoft.com/en-us/download)
- [Xperience by Kentico](https://docs.xperience.io/xp/changelog)

## Package Installation

Add the package to your application using the .NET CLI

```powershell
dotnet add package XperienceCommunity.ImageProcessing
```

## Quick Start

1. Install NuGet package above.

1. Add the following configuration to your `appsettings.json`:

   ```json
    {
      "ImageProcessing": {
        "ProcessMediaLibrary": true,
        "ProcessContentItemAssets": true
      }
    }
   ```
   
    - `ProcessMediaLibrary`: Set to `true` to enable image processing for Media library images. Defaults to `true`.
    - `ProcessContentItemAssets`: Set to `true` to enable image processing for Content Hub assets. Defaults to `true`.

    
1. Register the Image Processing middleware using `app.UseXperienceCommunityImageProcessing()`:

   ```csharp
    var app = builder.Build();

    app.UseKentico();

    // ...
   
    builder.Services.Configure<ImageProcessingOptions>(builder.Configuration.GetSection("ImageProcessing"));
   
    app.UseXperienceCommunityImageProcessing();
   ```

                          
1. You should be able to use the `width`, `height`, and `maxSideSize` query parameters on your image URLs to resize the image. Examples:

    1. Resize the Media library image to a width of 100px:
       ```
       https://yourdomain.com/getmedia/rest-of-your-asset-url?width=100
       ```
    1. Resize the Content item asset image to a height of 100px:
       ```
       https://yourdomain.com/getContentAsset/rest-of-your-asset-url?height=100
       ```
       
1. You can also use the `format` query parameter to convert the image to a different format. Allowed values are: `webp`, `jpg` and `png`. Example:

    1. Convert the Media library image to `webp`:
       ```
       https://yourdomain.com/getmedia/rest-of-your-asset-url?format=webp
       ```
   1. Convert the Content item asset image to `png`:
      ```
      https://yourdomain.com/getContentAsset/rest-of-your-asset-url?format=png
      ```


## Contributing

Feel free to submit issues or pull requests to the repository, this is a community package and everyone is welcome to support.

## License

Distributed under the MIT License. See [`LICENSE.md`](LICENSE.md) for more information.