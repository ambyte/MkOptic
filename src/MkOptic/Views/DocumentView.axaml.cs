using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using CommunityToolkit.Mvvm.Input;
using LiveMarkdown.Avalonia;
using MkOptic.Core.Documents;
using MkOptic.Core.Editing;
using MkOptic.ViewModels;
using TextMateSharp.Grammars;

namespace MkOptic.Views;

/// <summary>
/// Source editor with Markdown highlighting side by side with the rendered preview.
/// Editor-specific behaviour (selection, highlighting, layout) lives here because it operates on controls.
/// </summary>
public sealed partial class DocumentView : UserControl
{
    private static readonly GridLength Star = new(1, GridUnitType.Star);
    private static readonly GridLength Collapsed = new(0);
    private static readonly GridLength SplitterWidth = new(10);

    private static readonly (KeyGesture Gesture, MarkdownFormat Format)[] Shortcuts =
    [
        (new KeyGesture(Key.B, KeyModifiers.Control), MarkdownFormat.Bold),
        (new KeyGesture(Key.I, KeyModifiers.Control), MarkdownFormat.Italic),
        (new KeyGesture(Key.K, KeyModifiers.Control), MarkdownFormat.Link),
        (new KeyGesture(Key.E, KeyModifiers.Control), MarkdownFormat.InlineCode),
        (new KeyGesture(Key.M, KeyModifiers.Control), MarkdownFormat.InlineMath),
        (new KeyGesture(Key.D1, KeyModifiers.Control | KeyModifiers.Alt), MarkdownFormat.Heading1),
        (new KeyGesture(Key.D2, KeyModifiers.Control | KeyModifiers.Alt), MarkdownFormat.Heading2),
        (new KeyGesture(Key.D3, KeyModifiers.Control | KeyModifiers.Alt), MarkdownFormat.Heading3),
    ];

    private readonly RegistryOptions _registryOptions = new(ThemeName.DarkPlus);
    private TextMate.Installation? _textMate;
    private DocumentViewModel? _viewModel;
    private ScrollViewer? _editorScrollViewer;
    private TextScrollMap? _scrollMap;
    private double? _synchronizedEditorOffset;
    private double? _synchronizedPreviewOffset;
    private string? _renderedPreviewText;
    private ITextSourceVersion? _synchronizedTextVersion;
    private Window? _window;

    public DocumentView()
    {
        FormatCommand = new RelayCommand<MarkdownFormat>(ApplyFormat, _ => CanFormat());
        InitializeComponent();

        SearchPanel.Install(Editor);
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextArea.GotFocus += OnSourceFocusChanged;
        Editor.TextArea.LostFocus += OnSourceFocusChanged;
        Editor.PropertyChanged += OnEditorPropertyChanged;
        RegisterShortcuts();

        Editor.TemplateApplied += OnEditorTemplateApplied;
        PreviewScrollViewer.ScrollChanged += OnPreviewScrolled;
        Preview.SizeChanged += (_, _) => _scrollMap = null;
        Preview.PropertyChanged += OnPreviewPropertyChanged;
    }

    public IRelayCommand<MarkdownFormat> FormatCommand { get; }

    private bool CanFormat() => _viewModel?.IsSourceFocused == true && !Editor.IsReadOnly;

    private void OnSourceFocusChanged(object? sender, RoutedEventArgs e) => UpdateSourceFocus();

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextEditor.IsReadOnlyProperty)
        {
            FormatCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.IsActiveProperty)
        {
            UpdateSourceFocus();
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty && _viewModel is not null)
        {
            UpdateSourceFocus();
        }
    }

    private void UpdateSourceFocus()
    {
        if (_viewModel is not null)
        {
            _viewModel.IsSourceFocused = IsEffectivelyVisible && EditorCard.IsVisible &&
                Editor.TextArea.IsFocused && _window?.IsActive == true;
        }

        FormatCommand.NotifyCanExecuteChanged();
    }

    private void OnEditorTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (_editorScrollViewer is not null)
        {
            _editorScrollViewer.ScrollChanged -= OnEditorScrolled;
        }

        if (e.NameScope.Find<ScrollViewer>("PART_ScrollViewer") is { } scrollViewer)
        {
            _editorScrollViewer = scrollViewer;
            scrollViewer.ScrollChanged += OnEditorScrolled;
        }
    }

    private void OnEditorScrolled(object? sender, ScrollChangedEventArgs e)
    {
        // Extent-only changes (content still rendering) must not move the other pane.
        if (e.OffsetDelta.Y != 0)
        {
            SyncScroll(fromEditor: true);
        }
    }

    private void OnPreviewScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (e.OffsetDelta.Y != 0)
        {
            SyncScroll(fromEditor: false);
        }
    }

    private void SyncScroll(bool fromEditor)
    {
        if (!IsEffectivelyVisible || _editorScrollViewer is null || _viewModel?.ViewMode != DocumentViewMode.Split)
        {
            return;
        }

        var source = fromEditor ? _editorScrollViewer : PreviewScrollViewer;
        var sourceY = fromEditor ? Editor.TextArea.TextView.ScrollOffset.Y : source.Offset.Y;
        var expected = fromEditor ? _synchronizedEditorOffset : _synchronizedPreviewOffset;
        if (fromEditor)
        {
            _synchronizedEditorOffset = null;
        }
        else
        {
            _synchronizedPreviewOffset = null;
        }

        // ScrollChanged can be delivered after assigning Offset, so a synchronous boolean guard
        // is insufficient. Ignore only the expected programmatic movement, not subsequent user input.
        if (expected is { } expectedY && Math.Abs(sourceY - expectedY) < 1)
        {
            return;
        }

        // The debounced preview must represent this document before its source spans can be used.
        if (Preview.DocumentUpdate is null || _renderedPreviewText != _viewModel.PreviewText)
        {
            return;
        }

        if (_synchronizedTextVersion is null || _synchronizedTextVersion.CompareAge(Editor.Document.Version) != 0)
        {
            if (_viewModel.PreviewText != Editor.Document.Text)
            {
                return;
            }

            _synchronizedTextVersion = Editor.Document.Version;
            _scrollMap = null;
        }

        _scrollMap ??= PreviewScrollMap.Create(Preview, _viewModel.PreviewText);
        if (_scrollMap.IsEmpty || Preview.TranslatePoint(default, PreviewScrollViewer) is not { } previewOrigin)
        {
            return;
        }

        var previewTop = previewOrigin.Y + PreviewScrollViewer.Offset.Y;
        var target = fromEditor ? PreviewScrollViewer : _editorScrollViewer;
        var offsetY = fromEditor
            ? _scrollMap.ToPreview(GetEditorSourcePosition(sourceY)) + previewTop
            : GetEditorOffset(_scrollMap.ToSource(sourceY - previewTop));
        if (sourceY <= 0)
        {
            offsetY = 0;
        }

        offsetY = Math.Clamp(offsetY, 0, Math.Max(0, target.Extent.Height - target.Viewport.Height));
        if (Math.Abs(offsetY - target.Offset.Y) < 0.5)
        {
            return;
        }

        if (fromEditor)
        {
            _synchronizedPreviewOffset = offsetY;
        }
        else
        {
            _synchronizedEditorOffset = offsetY;
        }

        if (fromEditor)
        {
            target.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(target.Offset.X, offsetY));
        }
        else
        {
            var textView = Editor.TextArea.TextView;
            ((ILogicalScrollable)textView).Offset = new Vector(textView.ScrollOffset.X, offsetY);
        }
    }

    private void OnPreviewPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == MarkdownRenderer.RenderedTextProjectionProperty)
        {
            _renderedPreviewText = Preview.MarkdownBuilder is { } builder &&
                Preview.RenderedTextProjection is { } projection && projection.SourceVersion == builder.Version
                ? builder.ToString()
                : null;
            _scrollMap = null;
        }
    }

    private double GetEditorSourcePosition(double y)
    {
        var textView = Editor.TextArea.TextView;
        var position = GetEditorPosition(y);

        var top = textView.GetVisualPosition(position, VisualYPosition.LineTop).Y;
        var bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom).Y;
        var start = Editor.Document.GetOffset(position.Location);
        var next = GetEditorPosition(bottom + 0.01);
        var end = Editor.Document.GetOffset(next.Location);
        if (end <= start)
        {
            end = Editor.Document.TextLength;
        }
        var fraction = bottom > top ? Math.Clamp((y - top) / (bottom - top), 0, 1) : 0;
        return start + ((end - start) * fraction);
    }

    private double GetEditorOffset(double sourceOffset)
    {
        var document = Editor.Document;
        sourceOffset = Math.Clamp(sourceOffset, 0, document.TextLength);
        var position = new TextViewPosition(document.GetLocation((int)sourceOffset));
        var textView = Editor.TextArea.TextView;
        var top = textView.GetVisualPosition(position, VisualYPosition.LineTop).Y;
        var bottom = textView.GetVisualPosition(position, VisualYPosition.LineBottom).Y;
        var first = GetEditorPosition(top + 0.01);
        var next = GetEditorPosition(bottom + 0.01);
        var start = document.GetOffset(first.Location);
        var end = document.GetOffset(next.Location);
        if (end <= start)
        {
            end = document.TextLength;
        }
        var fraction = end > start ? Math.Clamp((sourceOffset - start) / (end - start), 0, 1) : 0;
        return top + ((bottom - top) * fraction);
    }

    private TextViewPosition GetEditorPosition(double y)
    {
        var textView = Editor.TextArea.TextView;
        var line = textView.GetOrConstructVisualLine(textView.GetDocumentLineByVisualTop(y));
        return line.GetTextViewPosition(new Point(0, y), false);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null)
        {
            _window.PropertyChanged += OnWindowPropertyChanged;
        }
        UpdateSourceFocus();
        if (_textMate is null)
        {
            _textMate = Editor.InstallTextMate(_registryOptions);
            _textMate.SetGrammar(_registryOptions.GetScopeByLanguageId(_registryOptions.GetLanguageByExtension(".md").Id));
        }

        ApplyColorTheme();
        ActualThemeVariantChanged += OnActualThemeVariantChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null)
        {
            _window.PropertyChanged -= OnWindowPropertyChanged;
            _window = null;
        }
        UpdateSourceFocus();
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        _textMate?.Dispose();
        _textMate = null;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.IsSourceFocused = false;
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as DocumentViewModel;
        _scrollMap = null;
        _synchronizedEditorOffset = null;
        _synchronizedPreviewOffset = null;
        _synchronizedTextVersion = null;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyViewMode();
    }

    // Tunnel phase: the editor's own bindings (e.g. Ctrl+I = indent) would otherwise handle the keys first.
    private void RegisterShortcuts() => AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (!CanFormat())
        {
            return;
        }

        foreach (var (gesture, format) in Shortcuts)
        {
            if (gesture.Matches(e))
            {
                ApplyFormat(format);
                e.Handled = true;
                return;
            }
        }
    }

    private void ApplyFormat(MarkdownFormat format)
    {
        if (!CanFormat())
        {
            return;
        }

        var document = Editor.Document;
        var selection = new TextRange(Editor.SelectionStart, Editor.SelectionLength);
        var edit = MarkdownFormatter.Apply(format, document.Text, selection);

        document.Replace(edit.Offset, edit.Length, edit.Text);
        Editor.Select(edit.Selection.Start, edit.Selection.Length);
        Editor.TextArea.Caret.BringCaretToView();
        Editor.Focus();
    }

    private void ApplyViewMode()
    {
        var mode = _viewModel?.ViewMode ?? DocumentViewMode.Split;
        var columns = SplitGrid.ColumnDefinitions;

        columns[0].Width = mode == DocumentViewMode.Preview ? Collapsed : Star;
        columns[1].Width = mode == DocumentViewMode.Split ? SplitterWidth : Collapsed;
        columns[2].Width = mode == DocumentViewMode.Editor ? Collapsed : Star;

        EditorCard.IsVisible = mode != DocumentViewMode.Preview;
        Splitter.IsVisible = mode == DocumentViewMode.Split;
        PreviewCard.IsVisible = mode != DocumentViewMode.Editor;
        UpdateSourceFocus();
    }

    private void ApplyColorTheme()
    {
        var theme = ActualThemeVariant == ThemeVariant.Light ? ThemeName.LightPlus : ThemeName.DarkPlus;
        _textMate?.SetTheme(_registryOptions.LoadTheme(theme));
        Preview.CodeBlockColorTheme = theme;
    }

    private void OnActualThemeVariantChanged(object? sender, EventArgs e) => ApplyColorTheme();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentViewModel.ViewMode))
        {
            ApplyViewMode();
        }
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        var caret = Editor.TextArea.Caret;
        _viewModel.CaretLine = caret.Line;
        _viewModel.CaretColumn = caret.Column;
    }

    private async void OnPreviewLinkClick(object? sender, LinkClickedEventArgs e)
    {
        var link = LinkResolver.Resolve(e.HRef, _viewModel?.DirectoryPath);
        e.Handled = true;

        switch (link.Kind)
        {
            case LinkKind.External when TopLevel.GetTopLevel(this)?.Launcher is { } launcher:
                await launcher.LaunchUriAsync(link.ExternalUri!);
                break;
            case LinkKind.LocalFile:
                _viewModel?.RequestOpenLinkedFile(link.LocalPath!);
                break;
        }
    }
}
