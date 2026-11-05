namespace MkOptic.Core.Documents;

/// <summary>
/// Basic text metrics shown in the status bar.
/// </summary>
public readonly record struct DocumentStatistics(int Lines, int Words, int Characters)
{
    private const int WordsPerMinute = 200;

    public static DocumentStatistics Empty { get; } = new(1, 0, 0);

    /// <summary>Estimated reading time in whole minutes (at least one minute for non-empty text).</summary>
    public int ReadingMinutes => Words == 0 ? 0 : Math.Max(1, (int)Math.Round(Words / (double)WordsPerMinute));

    public static DocumentStatistics Calculate(ReadOnlySpan<char> text)
    {
        var lines = 1;
        var words = 0;
        var insideWord = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '\n' || (c == '\r' && (i + 1 >= text.Length || text[i + 1] != '\n')))
            {
                lines++;
            }

            var isWordChar = char.IsLetterOrDigit(c) || c == '\'' || c == '_';
            if (isWordChar && !insideWord)
            {
                words++;
            }

            insideWord = isWordChar;
        }

        return new DocumentStatistics(lines, words, text.Length);
    }
}
