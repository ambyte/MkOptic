using MkOptic.Core.Files;
using MkOptic.Services;
using SukiUI.Models;

namespace MkOptic.Tests.ViewModels;

internal sealed class InMemoryFileService : IMarkdownFileService
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? ReadFailure { get; set; }

    public Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (ReadFailure is not null)
        {
            return Task.FromException<string>(ReadFailure);
        }

        return Files.TryGetValue(Path.GetFullPath(path), out var content)
            ? Task.FromResult(content)
            : Task.FromException<string>(new FileNotFoundException("Not found", path));
    }

    public Task WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        Files[Path.GetFullPath(path)] = content;
        return Task.CompletedTask;
    }
}

internal sealed class FakeFileDialogService : IFileDialogService
{
    public IReadOnlyList<string> FilesToOpen { get; set; } = [];

    public string? SaveLocation { get; set; }

    public int SaveDialogCalls { get; private set; }

    public Task<IReadOnlyList<string>> PickFilesToOpenAsync() => Task.FromResult(FilesToOpen);

    public Task<string?> PickSaveLocationAsync(string suggestedFileName)
    {
        SaveDialogCalls++;
        return Task.FromResult(SaveLocation);
    }
}

internal sealed class FakeUserInteractionService : IUserInteractionService
{
    public UnsavedChangesDecision Decision { get; set; } = UnsavedChangesDecision.Cancel;

    public List<string> AskedDocuments { get; } = [];

    public List<string> Errors { get; } = [];

    public List<string> Infos { get; } = [];

    public Task<UnsavedChangesDecision> AskToSaveChangesAsync(string documentTitle)
    {
        AskedDocuments.Add(documentTitle);
        return Task.FromResult(Decision);
    }

    public void ShowError(string title, string message) => Errors.Add(message);

    public void ShowInfo(string title, string message) => Infos.Add(message);
}

internal sealed class FakeThemeService : IThemeService
{
    public bool IsDark { get; private set; } = true;

    public IReadOnlyList<SukiColorTheme> ColorThemes => [];

    public void ApplyConfiguredTheme()
    {
    }

    public void ToggleBaseTheme() => IsDark = !IsDark;

    public void ChangeColorTheme(SukiColorTheme theme)
    {
    }
}
