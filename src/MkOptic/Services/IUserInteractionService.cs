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

    void ShowError(string title, string message);

    void ShowInfo(string title, string message);
}
