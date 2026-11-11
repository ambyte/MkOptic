using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.Options;
using MkOptic.Core.Options;
using MkOptic.Services.Export;

namespace MkOptic.Services;

public sealed class StorageProviderFileDialogService(IOptions<EditorOptions> options) : IFileDialogService
{
    private readonly FilePickerFileType _markdownFileType = new("Markdown")
    {
        Patterns = [.. options.Value.FileExtensions.Select(extension => $"*{extension}")],
        MimeTypes = ["text/markdown"],
    };

    public async Task<IReadOnlyList<string>> PickFilesToOpenAsync()
    {
        var files = await GetStorageProvider().OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Markdown files",
            AllowMultiple = true,
            FileTypeFilter = [_markdownFileType, FilePickerFileTypes.All],
        });

        return [.. files.Select(file => file.TryGetLocalPath()).OfType<string>()];
    }

    public async Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        var file = await GetStorageProvider().SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save as",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = options.Value.FileExtensions[0],
            FileTypeChoices = [_markdownFileType],
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    public async Task<string?> PickExportLocationAsync(string suggestedFileName, DocumentExportFormat format)
    {
        var (name, extension, mimeType) = format switch
        {
            DocumentExportFormat.Html => ("HTML", "html", "text/html"),
            DocumentExportFormat.Pdf => ("PDF", "pdf", "application/pdf"),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        var file = await GetStorageProvider().SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = $"Export to {name}",
            SuggestedFileName = Path.ChangeExtension(suggestedFileName, extension),
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(name) { Patterns = [$"*.{extension}"], MimeTypes = [mimeType] }],
            ShowOverwritePrompt = true,
        });

        return file?.TryGetLocalPath();
    }

    private static IStorageProvider GetStorageProvider()
    {
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        return TopLevel.GetTopLevel(window)?.StorageProvider
            ?? throw new InvalidOperationException("The main window is not available.");
    }
}
