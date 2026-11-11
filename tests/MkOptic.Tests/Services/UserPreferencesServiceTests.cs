using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Core.Options;
using MkOptic.Services;
using MkOptic.Tests.ViewModels;
using MkOptic.ViewModels;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MkOptic.Tests.Services;

public sealed class UserPreferencesServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"mkoptic-preferences-{Guid.NewGuid():N}");

    private string PreferencesPath => Path.Combine(_directory, "profile", "preferences.json");

    [Fact]
    public async Task MissingProfile_KeepsConfiguredDefaults_AndDoesNotWriteOnStartup()
    {
        var (viewModel, theme) = CreateViewModel(wordWrap: false, lineNumbers: false, autoReload: true);
        using var service = CreateService();

        service.ApplyAndTrack(viewModel, theme);
        await service.FlushAsync();

        Assert.True(viewModel.IsSidebarOpen);
        Assert.False(viewModel.EditorSettings.WordWrap);
        Assert.False(viewModel.EditorSettings.ShowLineNumbers);
        Assert.True(viewModel.EditorSettings.AutoReloadUnmodifiedFiles);
        Assert.False(File.Exists(PreferencesPath));
    }

    [Fact]
    public async Task ChangedPreferences_AreSavedAndRestoredOnNextLaunch()
    {
        var (first, firstTheme) = CreateViewModel();
        using (var service = CreateService())
        {
            service.ApplyAndTrack(first, firstTheme);
            first.ToggleSidebarCommand.Execute(null);
            first.EditorSettings.WordWrap = false;
            first.EditorSettings.ShowLineNumbers = false;
            first.EditorSettings.AutoReloadUnmodifiedFiles = true;
            first.ToggleThemeCommand.Execute(null);
            first.ChangeColorThemeCommand.Execute(firstTheme.ColorThemes[1]);
            await service.FlushAsync();
        }

        var saved = JsonSerializer.Deserialize<UserPreferences>(await File.ReadAllTextAsync(PreferencesPath, TestContext.Current.CancellationToken))!;
        Assert.False(saved.IsSidebarOpen);
        Assert.False(saved.WordWrap);
        Assert.False(saved.ShowLineNumbers);
        Assert.True(saved.AutoReloadUnmodifiedFiles);
        Assert.Equal("Light", saved.Appearance!.BaseTheme);
        Assert.Equal("Orange", saved.Appearance.AccentColor);

        var (second, secondTheme) = CreateViewModel();
        using var restored = CreateService();
        restored.ApplyAndTrack(second, secondTheme);

        Assert.False(second.IsSidebarOpen);
        Assert.False(second.EditorSettings.WordWrap);
        Assert.False(second.EditorSettings.ShowLineNumbers);
        Assert.True(second.EditorSettings.AutoReloadUnmodifiedFiles);
        Assert.False(secondTheme.IsDark);
        Assert.Equal("Orange", secondTheme.AccentColorName);
        Assert.False(second.IsDarkTheme);
    }

    [Theory]
    [InlineData("WordWrap")]
    [InlineData("ShowLineNumbers")]
    [InlineData("AutoReloadUnmodifiedFiles")]
    [InlineData("IsSidebarOpen")]
    [InlineData("BaseTheme")]
    [InlineData("AccentColor")]
    public async Task EachPreferenceChange_SavesWithoutWaitingForAnotherSetting(string preference)
    {
        var (viewModel, theme) = CreateViewModel();
        using var service = CreateService();
        service.ApplyAndTrack(viewModel, theme);
        switch (preference)
        {
            case "WordWrap": viewModel.EditorSettings.WordWrap = false; break;
            case "ShowLineNumbers": viewModel.EditorSettings.ShowLineNumbers = false; break;
            case "AutoReloadUnmodifiedFiles": viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true; break;
            case "IsSidebarOpen": viewModel.IsSidebarOpen = false; break;
            case "BaseTheme": viewModel.ToggleThemeCommand.Execute(null); break;
            case "AccentColor": viewModel.ChangeColorThemeCommand.Execute(theme.ColorThemes[1]); break;
        }

        await service.FlushAsync();

        Assert.True(File.Exists(PreferencesPath));
        var (restored, restoredTheme) = CreateViewModel();
        using var next = CreateService();
        next.ApplyAndTrack(restored, restoredTheme);
        Assert.Equal(viewModel.IsSidebarOpen, restored.IsSidebarOpen);
        Assert.Equal(viewModel.EditorSettings.WordWrap, restored.EditorSettings.WordWrap);
        Assert.Equal(viewModel.EditorSettings.ShowLineNumbers, restored.EditorSettings.ShowLineNumbers);
        Assert.Equal(viewModel.EditorSettings.AutoReloadUnmodifiedFiles, restored.EditorSettings.AutoReloadUnmodifiedFiles);
        Assert.Equal(theme.IsDark, restoredTheme.IsDark);
        Assert.Equal(theme.AccentColorName, restoredTheme.AccentColorName);
    }

    [Fact]
    public async Task PartialProfile_RestoresSavedValuesAndKeepsOtherDefaults()
    {
        await WriteProfileAsync("""{"WordWrap":false,"Appearance":{"BaseTheme":"Light"}}""");
        var (viewModel, theme) = CreateViewModel(lineNumbers: false, autoReload: true);
        using var service = CreateService();

        service.ApplyAndTrack(viewModel, theme);

        Assert.False(viewModel.EditorSettings.WordWrap);
        Assert.False(viewModel.EditorSettings.ShowLineNumbers);
        Assert.True(viewModel.EditorSettings.AutoReloadUnmodifiedFiles);
        Assert.True(viewModel.IsSidebarOpen);
        Assert.False(theme.IsDark);
        Assert.Equal("Blue", theme.AccentColorName);
    }

    [Theory]
    [InlineData("not valid json")]
    [InlineData("null")]
    [InlineData("{\"WordWrap\":\"not-a-boolean\"}")]
    [InlineData("{\"Appearance\":{\"BaseTheme\":\"Unknown\",\"AccentColor\":\"Unknown\"}}")]
    public async Task InvalidProfile_FallsBackToDefaultsWithoutThrowing(string json)
    {
        await WriteProfileAsync(json);
        var (viewModel, theme) = CreateViewModel();
        using var service = CreateService();

        service.ApplyAndTrack(viewModel, theme);

        Assert.True(viewModel.EditorSettings.WordWrap);
        Assert.True(viewModel.EditorSettings.ShowLineNumbers);
        Assert.False(viewModel.EditorSettings.AutoReloadUnmodifiedFiles);
        Assert.True(theme.IsDark);
        Assert.Equal("Blue", theme.AccentColorName);
    }

    [Fact]
    public async Task RapidChanges_SaveLatestSnapshotAndLeaveNoTemporaryFiles()
    {
        var (viewModel, theme) = CreateViewModel();
        using var service = CreateService();
        service.ApplyAndTrack(viewModel, theme);
        for (var index = 0; index < 30; index++)
        {
            viewModel.EditorSettings.WordWrap = !viewModel.EditorSettings.WordWrap;
            viewModel.EditorSettings.AutoReloadUnmodifiedFiles = !viewModel.EditorSettings.AutoReloadUnmodifiedFiles;
            viewModel.ToggleThemeCommand.Execute(null);
        }

        viewModel.EditorSettings.WordWrap = false;
        viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        await service.FlushAsync();

        var saved = JsonSerializer.Deserialize<UserPreferences>(await File.ReadAllTextAsync(PreferencesPath, TestContext.Current.CancellationToken))!;
        Assert.False(saved.WordWrap);
        Assert.True(saved.AutoReloadUnmodifiedFiles);
        Assert.Equal(theme.IsDark ? "Dark" : "Light", saved.Appearance!.BaseTheme);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(PreferencesPath)!));
    }

    [Fact]
    public async Task SaveFailure_DoesNotThrowAndNextChangeCanRetry()
    {
        var (viewModel, theme) = CreateViewModel();
        using var service = CreateService();
        service.ApplyAndTrack(viewModel, theme);
        Directory.CreateDirectory(_directory);
        var parent = Path.GetDirectoryName(PreferencesPath)!;
        await File.WriteAllTextAsync(parent, "blocks creating profile directory", TestContext.Current.CancellationToken);

        viewModel.EditorSettings.WordWrap = false;
        await service.FlushAsync();
        Assert.False(File.Exists(PreferencesPath));

        File.Delete(parent);
        viewModel.EditorSettings.ShowLineNumbers = false;
        await service.FlushAsync();
        Assert.True(File.Exists(PreferencesPath));
    }

    [Fact]
    public async Task Dispose_UnsubscribesFromPreferenceChanges()
    {
        var (viewModel, theme) = CreateViewModel();
        var service = CreateService();
        service.ApplyAndTrack(viewModel, theme);
        service.Dispose();

        viewModel.EditorSettings.WordWrap = false;
        viewModel.ToggleThemeCommand.Execute(null);
        viewModel.IsSidebarOpen = false;
        await service.FlushAsync();

        Assert.False(File.Exists(PreferencesPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private UserPreferencesService CreateService() => new(PreferencesPath, NullLogger<UserPreferencesService>.Instance);

    private async Task WriteProfileAsync(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
        await File.WriteAllTextAsync(PreferencesPath, json, TestContext.Current.CancellationToken);
    }

    private static (MainWindowViewModel ViewModel, FakeThemeService Theme) CreateViewModel(
        bool wordWrap = true, bool lineNumbers = true, bool autoReload = false)
    {
        var options = MsOptions.Create(new EditorOptions
        {
            WordWrap = wordWrap, ShowLineNumbers = lineNumbers, AutoReloadUnmodifiedFiles = autoReload,
            FileExtensions = [".md"],
        });
        var settings = new EditorSettingsViewModel(options);
        var theme = new FakeThemeService();
        var viewModel = new MainWindowViewModel(new InMemoryFileService(), new FakeFileDialogService(),
            new FakeUserInteractionService(), theme, new DocumentViewModelFactory(options, settings, TimeProvider.System),
            settings, options, NullLogger<MainWindowViewModel>.Instance, new FakeDocumentExportService());
        return (viewModel, theme);
    }
}
