using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using LiveMarkdown.Avalonia;
using MkOptic.Core.Documents;
using MkOptic.Core.Options;
using MkOptic.ViewModels;
using MkOptic.Views;

namespace MkOptic.Tests.Preview;

[Collection("Preview UI")]
public sealed class DocumentHostTests(PreviewTestSession fixture)
{
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
