using System.Text;
using System.Text.RegularExpressions;

namespace MkOptic.Core.Editing;

/// <summary>
/// Computes Markdown formatting edits for a text and the current selection.
/// The formatter is pure: it never mutates the text, it only describes the edit to apply.
/// </summary>
public static partial class MarkdownFormatter
{
    private const string TextPlaceholder = "text";
    private const string CodePlaceholder = "code";
    private const string FormulaPlaceholder = "E = mc^2";
    private const string LinkTextPlaceholder = "link text";
    private const string UrlPlaceholder = "https://example.com";
    private const string ImageAltPlaceholder = "description";
    private const string ImagePathPlaceholder = "image.png";
    private const string TableHeaderPlaceholder = "Header 1";

    public static TextEdit Apply(MarkdownFormat format, string text, TextRange selection)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (selection.Start < 0 || selection.Length < 0 || selection.End > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(selection), selection, "Selection is outside of the text.");
        }

        return format switch
        {
            MarkdownFormat.Bold => ToggleWrap(text, selection, "**", TextPlaceholder),
            MarkdownFormat.Italic => ToggleWrap(text, selection, "*", TextPlaceholder),
            MarkdownFormat.Strikethrough => ToggleWrap(text, selection, "~~", TextPlaceholder),
            MarkdownFormat.InlineCode => ToggleWrap(text, selection, "`", CodePlaceholder),
            MarkdownFormat.InlineMath => ToggleWrap(text, selection, "$", FormulaPlaceholder),
            MarkdownFormat.Link => InsertLink(text, selection, isImage: false),
            MarkdownFormat.Image => InsertLink(text, selection, isImage: true),
            MarkdownFormat.Heading1 => ToggleHeading(text, selection, 1),
            MarkdownFormat.Heading2 => ToggleHeading(text, selection, 2),
            MarkdownFormat.Heading3 => ToggleHeading(text, selection, 3),
            MarkdownFormat.Quote => ToggleLinePrefix(text, selection, QuotePrefix(), _ => "> "),
            MarkdownFormat.BulletList => ToggleLinePrefix(text, selection, BulletPrefix(), _ => "- "),
            MarkdownFormat.NumberedList => ToggleLinePrefix(text, selection, NumberedPrefix(), i => $"{i + 1}. "),
            MarkdownFormat.TaskList => ToggleLinePrefix(text, selection, TaskPrefix(), _ => "- [ ] "),
            MarkdownFormat.CodeBlock => InsertFencedBlock(text, selection, "```", CodePlaceholder),
            MarkdownFormat.MathBlock => InsertFencedBlock(text, selection, "$$", FormulaPlaceholder),
            MarkdownFormat.Table => InsertTable(text, selection),
            MarkdownFormat.HorizontalRule => InsertBlock(text, selection, "---", innerOffset: 3, innerLength: 0),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null),
        };
    }

    private static TextEdit ToggleWrap(string text, TextRange selection, string marker, string placeholder)
    {
        var selected = text.Substring(selection.Start, selection.Length);

        if (IsWrappedOutside(text, selection, marker))
        {
            var start = selection.Start - marker.Length;
            return new TextEdit(start, selection.Length + (2 * marker.Length), selected, new TextRange(start, selection.Length));
        }

        if (selected.Length >= 2 * marker.Length
            && selected.StartsWith(marker, StringComparison.Ordinal)
            && selected.EndsWith(marker, StringComparison.Ordinal))
        {
            var inner = selected[marker.Length..^marker.Length];
            return new TextEdit(selection.Start, selection.Length, inner, new TextRange(selection.Start, inner.Length));
        }

        var content = selected.Length == 0 ? placeholder : selected;
        return new TextEdit(
            selection.Start,
            selection.Length,
            string.Concat(marker, content, marker),
            new TextRange(selection.Start + marker.Length, content.Length));
    }

    /// <summary>
    /// Checks that the selection is directly surrounded by the marker and that the marker is not part
    /// of a longer run of the same character (so italic "*" does not match inside bold "**").
    /// </summary>
    private static bool IsWrappedOutside(string text, TextRange selection, string marker)
    {
        var before = selection.Start - marker.Length;
        var after = selection.End;
        if (before < 0 || after + marker.Length > text.Length)
        {
            return false;
        }

        if (!text.AsSpan(before, marker.Length).SequenceEqual(marker) || !text.AsSpan(after, marker.Length).SequenceEqual(marker))
        {
            return false;
        }

        var markerChar = marker[0];
        var isLongerRunBefore = before > 0 && text[before - 1] == markerChar;
        var isLongerRunAfter = after + marker.Length < text.Length && text[after + marker.Length] == markerChar;
        return !isLongerRunBefore && !isLongerRunAfter;
    }

    private static TextEdit InsertLink(string text, TextRange selection, bool isImage)
    {
        var selected = text.Substring(selection.Start, selection.Length);
        var label = selected.Length > 0 ? selected : isImage ? ImageAltPlaceholder : LinkTextPlaceholder;
        var target = isImage ? ImagePathPlaceholder : UrlPlaceholder;
        var prefix = isImage ? "![" : "[";
        var replacement = $"{prefix}{label}]({target})";
        var targetStart = selection.Start + prefix.Length + label.Length + 2;
        return new TextEdit(selection.Start, selection.Length, replacement, new TextRange(targetStart, target.Length));
    }

    /// <summary>
    /// Sets the heading level of the lines, removes it when every line already has exactly that level,
    /// and replaces a different heading level instead of stacking markers.
    /// </summary>
    private static TextEdit ToggleHeading(string text, TextRange selection, int level)
    {
        var marker = new string('#', level);
        var levelPrefix = new Regex($@"^{marker}[ \t]+(?!#)", RegexOptions.None, TimeSpan.FromSeconds(1));
        return ToggleLinePrefix(text, selection, levelPrefix, _ => $"{marker} ", HeadingPrefix());
    }

    private static TextEdit ToggleLinePrefix(
        string text,
        TextRange selection,
        Regex existingPrefix,
        Func<int, string> prefixFactory,
        Regex? replacedPrefix = null)
    {
        var (blockStart, blockEnd) = GetLineBlock(text, selection);
        var lines = text[blockStart..blockEnd].Split('\n');
        var nonEmptyLines = lines.Where(line => line.Trim().Length > 0).ToArray();
        var removePrefix = nonEmptyLines.Length > 0 && nonEmptyLines.All(existingPrefix.IsMatch);

        var builder = new StringBuilder(blockEnd - blockStart + (lines.Length * 4));
        var itemIndex = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            var line = lines[i];
            if (removePrefix)
            {
                builder.Append(existingPrefix.Replace(line, string.Empty, 1));
            }
            else if (line.Trim().Length > 0 || lines.Length == 1)
            {
                var content = replacedPrefix?.Replace(line, string.Empty, 1) ?? line;
                builder.Append(prefixFactory(itemIndex++)).Append(content);
            }
            else
            {
                builder.Append(line);
            }
        }

        var replacement = builder.ToString();
        var newSelection = selection.Length == 0
            ? new TextRange(blockStart + replacement.Length - TrailingCarriageReturnLength(replacement), 0)
            : new TextRange(blockStart, replacement.Length);
        return new TextEdit(blockStart, blockEnd - blockStart, replacement, newSelection);
    }

    private static int TrailingCarriageReturnLength(string value) => value.EndsWith('\r') ? 1 : 0;

    /// <summary>
    /// Expands the selection to whole lines. A trailing line break at the end of the selection
    /// does not pull the next line into the block.
    /// </summary>
    private static (int Start, int End) GetLineBlock(string text, TextRange selection)
    {
        var start = selection.Start == 0 ? 0 : text.LastIndexOf('\n', selection.Start - 1) + 1;
        var searchFrom = selection.Length > 0 && text[selection.End - 1] == '\n' ? selection.End - 1 : selection.End;
        var end = text.IndexOf('\n', searchFrom);
        return (start, end < 0 ? text.Length : end);
    }

    private static TextEdit InsertFencedBlock(string text, TextRange selection, string fence, string placeholder)
    {
        var newLine = DetectNewLine(text);
        var selected = text.Substring(selection.Start, selection.Length).TrimEnd('\r', '\n');
        var content = selected.Length > 0 ? selected : placeholder;
        var block = string.Concat(fence, newLine, content, newLine, fence);
        return InsertBlock(text, selection, block, fence.Length + newLine.Length, content.Length);
    }

    private static TextEdit InsertTable(string text, TextRange selection)
    {
        var newLine = DetectNewLine(text);
        string[] rows =
        [
            $"| {TableHeaderPlaceholder} | Header 2 | Header 3 |",
            "| :--- | :---: | ---: |",
            "| Cell | Cell | Cell |",
            "| Cell | Cell | Cell |",
        ];
        return InsertBlock(text, selection, string.Join(newLine, rows), innerOffset: 2, TableHeaderPlaceholder.Length);
    }

    /// <summary>
    /// Inserts a block element separated from surrounding paragraphs by blank lines,
    /// otherwise Markdown would merge it with the neighbouring text.
    /// </summary>
    private static TextEdit InsertBlock(string text, TextRange selection, string block, int innerOffset, int innerLength)
    {
        var newLine = DetectNewLine(text);
        var before = SeparatorBefore(text, selection.Start, newLine);
        var after = SeparatorAfter(text, selection.End, newLine);
        var replacement = string.Concat(before, block, after);
        var selectionStart = selection.Start + before.Length + innerOffset;
        return new TextEdit(selection.Start, selection.Length, replacement, new TextRange(selectionStart, innerLength));
    }

    private static string SeparatorBefore(string text, int position, string newLine)
    {
        if (position == 0)
        {
            return string.Empty;
        }

        var preceding = text.AsSpan(0, position).TrimEnd(' ');
        if (preceding.EndsWith($"{newLine}{newLine}") || preceding.Length == 0)
        {
            return string.Empty;
        }

        return preceding.EndsWith(newLine) ? newLine : newLine + newLine;
    }

    private static string SeparatorAfter(string text, int position, string newLine)
    {
        if (position == text.Length)
        {
            return newLine;
        }

        return text.AsSpan(position).StartsWith(newLine) ? newLine : newLine + newLine;
    }

    private static string DetectNewLine(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    [GeneratedRegex(@"^#{1,6}[ \t]+")]
    private static partial Regex HeadingPrefix();

    [GeneratedRegex(@"^>[ \t]?")]
    private static partial Regex QuotePrefix();

    [GeneratedRegex(@"^[-*+][ \t]+(?!\[[ xX]\])")]
    private static partial Regex BulletPrefix();

    [GeneratedRegex(@"^\d+[.)][ \t]+")]
    private static partial Regex NumberedPrefix();

    [GeneratedRegex(@"^[-*+][ \t]+\[[ xX]\][ \t]+")]
    private static partial Regex TaskPrefix();
}
