using System.ComponentModel;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MkOptic.Options;
using MkOptic.ViewModels;

namespace MkOptic.Services;

/// <summary>Restores per-user preferences and serializes changes through an ordered, atomic save queue.</summary>
public sealed partial class UserPreferencesService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger<UserPreferencesService> _logger;
    private readonly UserPreferences _preferences;
    private MainWindowViewModel? _viewModel;
    private IThemeService? _theme;
    private Task _pendingSave = Task.CompletedTask;

    public UserPreferencesService(string path, ILogger<UserPreferencesService> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _logger = logger;
        _preferences = Load();
    }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MkOptic", "preferences.json");

    /// <summary>Call once on the UI thread, after the configured theme is applied and before showing the window.</summary>
    public void ApplyAndTrack(MainWindowViewModel viewModel, IThemeService theme)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(theme);
        StopTracking();

        var settings = viewModel.EditorSettings;
        viewModel.IsSidebarOpen = _preferences.IsSidebarOpen ?? viewModel.IsSidebarOpen;
        settings.WordWrap = _preferences.WordWrap ?? settings.WordWrap;
        settings.ShowLineNumbers = _preferences.ShowLineNumbers ?? settings.ShowLineNumbers;
        settings.AutoReloadUnmodifiedFiles = _preferences.AutoReloadUnmodifiedFiles ?? settings.AutoReloadUnmodifiedFiles;

        if (Enum.TryParse<BaseTheme>(_preferences.Appearance?.BaseTheme, ignoreCase: true, out var baseTheme)
            && Enum.IsDefined(baseTheme) && theme.IsDark != (baseTheme == BaseTheme.Dark))
        {
            theme.ToggleBaseTheme();
        }

        var accent = theme.ColorThemes.FirstOrDefault(color =>
            string.Equals(color.DisplayName, _preferences.Appearance?.AccentColor, StringComparison.OrdinalIgnoreCase));
        if (accent is not null)
        {
            theme.ChangeColorTheme(accent);
        }

        _viewModel = viewModel;
        _theme = theme;
        viewModel.PropertyChanged += OnWindowPreferenceChanged;
        settings.PropertyChanged += OnEditorPreferenceChanged;
        theme.AppearanceChanged += OnAppearanceChanged;
    }

    /// <summary>Waits for all queued preference writes. Call on the UI thread before exiting.</summary>
    public async Task FlushAsync()
    {
        Task pending;
        do
        {
            pending = _pendingSave;
            await pending;
        }
        while (!ReferenceEquals(pending, _pendingSave));
    }

    public void Dispose() => StopTracking();

    private UserPreferences Load()
    {
        try
        {
            // Like .NET configuration providers, load this small startup-only file before constructing the window.
            return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(_path), JsonOptions) ?? new();
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new();
        }
        catch (Exception ex) when (IsStorageException(ex) || ex is JsonException)
        {
            LogLoadFailed(_logger, ex);
            return new();
        }
    }

    private void OnWindowPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsSidebarOpen))
        {
            QueueSave();
        }
    }

    private void OnEditorPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorSettingsViewModel.WordWrap)
            or nameof(EditorSettingsViewModel.ShowLineNumbers)
            or nameof(EditorSettingsViewModel.AutoReloadUnmodifiedFiles))
        {
            QueueSave();
        }
    }

    private void OnAppearanceChanged(object? sender, EventArgs e) => QueueSave();

    private void QueueSave()
    {
        if (_viewModel is not { } viewModel || _theme is not { } theme)
        {
            return;
        }

        // Capture on the UI thread; background I/O must not read mutable view-model state.
        var snapshot = new UserPreferences
        {
            IsSidebarOpen = viewModel.IsSidebarOpen,
            WordWrap = viewModel.EditorSettings.WordWrap,
            ShowLineNumbers = viewModel.EditorSettings.ShowLineNumbers,
            AutoReloadUnmodifiedFiles = viewModel.EditorSettings.AutoReloadUnmodifiedFiles,
            Appearance = new UserAppearancePreferences
            {
                BaseTheme = theme.IsDark ? nameof(BaseTheme.Dark) : nameof(BaseTheme.Light),
                AccentColor = theme.AccentColorName,
            },
        };
        _pendingSave = SaveAfterAsync(_pendingSave, snapshot);
    }

    private async Task SaveAfterAsync(Task previous, UserPreferences snapshot)
    {
        await previous.ConfigureAwait(false);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions)).ConfigureAwait(false);
            File.Move(temporaryPath, _path, overwrite: true);
        }
        catch (Exception ex) when (IsStorageException(ex))
        {
            LogSaveFailed(_logger, ex);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (Exception ex) when (IsStorageException(ex))
            {
                LogCleanupFailed(_logger, ex);
            }
        }
    }

    private void StopTracking()
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnWindowPreferenceChanged;
            _viewModel.EditorSettings.PropertyChanged -= OnEditorPreferenceChanged;
        }

        if (_theme is not null)
        {
            _theme.AppearanceChanged -= OnAppearanceChanged;
        }

        _viewModel = null;
        _theme = null;
    }

    private static bool IsStorageException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or System.Security.SecurityException;

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to load user preferences; using application defaults")]
    private static partial void LogLoadFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to save user preferences")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to remove a temporary preferences file")]
    private static partial void LogCleanupFailed(ILogger logger, Exception exception);
}
