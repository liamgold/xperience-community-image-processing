# Xperience Community: Image Processing

## Description

Xperience by Kentico comes with image processing abilities for the media library [Kentico.Xperience.ImageProcessing](https://www.nuget.org/packages/Kentico.Xperience.ImageProcessing) but lacks the ability to process images stored as assets in the Content Hub.

Image processing capabilities are on the [roadmap](https://roadmap.kentico.com/c/227-media-asset-transformations) for the Content Hub, but in the meantime, this package provides a way to processing Content Hub assets in the same way as media library images, through the use of SkiaSharp.

NOTE: This package **will** eventually be deprecated once the Content Hub has image processing capabilities.

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

1. Register the Image Processing middleware using `app.UseXperienceCommunityImageProcessing()`:

   ```csharp
    var app = builder.Build();

    app.UseKentico();

    // ...

    app.UseXperienceCommunityImageProcessing();
   ```

                          
1. You should be able to use the `width`, `height`, and `maxSideSize` query parameters on your Content Hub asset URLs to resize the image. Examples:

    1. Resize the image to a width of 100px:
       ```
       https://yourdomain.com/your-asset-url?width=100
       ```
    1. Resize the image to a height of 100px:
       ```
       https://yourdomain.com/your-asset-url?height=100
       ```
    1. Resize the image to a maximum side size of 100px:
       ```
       https://yourdomain.com/your-asset-url?maxSideSize=100
       ```


## Contributing

Feel free to submit issues or pull requests to the repository, this is a community package and everyone is welcome to support.

## License

Distributed under the MIT License. See [`LICENSE.md`](LICENSE.md) for more information.