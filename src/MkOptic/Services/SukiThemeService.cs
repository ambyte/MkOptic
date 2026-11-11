using Avalonia.Styling;
using Microsoft.Extensions.Options;
using MkOptic.Options;
using SukiUI;
using SukiUI.Models;

namespace MkOptic.Services;

public sealed class SukiThemeService(IOptions<AppearanceOptions> options) : IThemeService
{
    public event EventHandler? AppearanceChanged;

    public string AccentColorName { get; private set; } = options.Value.AccentColor.ToString();

    private static SukiTheme Theme => SukiTheme.GetInstance();

    public bool IsDark => Theme.ActiveBaseTheme == ThemeVariant.Dark;

    public IReadOnlyList<SukiColorTheme> ColorThemes => [.. Theme.ColorThemes];

    public void ApplyConfiguredTheme()
    {
        var appearance = options.Value;
        Theme.ChangeBaseTheme(appearance.BaseTheme == BaseTheme.Light ? ThemeVariant.Light : ThemeVariant.Dark);
        Theme.ChangeColorTheme(appearance.AccentColor);
        AccentColorName = appearance.AccentColor.ToString();
    }

    public void ToggleBaseTheme()
    {
        Theme.SwitchBaseTheme();
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ChangeColorTheme(SukiColorTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        Theme.ChangeColorTheme(theme);
        AccentColorName = theme.DisplayName;
        AppearanceChanged?.Invoke(this, EventArgs.Empty);
    }
}
