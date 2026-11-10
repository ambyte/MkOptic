using LiveMarkdown.Avalonia;
using Xunit;

namespace MkOptic.Tests.Preview;

public sealed class ImageDecoderTests
{
    [Fact]
    public void ConfigureImageDecoders_RegistersSvgBeforeBitmapFallback()
    {
        App.ConfigureImageDecoders();

        Assert.Collection(
            AsyncImageLoader.DefaultDecoders,
            decoder => Assert.Same(SvgImageDecoder.Shared, decoder),
            decoder => Assert.Same(DefaultBitmapDecoder.Shared, decoder));
    }
}
