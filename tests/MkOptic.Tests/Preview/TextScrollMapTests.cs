using MkOptic.Views;

namespace MkOptic.Tests.Preview;

public sealed class TextScrollMapTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(50, 20)]
    [InlineData(100, 40)]
    [InlineData(150, 340)]
    [InlineData(200, 640)]
    [InlineData(250, 660)]
    [InlineData(300, 680)]
    public void UnequalBlockHeights_MapCorrespondingTextInBothDirections(double source, double preview)
    {
        var map = CreateMap();

        Assert.Equal(preview, map.ToPreview(source), 6);
        Assert.Equal(source, map.ToSource(preview), 6);
    }

    [Theory]
    [InlineData(-100, 0, 0)]
    [InlineData(10000, 680, 300)]
    public void OutsideDocument_ClampsToNearestTextAnchor(double value, double preview, double source)
    {
        var map = CreateMap();

        Assert.Equal(preview, map.ToPreview(value));
        Assert.Equal(source, map.ToSource(value));
    }

    [Fact]
    public void SharedRowsAndDuplicateOffsets_KeepMapMonotone()
    {
        var map = new TextScrollMap([new(0, 0), new(10, 30), new(10, 40), new(20, 30), new(30, 20), new(40, 60)]);

        Assert.Equal(30, map.ToPreview(10));
        Assert.Equal(10, map.ToSource(30));
        Assert.Equal(45, map.ToPreview(25));
        Assert.Equal(25, map.ToSource(45));
    }

    [Fact]
    public void EmptyMap_IsSafe()
    {
        var map = new TextScrollMap([]);

        Assert.True(map.IsEmpty);
        Assert.Equal(0, map.ToPreview(100));
        Assert.Equal(0, map.ToSource(100));
    }

    [Fact]
    public void SingleAnchor_IsSafe()
    {
        var map = new TextScrollMap([new(12, 34)]);

        Assert.False(map.IsEmpty);
        Assert.Equal(34, map.ToPreview(100));
        Assert.Equal(12, map.ToSource(100));
    }

    private static TextScrollMap CreateMap() => new([new(0, 0), new(100, 40), new(200, 640), new(300, 680)]);
}
