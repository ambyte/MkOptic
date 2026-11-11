using Avalonia.Controls.Notifications;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace MkOptic.Services;

public sealed class SukiUserInteractionService(ISukiDialogManager dialogManager, ISukiToastManager toastManager)
    : IUserInteractionService
{
    private static readonly TimeSpan ErrorToastDuration = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan InfoToastDuration = TimeSpan.FromSeconds(3);

    public async Task<UnsavedChangesDecision> AskToSaveChangesAsync(string documentTitle)
    {
        var decision = new TaskCompletionSource<UnsavedChangesDecision>();

        var shown = dialogManager.CreateDialog()
            .OfType(NotificationType.Warning)
            .WithTitle("Unsaved changes")
            .WithContent($"Save changes to \"{documentTitle}\" before closing?")
            .WithActionButton("Save", _ => decision.TrySetResult(UnsavedChangesDecision.Save), true, "Flat", "Accent")
            .WithActionButton("Don't save", _ => decision.TrySetResult(UnsavedChangesDecision.Discard), true, "Outlined")
            .WithActionButton("Cancel", _ => decision.TrySetResult(UnsavedChangesDecision.Cancel), true, "Basic")
            .OnDismissed(_ => decision.TrySetResult(UnsavedChangesDecision.Cancel))
            .TryShow();

        return shown ? await decision.Task : UnsavedChangesDecision.Cancel;
    }

    public async Task<bool?> AskToReloadFileAsync(string documentTitle, bool hasLocalChanges)
    {
        var decision = new TaskCompletionSource<bool>();
        var warning = hasLocalChanges ? " Reloading will discard your unsaved changes." : string.Empty;
        var shown = dialogManager.CreateDialog()
            .OfType(NotificationType.Warning)
            .WithTitle("File changed outside MkOptic")
            .WithContent($"\"{documentTitle}\" has changed on disk. Reload it?{warning} If you keep your version, save it under a different name.")
            .WithActionButton("Reload", _ => decision.TrySetResult(true), true, "Flat", "Accent")
            .WithActionButton("Keep mine / Save As", _ => decision.TrySetResult(false), true, "Outlined")
            .OnDismissed(_ => decision.TrySetResult(false))
            .TryShow();

        // Another modal dialog may already be open. Do not interpret failure to show as a decision.
        return shown ? await decision.Task : null;
    }

    public void ShowError(string title, string message) =>
        ShowToast(NotificationType.Error, title, message, ErrorToastDuration);

    public void ShowInfo(string title, string message) =>
        ShowToast(NotificationType.Information, title, message, InfoToastDuration);

    private void ShowToast(NotificationType type, string title, string message, TimeSpan duration) =>
        toastManager.CreateToast()
            .OfType(type)
            .WithTitle(title)
            .WithContent(message)
            .Dismiss().After(duration)
            .Dismiss().ByClicking()
            .Queue();
}
