namespace MkOptic.Core.Files;

/// <summary>
/// Reads and writes Markdown documents on disk.
/// </summary>
public interface IMarkdownFileService
{
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="FileTooLargeException">The file exceeds the configured size limit.</exception>
    /// <exception cref="IOException">The file could not be read.</exception>
    /// <exception cref="UnauthorizedAccessException">Access to the file is denied.</exception>
    Task<string> ReadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the content atomically: data goes to a temporary file that then replaces the target.
    /// </summary>
    Task WriteAsync(string path, string content, CancellationToken cancellationToken = default);
}
