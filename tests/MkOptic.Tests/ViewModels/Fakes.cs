using MkOptic.Core.Files;
using MkOptic.Services;
using SukiUI.Models;

namespace MkOptic.Tests.ViewModels;

internal sealed class InMemoryFileService : IMarkdownFileService
{
    public Dictionary<string, string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? ReadFailure { get; set; }

    public Func<string, Task<string>>? ReadHandler { get; set; }

    public Task<string> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (ReadHandler is not null)
        {
            return ReadHandler(path);
        }

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

    public bool ReloadDecision { get; set; }

    public List<(string Title, bool HasLocalChanges)> ReloadQuestions { get; } = [];

    public Func<Task<bool?>>? ReloadHandler { get; set; }

    public Task<bool?> AskToReloadFileAsync(string documentTitle, bool hasLocalChanges)
    {
        ReloadQuestions.Add((documentTitle, hasLocalChanges));
        return ReloadHandler?.Invoke() ?? Task.FromResult<bool?>(ReloadDecision);
    }

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
    public event EventHandler? AppearanceChanged;

    public string AccentColorName { get; private set; } = "Blue";

    public bool IsDark { get; private set; } = true;

    public IReadOnlyList<SukiColorTheme> ColorThemes { get; } =
        [new("Blue", Avalonia.Media.Colors.Blue, Avalonia.Media.Colors.DarkBlue),
         new("Orange", Avalonia.Media.Colors.Orange, Avalonia.Media.Colors.DarkOrange)];

    public void ApplyConfiguredTheme()
    {
    }

    public void ToggleBaseTheme()
    {
        IsDark = !IsDark;
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeColorTheme(SukiColorTheme theme)
    {
        AccentColorName = theme.DisplayName;
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }
}
