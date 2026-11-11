namespace MkOptic.Services;

public enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// In-window dialogs and notifications.
/// </summary>
public interface IUserInteractionService
{
    Task<UnsavedChangesDecision> AskToSaveChangesAsync(string documentTitle);

    /// <returns>True to reload, false to save a copy, or null when a dialog cannot be shown.</returns>
    Task<bool?> AskToReloadFileAsync(string documentTitle, bool hasLocalChanges);

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);
}
