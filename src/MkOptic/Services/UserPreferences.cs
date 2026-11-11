namespace MkOptic.Services;

/// <summary>Only user-adjustable preferences are persisted; missing values retain application defaults.</summary>
public sealed record UserPreferences
{
    public bool? IsSidebarOpen { get; init; }

    public bool? WordWrap { get; init; }

    public bool? ShowLineNumbers { get; init; }

    public bool? AutoReloadUnmodifiedFiles { get; init; }

    public UserAppearancePreferences? Appearance { get; init; }
}

public sealed record UserAppearancePreferences
{
    public string? BaseTheme { get; init; }

    public string? AccentColor { get; init; }
}
