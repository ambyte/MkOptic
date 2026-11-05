using System.ComponentModel;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
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
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromMilliseconds(400);

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
    private Vector? _pendingEditorOffset;
    private Vector? _pendingPreviewOffset;
    private int _restoreToken;
    private bool _restoreSettled;

    public DocumentView()
    {
        InitializeComponent();
        FormatCommand = new RelayCommand<MarkdownFormat>(ApplyFormat);

        SearchPanel.Install(Editor);
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        RegisterShortcuts();

        Editor.TemplateApplied += OnEditorTemplateApplied;
        PreviewScrollViewer.ScrollChanged += OnPreviewScrolled;
    }

    public ICommand FormatCommand { get; }

    private void OnEditorTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (e.NameScope.Find<ScrollViewer>("PART_ScrollViewer") is { } scrollViewer)
        {
            _editorScrollViewer = scrollViewer;
            scrollViewer.ScrollChanged += OnEditorScrolled;
        }
    }

    private void OnEditorScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (HasPendingOffsets)
        {
            TryApplyPendingOffsets();
            return;
        }

        // Extent-only changes (content still rendering) must not move the other pane.
        if (e.OffsetDelta.Y != 0)
        {
            SyncScroll(_editorScrollViewer, PreviewScrollViewer);
        }
    }

    private void OnPreviewScrolled(object? sender, ScrollChangedEventArgs e)
    {
        if (HasPendingOffsets)
        {
            TryApplyPendingOffsets();
            return;
        }

        if (e.OffsetDelta.Y != 0)
        {
            SyncScroll(PreviewScrollViewer, _editorScrollViewer);
        }
    }

    private bool HasPendingOffsets => _pendingEditorOffset is not null || _pendingPreviewOffset is not null;

    // Mirrors the scroll position proportionally in split mode. The offset comparison stops feedback loops.
    private void SyncScroll(ScrollViewer? source, ScrollViewer? target)
    {
        if (source is null || target is null || _viewModel?.ViewMode != DocumentViewMode.Split)
        {
            return;
        }

        var sourceRange = source.Extent.Height - source.Viewport.Height;
        var targetRange = target.Extent.Height - target.Viewport.Height;
        if (sourceRange <= 0 || targetRange <= 0)
        {
            return;
        }

        var offsetY = Math.Clamp(source.Offset.Y / sourceRange, 0, 1) * targetRange;
        if (Math.Abs(offsetY - target.Offset.Y) >= 1)
        {
            target.Offset = new Vector(target.Offset.X, offsetY);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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
        ActualThemeVariantChanged -= OnActualThemeVariantChanged;
        _textMate?.Dispose();
        _textMate = null;
        base.OnDetachedFromVisualTree(e);
    }

    // Runs before bindings switch to the new document, so the outgoing offsets are still intact.
    protected override void OnDataContextBeginUpdate()
    {
        // Mid-restore offsets are not the user's position, so the previously saved ones are kept.
        if (_viewModel is not null && !HasPendingOffsets)
        {
            _viewModel.EditorScrollOffset = _editorScrollViewer?.Offset ?? default;
            _viewModel.PreviewScrollOffset = PreviewScrollViewer.Offset;
        }

        base.OnDataContextBeginUpdate();
    }

    private void RestoreScrollOffsets()
    {
        if (_viewModel is null)
        {
            return;
        }

        // Extents are only known after layout (the preview renders even later), so the offsets are applied
        // as the content grows. The content stays hidden meanwhile to avoid a visible jump from the top.
        _pendingEditorOffset = _viewModel.EditorScrollOffset;
        _pendingPreviewOffset = _viewModel.PreviewScrollOffset;
        _restoreToken++;
        _restoreSettled = false;
        SplitGrid.Opacity = 0;
        TryApplyPendingOffsets();

        var token = _restoreToken;
        Dispatcher.UIThread.Post(() => SettleRestore(token), DispatcherPriority.Background);
        DispatcherTimer.RunOnce(() => FinishRestore(token), RestoreTimeout);
    }

    private void TryApplyPendingOffsets()
    {
        if (_pendingEditorOffset is { } editorOffset && _editorScrollViewer is not null)
        {
            _editorScrollViewer.Offset = editorOffset;
            if (_restoreSettled && Math.Abs(_editorScrollViewer.Offset.Y - editorOffset.Y) < 1)
            {
                _pendingEditorOffset = null;
            }
        }

        if (_pendingPreviewOffset is { } previewOffset)
        {
            PreviewScrollViewer.Offset = previewOffset;
            if (_restoreSettled && Math.Abs(PreviewScrollViewer.Offset.Y - previewOffset.Y) < 1)
            {
                _pendingPreviewOffset = null;
            }
        }

        if (!HasPendingOffsets)
        {
            SplitGrid.Opacity = 1;
        }
    }

    // Offsets reached before the new document has been bound and laid out are not final (the editor resets
    // its scroll when the document changes), so they only count as restored once this pass has run.
    private void SettleRestore(int token)
    {
        if (token == _restoreToken)
        {
            _restoreSettled = true;
            TryApplyPendingOffsets();
        }
    }

    // Gives up on offsets that never became reachable (e.g. the document shrank) and reveals the content.
    private void FinishRestore(int token)
    {
        if (token != _restoreToken)
        {
            return;
        }

        _restoreSettled = true;
        TryApplyPendingOffsets();
        _pendingEditorOffset = null;
        _pendingPreviewOffset = null;
        SplitGrid.Opacity = 1;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as DocumentViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyViewMode();
        RestoreScrollOffsets();
    }

    // Tunnel phase: the editor's own bindings (e.g. Ctrl+I = indent) would otherwise handle the keys first.
    private void RegisterShortcuts() => AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);

    private void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
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
        if (_viewModel is null || Editor.IsReadOnly)
        {
            return;
        }

        if (_viewModel.ViewMode == DocumentViewMode.Preview)
        {
            _viewModel.ViewMode = DocumentViewMode.Split;
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
