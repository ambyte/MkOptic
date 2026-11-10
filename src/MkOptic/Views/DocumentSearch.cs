using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveMarkdown.Avalonia;
using MkOptic.Core.Documents;

namespace MkOptic.Views;

/// <summary>
/// View-owned find state. Source ranges and rendered block-local ranges deliberately stay separate:
/// Markdown syntax and link destinations are not part of the preview's visible text.
/// </summary>
public sealed partial class DocumentSearch : ObservableObject
{
    private const string MatchHighlight = "MkOptic.Find";
    private const string CurrentHighlight = "MkOptic.Find.Current";
    private static readonly IBrush MatchBrush = new SolidColorBrush(Color.Parse("#70DCA32C"));
    private static readonly IBrush CurrentBrush = new SolidColorBrush(Color.Parse("#B0EAA32C"));
    private readonly TextEditor _editor;
    private readonly MarkdownRenderer _preview;
    private readonly SourceHighlights _sourceHighlights;
    private List<TextSegment> _sourceMatches = [];
    private IReadOnlyList<TextHighlightMatch> _previewMatches = [];
    private TextHighlightMatch? _currentPreviewMatch;
    private DocumentViewMode _mode = DocumentViewMode.Split;
    private bool _refreshing;
    private int _index = -1;

    [ObservableProperty] private string _query = string.Empty;

    public DocumentSearch(TextEditor editor, MarkdownRenderer preview)
    {
        _editor = editor;
        _preview = preview;
        _sourceHighlights = new SourceHighlights(this);
        editor.TextArea.TextView.BackgroundRenderers.Add(_sourceHighlights);
        editor.TextChanged += (_, _) => Refresh(select: false);
        preview.PropertyChanged += OnPreviewChanged;
        var styles = new TextHighlightStyles();
        styles.Set(MatchHighlight, new TextHighlightStyle { Background = MatchBrush });
        styles.Set(CurrentHighlight, new TextHighlightStyle { Background = CurrentBrush });
        MarkdownTextBlock.SetHighlightStyles(preview, styles);
        NextCommand = new RelayCommand(() => Navigate(1), () => MatchCount > 0);
        PreviousCommand = new RelayCommand(() => Navigate(-1), () => MatchCount > 0);
        ClearCommand = new RelayCommand(() => Query = string.Empty, () => HasQuery);
    }

    public IRelayCommand NextCommand { get; }
    public IRelayCommand PreviousCommand { get; }
    public IRelayCommand ClearCommand { get; }
    public bool IsPreview { get; private set; }
    public int MatchCount => IsPreview ? _previewMatches.Count : _sourceMatches.Count;
    public int CurrentIndex => _index;
    public string TargetLabel => IsPreview ? "Search rendered preview" : "Search Markdown source";
    public bool HasQuery => Query.Length > 0;
    public string ResultLabel => !HasQuery ? string.Empty : $"{_index + 1} / {MatchCount}";

    partial void OnQueryChanged(string value) => Refresh(select: true);

    public void SetMode(DocumentViewMode mode)
    {
        _mode = mode;
        SetTarget(mode == DocumentViewMode.Preview || (mode == DocumentViewMode.Split && IsPreview));
    }

    public void SetTarget(bool preview)
    {
        preview = _mode == DocumentViewMode.Preview || (_mode == DocumentViewMode.Split && preview);
        if (IsPreview == preview)
        {
            return;
        }

        RemoveCurrentHighlight();
        IsPreview = preview;
        _index = MatchCount > 0 ? 0 : -1;
        OnPropertyChanged(nameof(IsPreview));
        OnPropertyChanged(nameof(TargetLabel));
        UpdateResults();
    }

    public void SeedSelection()
    {
        if (!IsPreview && _editor.SelectionLength > 0 && !_editor.SelectedText.Contains('\n'))
        {
            Query = _editor.SelectedText;
        }
    }

    public void FocusContent()
    {
        if (IsPreview)
        {
            _preview.Focus();
        }
        else
        {
            _editor.TextArea.Focus();
        }
    }

    public void Navigate(int direction)
    {
        if (MatchCount == 0)
        {
            return;
        }

        _index = (_index + direction + MatchCount) % MatchCount;
        SelectCurrent();
        UpdateResults();
    }

    private void Refresh(bool select)
    {
        if (_refreshing)
        {
            return;
        }

        _refreshing = true;
        try
        {
            RemoveCurrentHighlight();
            _sourceMatches = [];
            _previewMatches = [];
            if (Query.Length == 0)
            {
                _preview.ClearTextSearch();
            }
            else
            {
                // One immutable literal pattern keeps source and preview matching identical:
                // ordinal case-insensitive substrings, with no regex or whole-word mode.
                var pattern = new TextSearchPattern(Query, TextSearchOptions.None);
                _sourceMatches = pattern.FindRanges(_editor.Text)
                    .Select(range => new TextSegment { StartOffset = range.Start, Length = range.Length }).ToList();
                _previewMatches = _preview.ApplyTextSearch(pattern, MatchHighlight);
            }
        }
        finally
        {
            _refreshing = false;
        }

        _index = MatchCount > 0 ? 0 : -1;
        if (select)
        {
            SelectCurrent();
        }

        _editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
        UpdateResults();
    }

    private void OnPreviewChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != MarkdownRenderer.TextSearchMatchesProperty || _refreshing)
        {
            return;
        }

        RemoveCurrentHighlight();
        _previewMatches = _preview.TextSearchMatches;
        if (IsPreview)
        {
            _index = MatchCount > 0 ? Math.Clamp(_index, 0, MatchCount - 1) : -1;
            SelectCurrent();
            UpdateResults();
        }
    }

    private void SelectCurrent()
    {
        RemoveCurrentHighlight();
        if (_index < 0 || _index >= MatchCount)
        {
            return;
        }

        if (IsPreview)
        {
            var match = _previewMatches[_index];
            _currentPreviewMatch = match;
            match.Block.Highlights.Set(CurrentHighlight, [match.Range], priority: 1);
            var rectangles = match.Block.GetTextRangeBoundsInControl(match.Range.Start, match.Range.Length);
            if (rectangles.Count > 0 &&
                _preview.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is { } scroll &&
                match.Block.TranslatePoint(rectangles[0].TopLeft, scroll) is { } origin)
            {
                var rectangle = rectangles[0];
                if (origin.Y < 0 || origin.Y + rectangle.Height > scroll.Viewport.Height)
                {
                    var offset = scroll.Offset.Y + origin.Y - ((scroll.Viewport.Height - rectangle.Height) / 2);
                    scroll.SetCurrentValue(ScrollViewer.OffsetProperty,
                        new Vector(scroll.Offset.X, Math.Clamp(offset, 0, Math.Max(0, scroll.Extent.Height - scroll.Viewport.Height))));
                }
            }
        }
        else
        {
            var match = _sourceMatches[_index];
            _editor.Select(match.StartOffset, match.Length);
            _editor.TextArea.Caret.BringCaretToView();
        }
    }

    private void RemoveCurrentHighlight()
    {
        _currentPreviewMatch?.Block.Highlights.Remove(CurrentHighlight);
        _currentPreviewMatch = null;
    }

    private void UpdateResults()
    {
        OnPropertyChanged(nameof(MatchCount));
        OnPropertyChanged(nameof(CurrentIndex));
        OnPropertyChanged(nameof(ResultLabel));
        OnPropertyChanged(nameof(HasQuery));
        NextCommand.NotifyCanExecuteChanged();
        PreviousCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    private sealed class SourceHighlights(DocumentSearch search) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Selection;

        public void Draw(TextView textView, DrawingContext drawingContext)
        {
            if (!textView.VisualLinesValid || textView.VisualLines.Count == 0)
            {
                return;
            }

            var first = textView.VisualLines[0].FirstDocumentLine.Offset;
            var last = textView.VisualLines[^1].LastDocumentLine.EndOffset;
            foreach (var match in search._sourceMatches)
            {
                if (match.EndOffset < first)
                {
                    continue;
                }
                if (match.StartOffset > last)
                {
                    break;
                }
                foreach (var rectangle in BackgroundGeometryBuilder.GetRectsForSegment(textView, match))
                {
                    drawingContext.DrawRectangle(MatchBrush, null, rectangle, 2, 2);
                }
            }
        }
    }
}
