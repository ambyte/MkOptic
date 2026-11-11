using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Logging;

namespace MkOptic.Services.Export;

public sealed partial class MarkdownExportService(ILogger<MarkdownExportService> logger) : IDocumentExportService
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions().DisableHtml().Build();

    public async Task ExportAsync(string markdown, string title, string? sourcePath, string destinationPath,
        DocumentExportFormat format, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        var fullPath = Path.GetFullPath(destinationPath);
        if (sourcePath is not null && string.Equals(Path.GetFullPath(sourcePath), fullPath,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new IOException("Export cannot overwrite the source Markdown file.");
        }

        var document = await Task.Run(() => Markdown.Parse(markdown, Pipeline), cancellationToken).ConfigureAwait(false);
        await PrepareLinksAsync(document, sourcePath, cancellationToken).ConfigureAwait(false);
        var bytes = await Task.Run(() => format switch
        {
            DocumentExportFormat.Html => Encoding.UTF8.GetBytes(HtmlDocumentRenderer.Render(document, title, Pipeline)),
            DocumentExportFormat.Pdf => PdfDocumentRenderer.Render(document, title),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        }, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogCleanupFailed(logger, ex, Path.GetFileName(temporaryPath));
            }
        }

        var fileName = Path.GetFileName(fullPath);
        LogExported(logger, fileName, format);
    }

    private static async Task PrepareLinksAsync(MarkdownDocument document, string? sourcePath, CancellationToken cancellationToken)
    {
        var baseUri = sourcePath is null ? null : new Uri(Path.GetFullPath(sourcePath));
        foreach (var link in document.Descendants<LinkInline>())
        {
            var url = link.GetDynamicUrl?.Invoke() ?? link.Url;
            link.GetDynamicUrl = null;
            if (string.IsNullOrWhiteSpace(url) || url.StartsWith('#'))
            {
                continue;
            }

            Uri? uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)
                && (baseUri is null || !Uri.TryCreate(baseUri, url, out uri)))
            {
                link.Url = string.Empty;
                continue;
            }

            if (link.IsImage && uri.IsFile)
            {
                var mimeType = Path.GetExtension(uri.LocalPath).ToUpperInvariant() switch
                {
                    ".PNG" => "image/png",
                    ".JPG" or ".JPEG" => "image/jpeg",
                    ".GIF" => "image/gif",
                    ".SVG" => "image/svg+xml",
                    ".WEBP" => "image/webp",
                    _ => null,
                };
                // Never read arbitrary linked files (or network shares) into a shareable export.
                if (mimeType is null || uri.IsUnc)
                {
                    link.Url = string.Empty;
                    continue;
                }

                const int maxImageBytes = 20 * 1024 * 1024;
                await using var stream = new FileStream(uri.LocalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                    4096, FileOptions.Asynchronous);
                if (stream.Length > maxImageBytes)
                {
                    throw new IOException("An export image exceeds the 20 MB limit.");
                }

                using var content = new MemoryStream();
                await stream.CopyToAsync(content, cancellationToken).ConfigureAwait(false);
                link.Url = $"data:{mimeType};base64,{Convert.ToBase64String(content.ToArray())}";
            }
            else
            {
                link.Url = uri.Scheme is "http" or "https" or "mailto" || (!link.IsImage && uri.IsFile && !uri.IsUnc)
                    ? uri.AbsoluteUri
                    : string.Empty;
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Exported file {FileName} as {Format}")]
    private static partial void LogExported(ILogger logger, string fileName, DocumentExportFormat format);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove temporary export {FileName}")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception, string fileName);
}
