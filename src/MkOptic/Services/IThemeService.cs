using SukiUI.Models;

namespace MkOptic.Services;

/// <summary>
/// Light/dark switching and accent colours of the application.
/// </summary>
public interface IThemeService
{
    event EventHandler? AppearanceChanged;

    bool IsDark { get; }

    string AccentColorName { get; }

    IReadOnlyList<SukiColorTheme> ColorThemes { get; }

    /// <summary>Applies the configured startup theme and accent colour.</summary>
    void ApplyConfiguredTheme();

    void ToggleBaseTheme();

    void ChangeColorTheme(SukiColorTheme theme);
}
