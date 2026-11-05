namespace MkOptic.Services;

/// <summary>
/// Native open/save dialogs restricted to Markdown files.
/// </summary>
public interface IFileDialogService
{
    Task<IReadOnlyList<string>> PickFilesToOpenAsync();

    Task<string?> PickSaveLocationAsync(string suggestedFileName);
}
