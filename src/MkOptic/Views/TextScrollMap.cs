namespace MkOptic.Views;

/// <summary>
/// A monotone, bidirectional map between Markdown source offsets and rendered vertical positions.
/// Interpolation is local to adjacent text anchors, never proportional to the whole document.
/// </summary>
internal sealed class TextScrollMap
{
    private readonly Anchor[] _anchors;

    public TextScrollMap(IEnumerable<Anchor> anchors)
    {
        var ordered = anchors.OrderBy(anchor => anchor.SourceOffset).ThenBy(anchor => anchor.PreviewY);
        var accepted = new List<Anchor>();
        foreach (var anchor in ordered)
        {
            // Table cells can share a row, and several inline runs can share a text line.
            // Keep the first anchor on that row so the inverse map remains unambiguous.
            if (accepted.Count == 0 ||
                (anchor.SourceOffset > accepted[^1].SourceOffset && anchor.PreviewY > accepted[^1].PreviewY))
            {
                accepted.Add(anchor);
            }
        }

        _anchors = [.. accepted];
    }

    public bool IsEmpty => _anchors.Length == 0;

    public double ToPreview(double sourceOffset) => Interpolate(sourceOffset, toPreview: true);

    public double ToSource(double previewY) => Interpolate(previewY, toPreview: false);

    private double Interpolate(double value, bool toPreview)
    {
        if (_anchors.Length == 0)
        {
            return 0;
        }

        var low = 0;
        var high = _anchors.Length - 1;
        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            var coordinate = toPreview ? _anchors[middle].SourceOffset : _anchors[middle].PreviewY;
            if (coordinate < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var right = _anchors[low];
        var left = _anchors[Math.Max(0, low - 1)];
        var start = toPreview ? left.SourceOffset : left.PreviewY;
        var end = toPreview ? right.SourceOffset : right.PreviewY;
        var fraction = end > start ? Math.Clamp((value - start) / (end - start), 0, 1) : 0;
        var targetStart = toPreview ? left.PreviewY : left.SourceOffset;
        var targetEnd = toPreview ? right.PreviewY : right.SourceOffset;
        return targetStart + ((targetEnd - targetStart) * fraction);
    }

    internal readonly record struct Anchor(double SourceOffset, double PreviewY);
}
