using Avalonia;
using Markdig;
using Markdig.Syntax;

namespace MkOptic.ViewModels;

/// <summary>A Markdown heading and its position in the source used to build the outline.</summary>
public sealed record DocumentHeading(string Title, int Level, int Offset, int EndOffset)
{
    public Thickness Indentation => new((Level - 1) * 14, 0, 0, 0);

    public static IReadOnlyList<DocumentHeading> Parse(string source) => Markdown.Parse(source)
        .Descendants<HeadingBlock>()
        .Select(heading => new DocumentHeading(
            GetTitle(source, heading),
            heading.Level, heading.Span.Start, heading.Span.End))
        .ToArray();

    private static string GetTitle(string source, HeadingBlock heading)
    {
        // ContainerInline does not carry a usable source span without precise-source tracking.
        // Render the complete heading block instead; this also strips Setext underline markers.
        var title = Markdown.ToPlainText(source.Substring(heading.Span.Start, heading.Span.Length)).Trim();
        return title.Length == 0 ? "(Untitled heading)" : title;
    }
}
