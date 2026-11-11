using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using MkOptic.Core.Options;

namespace MkOptic.ViewModels;

/// <summary>
/// Editor appearance shared by all open documents.
/// </summary>
public sealed partial class EditorSettingsViewModel : ObservableObject
{
    private const double MinFontSize = 8;
    private const double MaxFontSize = 48;

    private readonly double _defaultFontSize;

    public EditorSettingsViewModel(IOptions<EditorOptions> options)
    {
        var value = options.Value;
        _defaultFontSize = value.FontSize;
        FontSize = value.FontSize;
        FontFamily = new FontFamily(value.FontFamily);
        WordWrap = value.WordWrap;
        ShowLineNumbers = value.ShowLineNumbers;
        AutoReloadUnmodifiedFiles = value.AutoReloadUnmodifiedFiles;
    }

    public FontFamily FontFamily { get; }

    [ObservableProperty]
    public partial double FontSize { get; set; }

    [ObservableProperty]
    public partial bool WordWrap { get; set; }

    [ObservableProperty]
    public partial bool ShowLineNumbers { get; set; }

    [ObservableProperty]
    public partial bool AutoReloadUnmodifiedFiles { get; set; }

    [RelayCommand]
    private void ZoomIn() => FontSize = Math.Min(MaxFontSize, FontSize + 1);

    [RelayCommand]
    private void ZoomOut() => FontSize = Math.Max(MinFontSize, FontSize - 1);

    [RelayCommand]
    private void ResetZoom() => FontSize = _defaultFontSize;
}
