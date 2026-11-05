using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MkOptic.Core.Options;

namespace MkOptic.Core.Files;

public sealed partial class MarkdownFileService(IOptions<EditorOptions> options, ILogger<MarkdownFileService> logger)
    : IMarkdownFileService
{
    private const int BufferSize = 64 * 1024;

    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly EditorOptions _options = options.Value;

    public async Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = NormalizePath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("File not found.", fullPath);
        }

        if (info.Length > _options.MaxFileSizeBytes)
        {
            throw new FileTooLargeException(fullPath, info.Length, _options.MaxFileSizeBytes);
        }

        await using var stream = new FileStream(
            fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, useAsync: true);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        LogFileRead(logger, info.Name, info.Length);
        return content;
    }

    public async Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fullPath = NormalizePath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("The path has no parent directory.", nameof(path));
        Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            await using (var writer = new StreamWriter(stream, Utf8WithoutBom))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        var fileName = Path.GetFileName(fullPath);
        LogFileWritten(logger, fileName, content.Length);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetFullPath(path);
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogTempFileCleanupFailed(logger, ex, Path.GetFileName(path));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Read file {FileName} ({Bytes} bytes)")]
    private static partial void LogFileRead(ILogger logger, string fileName, long bytes);

    [LoggerMessage(Level = LogLevel.Information, Message = "Saved file {FileName} ({Characters} characters)")]
    private static partial void LogFileWritten(ILogger logger, string fileName, int characters);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to delete temporary file {FileName}")]
    private static partial void LogTempFileCleanupFailed(ILogger logger, Exception exception, string fileName);
}
