using MkOptic.Core.Documents;

namespace MkOptic.Tests.Core;

public sealed class DocumentStatisticsTests
{
    [Fact]
    public void Calculate_EmptyText_HasOneLineAndNoWords()
    {
        var statistics = DocumentStatistics.Calculate(string.Empty);

        Assert.Equal(new DocumentStatistics(1, 0, 0), statistics);
    }

    [Theory]
    [InlineData("one\ntwo", 2)]
    [InlineData("one\r\ntwo\r\nthree", 3)]
    [InlineData("one\rtwo", 2)]
    [InlineData("trailing\n", 2)]
    public void Calculate_CountsLinesForAllLineEndings(string text, int expectedLines)
    {
        Assert.Equal(expectedLines, DocumentStatistics.Calculate(text).Lines);
    }

    [Fact]
    public void Calculate_CountsWordsIgnoringMarkdownSyntax()
    {
        var statistics = DocumentStatistics.Calculate("# Hello, **world**! It's 2026.");

        Assert.Equal(4, statistics.Words);
    }

    [Fact]
    public void Calculate_CountsCharactersIncludingWhitespace()
    {
        Assert.Equal(10, DocumentStatistics.Calculate("short text").Characters);
    }
}
