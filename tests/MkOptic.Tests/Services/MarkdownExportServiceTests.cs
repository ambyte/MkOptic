using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Services.Export;
using PdfSharp.Pdf.IO;
using SkiaSharp;

namespace MkOptic.Tests.Services;

public sealed class MarkdownExportServiceTests : IDisposable
{
    private static readonly string PixelPng = CreatePixelPng();
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mkoptic-export-{Guid.NewGuid():N}");
    private readonly MarkdownExportService _service = new(NullLogger<MarkdownExportService>.Instance);

    public MarkdownExportServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public void ExportFonts_AreTrueTypeFonts()
    {
        var resolver = new ExportFontResolver();
        var regular = resolver.GetFont("Inter-Regular");
        Assert.True(regular.Length > 1000);
        Assert.Equal(new byte[] { 0, 1, 0, 0 }, regular[..4]);
    }

    [Fact]
    public async Task Html_ExportsStyledUtf8DocumentWithRichMarkdown()
    {
        var path = Path.Combine(_directory, "result.html");
        await _service.ExportAsync("# Привет\n\n**Bold** and *italic*\n\n- [x] Task\n\n| A | B |\n|---|---|\n| 1 | 2 |\n\n```csharp\nvar x = 1;\n```",
            "Title <&>", null, path, DocumentExportFormat.Html, TestContext.Current.CancellationToken);

        var html = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.Contains("<meta charset=\"utf-8\">", html, StringComparison.Ordinal);
        Assert.Contains("<title>Title &lt;&amp;&gt;</title>", html, StringComparison.Ordinal);
        Assert.Contains("Привет", html, StringComparison.Ordinal);
        Assert.Contains("<strong>Bold</strong>", html, StringComparison.Ordinal);
        Assert.Contains("<em>italic</em>", html, StringComparison.Ordinal);
        Assert.Contains("<table>", html, StringComparison.Ordinal);
        Assert.Contains("checked", html, StringComparison.Ordinal);
        Assert.Contains("language-csharp", html, StringComparison.Ordinal);
        Assert.Contains("<style>", html, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Html_DoesNotIncludeExecutableMarkdownHtmlOrUnsafeLinks()
    {
        var path = Path.Combine(_directory, "safe.html");
        await _service.ExportAsync("<script>alert(1)</script>\n\n[unsafe](javascript:alert)\n\n[web](https://example.com)",
            "<script>title</script>", null, path, DocumentExportFormat.Html, TestContext.Current.CancellationToken);

        var html = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("href=\"javascript:", html, StringComparison.Ordinal);
        Assert.Contains("https://example.com", html, StringComparison.Ordinal);
        Assert.Contains("Content-Security-Policy", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Html_EmbedsLocalImagesAndResolvesRelativeLinksAgainstSource()
    {
        await File.WriteAllBytesAsync(Path.Combine(_directory, "pixel.png"), Convert.FromBase64String(PixelPng), TestContext.Current.CancellationToken);
        var path = Path.Combine(_directory, "output", "images.html");
        await _service.ExportAsync("![Pixel](pixel.png)\n\n[Other](other.md)", "Images",
            Path.Combine(_directory, "source.md"), path, DocumentExportFormat.Html, TestContext.Current.CancellationToken);

        var html = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Contains("data:image/png;base64," + PixelPng, html, StringComparison.Ordinal);
        Assert.Contains(new Uri(Path.Combine(_directory, "other.md")).AbsoluteUri, html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("# Заголовок\n\nПривет, мир! **Жирный** и *курсив*.\n\n- item\n- [x] done\n\n> quote\n\n---\n\n| A | B |\n|---|---|\n| x | y |\n\n```\ncode\n```\n\n[link](https://example.com)\n\n$x^2$")]
    public async Task Pdf_IsReadableAndHasUnicodeTitleAndEmbeddedFonts(string markdown)
    {
        var path = Path.Combine(_directory, "result.pdf");
        await _service.ExportAsync(markdown, "Документ", null, path, DocumentExportFormat.Pdf, TestContext.Current.CancellationToken);

        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes.AsSpan(0, 8)), StringComparison.Ordinal);
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.Equal("Документ", document.Info.Title);
        Assert.True(document.PageCount > 0);
        Assert.Contains("/FontFile2", Encoding.Latin1.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pdf_LongDocumentIsPaginated()
    {
        var path = Path.Combine(_directory, "long.pdf");
        var markdown = string.Join("\n\n", Enumerable.Repeat("A long paragraph with readable text and кириллица.", 200));
        await _service.ExportAsync(markdown, "Long", null, path, DocumentExportFormat.Pdf, TestContext.Current.CancellationToken);

        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        Assert.True(document.PageCount > 1);
    }

    [Fact]
    public async Task Pdf_EmbedsLocalPng()
    {
        await File.WriteAllBytesAsync(Path.Combine(_directory, "pixel.png"), Convert.FromBase64String(PixelPng), TestContext.Current.CancellationToken);
        var path = Path.Combine(_directory, "image.pdf");
        await _service.ExportAsync("![Pixel](pixel.png)", "Image", Path.Combine(_directory, "source.md"),
            path, DocumentExportFormat.Pdf, TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        Assert.Matches(@"/Subtype\s*/Image\b", Encoding.Latin1.GetString(bytes));
    }

    [Fact]
    public async Task Export_RejectsSourceOverwrite()
    {
        var path = Path.Combine(_directory, "source.md");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<IOException>(() => _service.ExportAsync("replacement", "Title", path, path,
            DocumentExportFormat.Html, TestContext.Current.CancellationToken));
        Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Export_MissingImageDoesNotReplaceExistingDestination()
    {
        var path = Path.Combine(_directory, "result.html");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<FileNotFoundException>(() => _service.ExportAsync("![Missing](missing.png)", "Title",
            Path.Combine(_directory, "source.md"), path, DocumentExportFormat.Html, TestContext.Current.CancellationToken));
        Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Export_Cancelled_DoesNotReplaceExistingDestination()
    {
        var path = Path.Combine(_directory, "result.html");
        await File.WriteAllTextAsync(path, "original", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ExportAsync("# New", "Title", null,
            path, DocumentExportFormat.Html, cancellation.Token));
        Assert.Equal("original", await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    private static string CreatePixelPng()
    {
        using var bitmap = new SKBitmap(2, 2);
        bitmap.Erase(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToBase64String(data.ToArray());
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
