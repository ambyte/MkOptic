namespace MkOptic.Core.Files;

/// <summary>
/// Thrown when a file exceeds the configured size limit.
/// </summary>
public sealed class FileTooLargeException : IOException
{
    public FileTooLargeException()
    {
    }

    public FileTooLargeException(string message)
        : base(message)
    {
    }

    public FileTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FileTooLargeException(string path, long actualBytes, long maxBytes)
        : base($"File '{Path.GetFileName(path)}' is {actualBytes} bytes, which exceeds the limit of {maxBytes} bytes.")
    {
        ActualBytes = actualBytes;
        MaxBytes = maxBytes;
    }

    public long ActualBytes { get; }

    public long MaxBytes { get; }
}
