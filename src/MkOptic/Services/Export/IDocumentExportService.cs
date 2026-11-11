namespace MkOptic.Services.Export;

/// <summary>Exports a snapshot without changing the source document or its saved state.</summary>
public interface IDocumentExportService
{
    Task ExportAsync(string markdown, string title, string? sourcePath, string destinationPath,
        DocumentExportFormat format, CancellationToken cancellationToken = default);
}
