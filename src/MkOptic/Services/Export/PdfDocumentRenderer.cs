using System.Globalization;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using MigraDoc.DocumentObjectModel;
using PdfSharp.Drawing;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using PdfTable = MigraDoc.DocumentObjectModel.Tables.Table;

namespace MkOptic.Services.Export;

internal static class PdfDocumentRenderer
{
    public static byte[] Render(MarkdownDocument markdown, string title)
    {
        ExportFontResolver.EnsureInitialized();
        var document = new Document();
        document.Info.Title = title;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Inter";
        normal.Font.Size = 10;
        normal.ParagraphFormat.SpaceAfter = 6;
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(2);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(2);
        section.PageSetup.RightMargin = Unit.FromCentimeter(2);
        section.Footers.Primary.AddParagraph().AddPageField();
        AddBlocks(section.Elements, markdown, 0);
        if (section.Elements.Count == 0)
        {
            section.AddParagraph();
        }

        var renderer = new MigraDoc.Rendering.PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static void AddBlocks(DocumentElements target, ContainerBlock blocks, int depth)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var headingParagraph = AddParagraph(target, heading.Inline);
                    headingParagraph.Format.Font.Size = Math.Max(11, 25 - (heading.Level * 3));
                    headingParagraph.Format.Font.Bold = true;
                    headingParagraph.Format.SpaceBefore = 12;
                    headingParagraph.Format.KeepWithNext = true;
                    headingParagraph.Format.OutlineLevel = (OutlineLevel)heading.Level;
                    break;
                case ParagraphBlock paragraph:
                    AddParagraph(target, paragraph.Inline);
                    break;
                case MathBlock math:
                    AddCode(target, math.Lines.ToString());
                    break;
                case CodeBlock code:
                    AddCode(target, code.Lines.ToString());
                    break;
                case ListBlock list:
                    var number = int.TryParse(list.OrderedStart, CultureInfo.InvariantCulture, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var before = target.Count;
                        AddBlocks(target, item, depth + 1);
                        if (target.Count > before && target[before] is Paragraph first)
                        {
                            first.Elements.InsertObject(0, new Text(list.IsOrdered ? $"{number++}. " : "• "));
                            first.Format.LeftIndent = Unit.FromCentimeter((depth + 1) * 0.5);
                        }
                    }

                    break;
                case QuoteBlock quote:
                    var firstIndex = target.Count;
                    AddBlocks(target, quote, depth);
                    for (var i = firstIndex; i < target.Count; i++)
                    {
                        if (target[i] is Paragraph quoted)
                        {
                            quoted.Format.LeftIndent = Unit.FromCentimeter(0.6);
                            quoted.Format.Font.Color = Colors.DimGray;
                        }
                    }

                    break;
                case MarkdownTable table:
                    AddTable(target, table);
                    break;
                case ThematicBreakBlock:
                    var rule = AddParagraph(target, null);
                    rule.Format.Borders.Bottom.Width = 0.5;
                    rule.Format.SpaceAfter = 10;
                    break;
                case ContainerBlock container:
                    AddBlocks(target, container, depth);
                    break;
                case LeafBlock leaf:
                    if (leaf.Inline is not null)
                    {
                        AddParagraph(target, leaf.Inline);
                    }
                    else if (leaf.Lines.Count > 0)
                    {
                        AddCode(target, leaf.Lines.ToString());
                    }

                    break;
            }
        }
    }

    private static Paragraph AddParagraph(DocumentElements target, ContainerInline? inline)
    {
        var paragraph = new Paragraph();
        target.Add(paragraph);
        if (inline is not null)
        {
            AddInlines(paragraph.Elements, inline);
        }

        return paragraph;
    }

    private static void AddCode(DocumentElements target, string code)
    {
        // Keep separate lines pageable, including code blocks taller than an A4 page.
        foreach (var line in code.ReplaceLineEndings("\n").Split('\n'))
        {
            var paragraph = AddParagraph(target, null);
            paragraph.AddText(line.Length == 0 ? " " : line.Replace("\t", "    ", StringComparison.Ordinal));
            paragraph.Format.Font.Size = 9;
            paragraph.Format.Shading.Color = Colors.WhiteSmoke;
            paragraph.Format.SpaceAfter = 0;
        }
    }

    private static void AddTable(DocumentElements target, MarkdownTable markdown)
    {
        var rows = markdown.OfType<TableRow>().ToArray();
        var columnCount = rows.Length == 0 ? 0 : rows.Max(row => row.Count);
        if (columnCount == 0)
        {
            return;
        }

        var table = new PdfTable();
        target.Add(table);
        table.Borders.Width = 0.5;
        table.Borders.Color = Colors.LightGray;
        for (var i = 0; i < columnCount; i++)
        {
            table.AddColumn(Unit.FromCentimeter(17.0 / columnCount));
        }

        foreach (var sourceRow in rows)
        {
            var row = table.AddRow();
            row.HeadingFormat = sourceRow.IsHeader;
            row.Format.Font.Bold = sourceRow.IsHeader;
            if (sourceRow.IsHeader)
            {
                row.Shading.Color = Colors.WhiteSmoke;
            }

            for (var i = 0; i < sourceRow.Count; i++)
            {
                if (sourceRow[i] is TableCell cell)
                {
                    AddBlocks(row.Cells[i].Elements, cell, 0);
                }
            }
        }

        AddParagraph(target, null);
    }

    private static void AddInlines(ParagraphElements target, ContainerInline container)
    {
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    target.Add(new Text(literal.Content.ToString()));
                    break;
                case CodeInline code:
                    var codeText = new FormattedText();
                    codeText.AddText(code.Content);
                    codeText.Font.Size = 9;
                    target.Add(codeText);
                    break;
                case MathInline math:
                    target.Add(new Text(math.Content.ToString()));
                    break;
                case LineBreakInline lineBreak:
                    target.Add(lineBreak.IsHard ? new Character { SymbolName = SymbolName.LineBreak } : new Text(" "));
                    break;
                case TaskList task:
                    target.Add(new Text(task.Checked ? "[x] " : "[ ] "));
                    break;
                case EmphasisInline emphasis:
                    var formatted = new FormattedText();
                    formatted.Bold = emphasis.DelimiterChar != '~' && emphasis.DelimiterCount >= 2;
                    formatted.Italic = emphasis.DelimiterChar != '~' && emphasis.DelimiterCount == 1;
                    target.Add(formatted);
                    AddInlines(formatted.Elements, emphasis);
                    break;
                case LinkInline { IsImage: true } image:
                    if (image.Url is { } imageUrl && (imageUrl.StartsWith("data:image/png;base64,", StringComparison.Ordinal)
                        || imageUrl.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal)))
                    {
                        // MigraDoc otherwise silently substitutes an error label for corrupt images.
                        using var imageStream = new MemoryStream(Convert.FromBase64String(imageUrl[(imageUrl.IndexOf(',') + 1)..]));
                        using var validatedImage = XImage.FromStream(imageStream);
                        var pdfImage = new MigraDoc.DocumentObjectModel.Shapes.Image("base64:" + imageUrl[(imageUrl.IndexOf(',') + 1)..]);
                        pdfImage.Width = Unit.FromCentimeter(10);
                        pdfImage.LockAspectRatio = true;
                        target.Add(pdfImage);
                    }
                    else
                    {
                        target.Add(new Text("[Image: "));
                        AddInlines(target, image);
                        target.Add(new Text("]"));
                    }

                    break;
                case LinkInline link:
                    if (Uri.TryCreate(link.Url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "mailto")
                    {
                        var hyperlink = new Hyperlink { Type = HyperlinkType.Web, Filename = uri.AbsoluteUri };
                        target.Add(hyperlink);
                        AddInlines(hyperlink.Elements, link);
                    }
                    else
                    {
                        AddInlines(target, link);
                    }

                    break;
                case AutolinkInline autoLink:
                    target.Add(new Text(autoLink.Url));
                    break;
                case ContainerInline nested:
                    AddInlines(target, nested);
                    break;
            }
        }
    }
}
