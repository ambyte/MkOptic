namespace MkOptic.Core.Editing;

/// <summary>
/// A contiguous range of text expressed as a start offset and a length.
/// </summary>
public readonly record struct TextRange(int Start, int Length)
{
    public int End => Start + Length;
}

/// <summary>
/// Replaces <see cref="Length"/> characters at <see cref="Offset"/> with <see cref="Text"/>;
/// afterwards the editor should select <see cref="Selection"/> (in coordinates of the edited text).
/// </summary>
public sealed record TextEdit(int Offset, int Length, string Text, TextRange Selection);
