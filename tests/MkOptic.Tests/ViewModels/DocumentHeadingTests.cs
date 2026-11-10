using MkOptic.ViewModels;

namespace MkOptic.Tests.ViewModels;

public sealed class DocumentHeadingTests
{
    [Fact]
    public void Parse_ExtractsAllLevelsAndUnderlinedHeadingsInSourceOrder()
    {
        const string source = "# First\n\n## Second\n\n### Third\n\n#### Fourth\n\n##### Fifth\n\n###### Sixth\n\nUnderlined\n==========\n\nSubtitle\n--------\n";

        var headings = DocumentHeading.Parse(source);

        Assert.Equal([1, 2, 3, 4, 5, 6, 1, 2], headings.Select(heading => heading.Level));
        Assert.Equal(["First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Underlined", "Subtitle"],
            headings.Select(heading => heading.Title));
        Assert.Equal(source.IndexOf("## Second", StringComparison.Ordinal), headings[1].Offset);
        Assert.Equal(14, headings[1].Indentation.Left);
    }

    [Fact]
    public void Parse_UsesVisibleInlineTextAndIgnoresCodeBlocks()
    {
        const string source = "# **Bold** and [link](https://example.com) with `code` &amp; text\n\n```md\n# Not a heading\n```\n\n    ## Also code\n\n## Same\n\n## Same\n";

        var headings = DocumentHeading.Parse(source);

        Assert.Equal(["Bold and link with code & text", "Same", "Same"], headings.Select(heading => heading.Title));
        Assert.NotEqual(headings[1].Offset, headings[2].Offset);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ordinary paragraph\n\n---\n\nAnother paragraph.")]
    public void Parse_WithoutHeadings_ReturnsEmptyOutline(string source)
    {
        Assert.Empty(DocumentHeading.Parse(source));
    }
}
