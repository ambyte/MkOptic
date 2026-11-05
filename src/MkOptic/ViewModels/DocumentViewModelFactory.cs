using Microsoft.Extensions.Options;
using MkOptic.Core.Options;

namespace MkOptic.ViewModels;

/// <summary>
/// Creates documents with shared settings and sequential names for untitled documents.
/// </summary>
public sealed class DocumentViewModelFactory(
    IOptions<EditorOptions> options,
    EditorSettingsViewModel settings,
    TimeProvider timeProvider)
{
    private readonly EditorOptions _options = options.Value;
    private int _untitledCounter;

    public DocumentViewModel Create(string content = "", string? filePath = null)
    {
        var untitledName = filePath is null ? $"Untitled {++_untitledCounter}" : Path.GetFileNameWithoutExtension(filePath);
        return new DocumentViewModel(
            content,
            filePath,
            untitledName,
            _options.DefaultViewMode,
            settings,
            TimeSpan.FromMilliseconds(_options.PreviewDelayMilliseconds),
            timeProvider);
    }
}
