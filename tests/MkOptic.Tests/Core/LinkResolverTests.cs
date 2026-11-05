using MkOptic.Core.Documents;

namespace MkOptic.Tests.Core;

public sealed class LinkResolverTests
{
    private static readonly string DocumentDirectory = Path.Combine(Path.GetTempPath(), "docs");

    [Theory]
    [InlineData("https://example.com/page")]
    [InlineData("http://example.com")]
    [InlineData("mailto:user@example.com")]
    public void Resolve_WebAndMailLinks_AreExternal(string url)
    {
        var link = LinkResolver.Resolve(new Uri(url), DocumentDirectory);

        Assert.Equal(LinkKind.External, link.Kind);
        Assert.Equal(new Uri(url), link.ExternalUri);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com/file")]
    public void Resolve_OtherSchemes_AreUnsupported(string url)
    {
        Assert.Equal(LinkKind.Unsupported, LinkResolver.Resolve(new Uri(url), DocumentDirectory).Kind);
    }

    [Fact]
    public void Resolve_RelativeLink_IsResolvedAgainstDocumentDirectory_WithoutFragment()
    {
        var link = LinkResolver.Resolve(new Uri("guide/Read%20me.md#setup", UriKind.Relative), DocumentDirectory);

        Assert.Equal(LinkKind.LocalFile, link.Kind);
        Assert.Equal(Path.GetFullPath(Path.Combine(DocumentDirectory, "guide", "Read me.md")), link.LocalPath);
    }

    [Fact]
    public void Resolve_AnchorOnlyLink_IsUnsupported()
    {
        Assert.Equal(LinkKind.Unsupported, LinkResolver.Resolve(new Uri("#section", UriKind.Relative), DocumentDirectory).Kind);
    }

    [Fact]
    public void Resolve_RelativeLinkInUnsavedDocument_IsUnsupported()
    {
        Assert.Equal(LinkKind.Unsupported, LinkResolver.Resolve(new Uri("other.md", UriKind.Relative), null).Kind);
    }
}
