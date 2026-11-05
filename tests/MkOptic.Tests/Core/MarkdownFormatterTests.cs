using MkOptic.Core.Editing;

namespace MkOptic.Tests.Core;

public sealed class MarkdownFormatterTests
{
    [Fact]
    public void Bold_WrapsSelection_AndKeepsTextSelected()
    {
        var result = Apply(MarkdownFormat.Bold, "say hello now", new TextRange(4, 5));

        Assert.Equal("say **hello** now", result.Text);
        Assert.Equal("hello", result.Text.Substring(result.Selection.Start, result.Selection.Length));
    }

    [Fact]
    public void Bold_OnAlreadyWrappedSelection_RemovesMarkers()
    {
        var result = Apply(MarkdownFormat.Bold, "say **hello** now", new TextRange(6, 5));

        Assert.Equal("say hello now", result.Text);
        Assert.Equal(new TextRange(4, 5), result.Selection);
    }

    [Fact]
    public void Bold_WhenSelectionIncludesMarkers_RemovesMarkers()
    {
        var result = Apply(MarkdownFormat.Bold, "**hello**", new TextRange(0, 9));

        Assert.Equal("hello", result.Text);
    }

    [Fact]
    public void Italic_InsideBold_WrapsInsteadOfStrippingBoldMarker()
    {
        var result = Apply(MarkdownFormat.Italic, "**hello**", new TextRange(2, 5));

        Assert.Equal("***hello***", result.Text);
    }

    [Fact]
    public void InlineMath_WithEmptySelection_InsertsPlaceholderAndSelectsIt()
    {
        var result = Apply(MarkdownFormat.InlineMath, "x ", new TextRange(2, 0));

        Assert.Equal("x $E = mc^2$", result.Text);
        Assert.Equal("E = mc^2", result.Text.Substring(result.Selection.Start, result.Selection.Length));
    }

    [Fact]
    public void Link_UsesSelectionAsLabel_AndSelectsUrl()
    {
        var result = Apply(MarkdownFormat.Link, "see docs", new TextRange(4, 4));

        Assert.Equal("see [docs](https://example.com)", result.Text);
        Assert.Equal("https://example.com", result.Text.Substring(result.Selection.Start, result.Selection.Length));
    }

    [Fact]
    public void BulletList_PrefixesEveryNonEmptyLine()
    {
        var result = Apply(MarkdownFormat.BulletList, "one\n\ntwo", new TextRange(0, 8));

        Assert.Equal("- one\n\n- two", result.Text);
    }

    [Fact]
    public void BulletList_WhenAllLinesPrefixed_RemovesPrefix()
    {
        var result = Apply(MarkdownFormat.BulletList, "- one\n- two", new TextRange(0, 11));

        Assert.Equal("one\ntwo", result.Text);
    }

    [Fact]
    public void NumberedList_NumbersLinesSequentially()
    {
        var result = Apply(MarkdownFormat.NumberedList, "a\nb\nc", new TextRange(0, 5));

        Assert.Equal("1. a\n2. b\n3. c", result.Text);
    }

    [Fact]
    public void Heading_WithCaretInLine_PrefixesWholeLine_AndMovesCaretToLineEnd()
    {
        var result = Apply(MarkdownFormat.Heading1, "intro\ntitle\nbody", new TextRange(8, 0));

        Assert.Equal("intro\n# title\nbody", result.Text);
        Assert.Equal(new TextRange(13, 0), result.Selection);
    }

    [Theory]
    [InlineData(MarkdownFormat.Heading2, "title", "## title")]
    [InlineData(MarkdownFormat.Heading3, "title", "### title")]
    [InlineData(MarkdownFormat.Heading2, "# title", "## title")]
    [InlineData(MarkdownFormat.Heading1, "### title", "# title")]
    [InlineData(MarkdownFormat.Heading2, "## title", "title")]
    [InlineData(MarkdownFormat.Heading1, "# title", "title")]
    public void Heading_SetsReplacesOrRemovesLevel(MarkdownFormat format, string input, string expected)
    {
        var result = Apply(format, input, new TextRange(0, 0));

        Assert.Equal(expected, result.Text);
    }

    [Fact]
    public void TaskList_Toggle_AddsAndRemovesCheckbox()
    {
        var added = Apply(MarkdownFormat.TaskList, "task", new TextRange(0, 0));
        var removed = Apply(MarkdownFormat.TaskList, "- [x] done", new TextRange(0, 0));

        Assert.Equal("- [ ] task", added.Text);
        Assert.Equal("done", removed.Text);
    }

    [Fact]
    public void Table_IsSeparatedFromPrecedingParagraphByBlankLine()
    {
        var result = Apply(MarkdownFormat.Table, "Paragraph", new TextRange(9, 0));

        Assert.StartsWith("Paragraph\n\n| Header 1 |", result.Text, StringComparison.Ordinal);
        Assert.EndsWith("|\n", result.Text, StringComparison.Ordinal);
        Assert.Equal("Header 1", result.Text.Substring(result.Selection.Start, result.Selection.Length));
    }

    [Fact]
    public void CodeBlock_WrapsSelectedLines()
    {
        var result = Apply(MarkdownFormat.CodeBlock, "var x = 1;", new TextRange(0, 10));

        Assert.Equal("```\nvar x = 1;\n```\n", result.Text);
    }

    [Fact]
    public void MathBlock_UsesWindowsLineEndingsWhenDocumentDoes()
    {
        var result = Apply(MarkdownFormat.MathBlock, "a\r\n", new TextRange(3, 0));

        Assert.Equal("a\r\n\r\n$$\r\nE = mc^2\r\n$$\r\n", result.Text);
    }

    [Fact]
    public void HorizontalRule_InTheMiddleOfText_IsSurroundedByBlankLines()
    {
        var result = Apply(MarkdownFormat.HorizontalRule, "abovebelow", new TextRange(5, 0));

        Assert.Equal("above\n\n---\n\nbelow", result.Text);
    }

    [Fact]
    public void Apply_WithSelectionOutsideText_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MarkdownFormatter.Apply(MarkdownFormat.Bold, "abc", new TextRange(2, 5)));
    }

    /// <summary>Applies the formatter and returns the resulting text with the new selection.</summary>
    private static (string Text, TextRange Selection) Apply(MarkdownFormat format, string text, TextRange selection)
    {
        var edit = MarkdownFormatter.Apply(format, text, selection);
        var result = string.Concat(text.AsSpan(0, edit.Offset), edit.Text, text.AsSpan(edit.Offset + edit.Length));
        return (result, edit.Selection);
    }
}
