using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using LiveMarkdown.Avalonia;
using MkOptic.Core.Documents;
using MkOptic.Core.Editing;
using MkOptic.Core.Options;
using MkOptic.ViewModels;
using MkOptic.Views;

namespace MkOptic.Tests.Preview;

[Collection("Preview UI")]
public sealed class DocumentHostTests(PreviewTestSession fixture)
{
    [Theory(Timeout = 30000)]
    [InlineData(DocumentViewMode.Split)]
    [InlineData(DocumentViewMode.Preview)]
    public async Task Zoom_ScalesRenderedTypographyAndResetsWithoutChangingContent(DocumentViewMode mode)
    {
        await fixture.Session.Dispatch(async () =>
        {
            const string source = "# Heading\n\nParagraph.\n\n```text\ncode sample\n```\n\n> Quote.\n\n- Item\n";
            var settings = new EditorSettingsViewModel(Microsoft.Extensions.Options.Options.Create(new EditorOptions { FontSize = 18 }));
            using var document = new DocumentViewModel(source, null, "Zoom", mode, settings, TimeSpan.Zero, TimeProvider.System);
            var view = new DocumentView { DataContext = document };
            var window = new Window { Width = 1000, Height = 700, Content = view };
            window.Show();
            try
            {
                await SettleAsync(window);
                var preview = view.FindControl<MarkdownRenderer>("Preview")!;
                var editor = view.FindControl<TextEditor>("Editor")!;
                var blocks = preview.GetVisualDescendants().OfType<MarkdownTextBlock>().ToArray();
                var heading = Assert.Single(blocks, block => block.LayoutText == "Heading");
                var paragraph = Assert.Single(blocks, block => block.LayoutText == "Paragraph.");
                var code = Assert.Single(blocks, block => block.LayoutText.Contains("code sample", StringComparison.Ordinal));
                var committed = preview.DocumentUpdate;
                var toolbarFontSize = view.FindControl<StackPanel>("FormattingToolbar")!.GetValue(TextBlock.FontSizeProperty);

                AssertSizes(18);
                settings.ZoomInCommand.Execute(null);
                await FlushAsync(window);
                AssertSizes(19);
                settings.ZoomOutCommand.Execute(null);
                await FlushAsync(window);
                AssertSizes(18);
                settings.FontSize = 28;
                await FlushAsync(window);
                AssertSizes(28);
                settings.ResetZoomCommand.Execute(null);
                await FlushAsync(window);
                AssertSizes(18);

                Assert.Same(committed, preview.DocumentUpdate);
                Assert.Equal(source, document.GetText());
                Assert.False(document.IsDirty);
                Assert.Equal(toolbarFontSize, view.FindControl<StackPanel>("FormattingToolbar")!.GetValue(TextBlock.FontSizeProperty));

                void AssertSizes(double fontSize)
                {
                    Assert.Equal(fontSize, editor.FontSize);
                    Assert.Equal(15 * fontSize / 14, paragraph.FontSize, precision: 6);
                    Assert.Equal(30 * fontSize / 14, heading.FontSize, precision: 6);
                    Assert.Equal(paragraph.FontSize, code.FontSize);
                    Assert.Equal(11 * fontSize / 14, Assert.IsType<double>(preview.Resources["MarkdownScriptFontSize"]), precision: 6);
                    Assert.Equal(18 * fontSize / 14, Assert.IsType<double>(preview.Resources["MarkdownMathFontSize"]), precision: 6);
                }
            }
            finally
            {
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = 30000)]
    [InlineData(DocumentViewMode.Editor)]
    [InlineData(DocumentViewMode.Split)]
    [InlineData(DocumentViewMode.Preview)]
    public async Task Switching_RetainsViewsScrollSelectionAndCommittedPreview(DocumentViewMode mode)
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var first = CreateDocument("First", mode, longDocument: true);
            using var second = CreateDocument("Second", mode, longDocument: false);
            using var unvisited = CreateDocument("Unvisited", mode, longDocument: true);
            var host = new DocumentHost
            {
                Documents = new ObservableCollection<DocumentViewModel> { first, second, unvisited },
                ActiveDocument = first,
            };
            var window = new Window { Width = 1000, Height = 500, Content = host };
            window.Show();
            try
            {
                await SettleAsync(window);
                var firstView = Assert.IsType<DocumentView>(Assert.Single(host.Children));
                var editor = firstView.FindControl<TextEditor>("Editor")!;
                var preview = firstView.FindControl<MarkdownRenderer>("Preview")!;
                var previewScroll = firstView.FindControl<ScrollViewer>("PreviewScrollViewer")!;
                var editorScroll = editor.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();
                editor.Select(120, 15);
                if (mode != DocumentViewMode.Preview)
                {
                    Assert.NotNull(editorScroll);
                    editorScroll.Offset = new Vector(0, Math.Max(0, editorScroll.Extent.Height - editorScroll.Viewport.Height - 30));
                }

                if (mode == DocumentViewMode.Preview)
                {
                    previewScroll.Offset = new Vector(0, Math.Max(0, previewScroll.Extent.Height - previewScroll.Viewport.Height - 30));
                }

                await FlushAsync(window);
                var editorOffset = editorScroll?.Offset ?? default;
                var previewOffset = previewScroll.Offset;
                var selectionStart = editor.SelectionStart;
                var selectionLength = editor.SelectionLength;
                var caret = editor.CaretOffset;
                var committedPreview = preview.DocumentUpdate;
                var builder = preview.MarkdownBuilder;
                Assert.NotNull(committedPreview);
                Assert.True(mode == DocumentViewMode.Preview ? previewOffset.Y > 0 : editorOffset.Y > 0);
                var detachments = 0;
                firstView.DetachedFromVisualTree += (_, _) => detachments++;

                host.ActiveDocument = second;
                await SettleAsync(window);
                Assert.False(firstView.IsVisible);
                Assert.Same(first, firstView.DataContext);
                Assert.Equal(2, host.Children.Count);

                for (var index = 0; index < 5; index++)
                {
                    host.ActiveDocument = first;
                    await FlushAsync(window);
                    host.ActiveDocument = second;
                    await FlushAsync(window);
                }

                // Also exercise changes coalesced into a single layout pass.
                for (var index = 0; index < 10; index++)
                {
                    host.ActiveDocument = first;
                    host.ActiveDocument = second;
                }

                host.ActiveDocument = first;
                await FlushAsync(window);
                Assert.Same(firstView, Assert.Single(host.Children.OfType<DocumentView>(), view => view.IsVisible));
                Assert.Equal(0, detachments);
                Assert.Same(builder, preview.MarkdownBuilder);
                Assert.Same(committedPreview, preview.DocumentUpdate);
                Assert.InRange(Math.Abs((editorScroll?.Offset.Y ?? 0) - editorOffset.Y), 0, 1);
                Assert.InRange(Math.Abs(previewScroll.Offset.Y - previewOffset.Y), 0, 1);
                Assert.Equal(selectionStart, editor.SelectionStart);
                Assert.Equal(selectionLength, editor.SelectionLength);
                Assert.Equal(caret, editor.CaretOffset);
                Assert.Equal(1, firstView.FindControl<Grid>("SplitGrid")!.Opacity);
                Assert.DoesNotContain(host.Children, view => ReferenceEquals(view.DataContext, unvisited));
            }
            finally
            {
                window.Close();
            }

            Assert.Empty(host.Children);
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task RemovingAndResettingDocuments_ReleasesViewsAndIgnoresClosedActiveDocument()
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var first = CreateDocument("First", DocumentViewMode.Split, longDocument: false);
            using var second = CreateDocument("Second", DocumentViewMode.Editor, longDocument: false);
            var documents = new ObservableCollection<DocumentViewModel> { first, second };
            var host = new DocumentHost { Documents = documents, ActiveDocument = first };
            var window = new Window { Width = 1000, Height = 500, Content = host };
            window.Show();
            try
            {
                await SettleAsync(window);
                var firstView = Assert.IsType<DocumentView>(Assert.Single(host.Children));
                host.ActiveDocument = second;
                await SettleAsync(window);
                var secondView = Assert.Single(host.Children.OfType<DocumentView>(), view => view.IsVisible);
                documents.Remove(first);
                Assert.Null(firstView.DataContext);
                Assert.Null(firstView.Parent);
                Assert.Same(secondView, Assert.Single(host.Children));

                documents.Clear();
                Assert.Empty(host.Children);
                Assert.Null(secondView.DataContext);
                Assert.Null(secondView.Parent);
                // The model updates ActiveDocument after removing it from the collection.
                host.ActiveDocument = first;
                Assert.Empty(host.Children);

                host.Documents = new ObservableCollection<DocumentViewModel> { first };
                Assert.Single(host.Children);
                documents.Add(second);
                Assert.Single(host.Children);
            }
            finally
            {
                window.Close();
            }

            Assert.Empty(host.Children);
            host.Documents!.Add(second);
            host.ActiveDocument = second;
            Assert.Empty(host.Children);
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task Switching_KeepsEachDocumentsViewModeControlsIndependent()
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var first = CreateDocument("First", DocumentViewMode.Split, longDocument: false);
            using var second = CreateDocument("Second", DocumentViewMode.Preview, longDocument: false);
            var host = new DocumentHost
            {
                Documents = new ObservableCollection<DocumentViewModel> { first, second },
                ActiveDocument = first,
            };
            var window = new Window { Width = 1000, Height = 500, Content = host };
            window.Show();
            try
            {
                await SettleAsync(window);
                var firstView = Assert.IsType<DocumentView>(Assert.Single(host.Children));
                host.ActiveDocument = second;
                await SettleAsync(window);
                var secondView = Assert.Single(host.Children.OfType<DocumentView>(), view => view.IsVisible);
                Assert.True(firstView.GetVisualDescendants().OfType<RadioButton>().ElementAt(1).IsChecked);
                Assert.True(secondView.GetVisualDescendants().OfType<RadioButton>().ElementAt(2).IsChecked);

                secondView.GetVisualDescendants().OfType<RadioButton>().First().IsChecked = true;
                await FlushAsync(window);
                Assert.Equal(DocumentViewMode.Editor, second.ViewMode);
                Assert.Equal(DocumentViewMode.Split, first.ViewMode);
                host.ActiveDocument = first;
                await FlushAsync(window);
                Assert.True(firstView.GetVisualDescendants().OfType<RadioButton>().ElementAt(1).IsChecked);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = 30000)]
    [InlineData(DocumentViewMode.Editor)]
    [InlineData(DocumentViewMode.Split)]
    public async Task Formatting_RequiresSourceInputFocusAndToolbarClicksPreserveIt(DocumentViewMode mode)
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var document = CreateDocument("Focus", mode, longDocument: false);
            var view = new DocumentView { DataContext = document };
            var otherInput = new TextBox();
            var panel = new DockPanel();
            DockPanel.SetDock(otherInput, Dock.Bottom);
            panel.Children.Add(otherInput);
            panel.Children.Add(view);
            var window = new Window { Width = 1000, Height = 500, Content = panel };
            window.Show();
            try
            {
                await SettleAsync(window);
                var editor = view.FindControl<TextEditor>("Editor")!;
                var toolbar = view.FindControl<StackPanel>("FormattingToolbar")!;
                var buttons = toolbar.Children.OfType<Button>().ToArray();
                otherInput.Focus();
                Assert.False(document.IsSourceFocused);
                Assert.False(view.FormatCommand.CanExecute(MarkdownFormat.Bold));
                Assert.All(buttons, button => Assert.False(button.IsEffectivelyEnabled));
                var original = document.GetText();
                view.FormatCommand.Execute(MarkdownFormat.Bold);
                Assert.Equal(original, document.GetText());

                editor.TextArea.Focus();
                Assert.True(document.IsSourceFocused);
                Assert.All(buttons, button => Assert.True(button.IsEffectivelyEnabled));
                editor.Select(2, 5);
                var bold = Assert.Single(buttons, button => Equals(button.CommandParameter, MarkdownFormat.Bold));
                Assert.False(bold.Focusable);
                var point = bold.TranslatePoint(new Point(bold.Bounds.Width / 2, bold.Bounds.Height / 2), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                Assert.Contains("**Focus**", document.GetText(), StringComparison.Ordinal);
                Assert.True(editor.TextArea.IsFocused);
                Assert.True(document.IsSourceFocused);

                editor.IsReadOnly = true;
                Assert.False(view.FormatCommand.CanExecute(MarkdownFormat.Bold));
                editor.IsReadOnly = false;
                Assert.True(view.FormatCommand.CanExecute(MarkdownFormat.Bold));

                otherInput.Focus();
                Assert.False(document.IsSourceFocused);
                Assert.All(buttons, button => Assert.False(button.IsEffectivelyEnabled));
                editor.TextArea.Focus();
                document.ViewMode = DocumentViewMode.Preview;
                Assert.False(document.IsSourceFocused);
                Assert.False(view.FormatCommand.CanExecute(MarkdownFormat.Bold));
                view.FormatCommand.Execute(MarkdownFormat.Bold);
                Assert.Equal(DocumentViewMode.Preview, document.ViewMode);
            }
            finally
            {
                window.Close();
            }

            Assert.False(document.IsSourceFocused);
            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task SwitchingDocuments_ClearsHiddenSourcesFocusState()
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var first = CreateDocument("First", DocumentViewMode.Split, longDocument: false);
            using var second = CreateDocument("Second", DocumentViewMode.Preview, longDocument: false);
            var host = new DocumentHost
            {
                Documents = new ObservableCollection<DocumentViewModel> { first, second },
                ActiveDocument = first,
            };
            var window = new Window { Width = 1000, Height = 500, Content = host };
            window.Show();
            try
            {
                await SettleAsync(window);
                var view = Assert.IsType<DocumentView>(Assert.Single(host.Children));
                view.FindControl<TextEditor>("Editor")!.TextArea.Focus();
                Assert.True(first.IsSourceFocused);
                host.ActiveDocument = second;
                Assert.False(first.IsSourceFocused);
                Assert.False(second.IsSourceFocused);
                Assert.False(view.FormatCommand.CanExecute(MarkdownFormat.Bold));
            }
            finally
            {
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    private static DocumentViewModel CreateDocument(string title, DocumentViewMode mode, bool longDocument)
    {
        var settings = new EditorSettingsViewModel(Microsoft.Extensions.Options.Options.Create(new EditorOptions()));
        var source = string.Concat(Enumerable.Range(0, longDocument ? 60 : 1)
            .Select(index => $"# {title} heading {index}\n\nParagraph number {index} with some text.\n\n"));
        return new DocumentViewModel(source, null, title, mode, settings, TimeSpan.FromMilliseconds(10), TimeProvider.System);
    }

    private static async Task SettleAsync(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        await Task.Delay(500, TestContext.Current.CancellationToken);
        await FlushAsync(window);
    }

    private static async Task FlushAsync(Window window)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        window.UpdateLayout();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
}
