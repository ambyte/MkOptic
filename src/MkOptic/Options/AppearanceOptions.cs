using SukiUI.Enums;

namespace MkOptic.Options;

public enum BaseTheme
{
    Dark,
    Light,
}

/// <summary>
/// Startup look of the application, bound from the <c>Appearance</c> configuration section.
/// </summary>
public sealed class AppearanceOptions
{
    public const string SectionName = "Appearance";

    public BaseTheme BaseTheme { get; set; } = BaseTheme.Dark;

    public SukiColor AccentColor { get; set; } = SukiColor.Blue;
}
