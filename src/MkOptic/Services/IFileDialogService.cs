using MkOptic.Services.Export;

namespace MkOptic.Services;

/// <summary>
/// Native open/save dialogs for source documents and exported files.
/// </summary>
public interface IFileDialogService
{
    Task<IReadOnlyList<string>> PickFilesToOpenAsync();

    Task<string?> PickSaveLocationAsync(string suggestedFileName);

    Task<string?> PickExportLocationAsync(string suggestedFileName, DocumentExportFormat format);
}
