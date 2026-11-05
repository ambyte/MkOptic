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
