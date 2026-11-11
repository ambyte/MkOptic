using Avalonia.Platform;
using Avalonia.Fonts.Inter;
using PdfSharp.Fonts;

namespace MkOptic.Services.Export;

/// <summary>Reuses the shipped Inter fonts, including Cyrillic, instead of relying on OS fonts.</summary>
internal sealed class ExportFontResolver : IFontResolver
{
    private static readonly StandardAssetLoader Assets = new(typeof(InterFontCollection).Assembly);

    private static readonly Lazy<bool> Initialized = new(() =>
    {
        GlobalFontSettings.FontResolver = new ExportFontResolver();
        return true;
    });

    public static void EnsureInitialized() => _ = Initialized.Value;

    public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "Inter-Bold" : "Inter-Regular", mustSimulateBold: false, mustSimulateItalic: italic);

    public byte[] GetFont(string faceName)
    {
        using var stream = Assets.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/{faceName}.ttf"));
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
