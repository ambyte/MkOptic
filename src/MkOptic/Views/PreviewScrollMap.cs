using Avalonia;
using Avalonia.VisualTree;
using LiveMarkdown.Avalonia;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MkOptic.Views;

/// <summary>Extracts text anchors from the committed preview, including nested list and table text.</summary>
internal static class PreviewScrollMap
{
    public static TextScrollMap Create(MarkdownRenderer preview, string source)
    {
        var anchors = new List<TextScrollMap.Anchor> { new(0, 0) };
        var literals = preview.DocumentUpdate?.Document.Descendants<LiteralInline>()
            .OrderBy(literal => literal.Span.Start).ToArray() ?? [];
        foreach (var block in preview.GetVisualDescendants().OfType<MarkdownTextBlock>())
        {
            var span = block.SourceSpan;
            if (span.Start < 0 || span.End < span.Start || span.End >= source.Length ||
                block.Bounds.Height <= 0 || block.TranslatePoint(default, preview) is not { } origin)
            {
                continue;
            }

            var anchorCount = anchors.Count;
            var layout = block.LayoutText;
            var searchStart = 0;
            for (var literalIndex = FindFirstLiteral(literals, span.Start); literalIndex < literals.Length; literalIndex++)
            {
                var literal = literals[literalIndex];
                if (literal.Span.Start > span.End)
                {
                    break;
                }

                if (literal.Span.End > span.End)
                {
                    continue;
                }

                var text = literal.Content.ToString();
                if (text.Length == 0)
                {
                    continue;
                }

                var index = layout.IndexOf(text, searchStart, StringComparison.Ordinal);
                if (index < 0)
                {
                    continue;
                }

                // Some extension parsers include leading cell padding in a literal's span.
                var sourceIndex = source.IndexOf(text, literal.Span.Start,
                    Math.Min(source.Length - literal.Span.Start, literal.Span.Length), StringComparison.Ordinal);
                AddTextLines(anchors, block, origin, index, text.Length,
                    sourceIndex >= 0 ? sourceIndex : literal.Span.Start);
                searchStart = index + text.Length;
            }

            // Code text has no LiteralInline nodes. Exact line matching also covers plain text
            // supplied by extension controls; searches stay inside this block's source span.
            if (searchStart == 0)
            {
                AddMatchingLines(anchors, block, origin, source, span.Start, span.End + 1);
            }

            if (anchors.Count == anchorCount)
            {
                anchors.Add(new(span.Start, origin.Y));
            }
        }

        anchors.Add(new(source.Length, preview.Bounds.Height));
        return new TextScrollMap(anchors);
    }

    private static int FindFirstLiteral(LiteralInline[] literals, int offset)
    {
        var low = 0;
        var high = literals.Length;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (literals[middle].Span.Start < offset)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private static void AddTextLines(
        List<TextScrollMap.Anchor> anchors,
        MarkdownTextBlock block,
        Point origin,
        int layoutStart,
        int length,
        int sourceStart)
    {
        foreach (var line in block.TextLayout.TextLines)
        {
            var index = Math.Max(layoutStart, line.FirstTextSourceIndex);
            if (index >= layoutStart + length || index >= line.FirstTextSourceIndex + line.Length)
            {
                continue;
            }

            var bounds = block.GetTextRangeBoundsInControl(index, 1);
            if (bounds.Count > 0)
            {
                anchors.Add(new(sourceStart + index - layoutStart, origin.Y + bounds[0].Top));
            }
        }
    }

    private static void AddMatchingLines(
        List<TextScrollMap.Anchor> anchors,
        MarkdownTextBlock block,
        Point origin,
        string source,
        int sourceStart,
        int sourceEnd)
    {
        foreach (var line in block.TextLayout.TextLines)
        {
            var start = line.FirstTextSourceIndex;
            var length = Math.Min(line.Length, block.LayoutText.Length - start);
            if (length <= 0)
            {
                continue;
            }

            var text = block.LayoutText.Substring(start, length).TrimEnd('\r', '\n');
            var match = source.IndexOf(text, sourceStart, sourceEnd - sourceStart, StringComparison.Ordinal);
            if (text.Length == 0 || match < 0)
            {
                continue;
            }

            AddTextLines(anchors, block, origin, start, text.Length, match);
            sourceStart = match + text.Length;
        }
    }
}
