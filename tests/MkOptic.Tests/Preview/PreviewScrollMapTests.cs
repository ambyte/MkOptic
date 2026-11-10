using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using LiveMarkdown.Avalonia;
using Markdig;
using MkOptic.Core.Documents;
using MkOptic.Core.Options;
using MkOptic.ViewModels;
using MkOptic.Views;

namespace MkOptic.Tests.Preview;

public sealed class PreviewScrollMapTests(PreviewTestSession fixture) : IClassFixture<PreviewTestSession>
{
    private static readonly string[] ExpectedBlocks = ["First paragraph.", "Quoted paragraph.", "First item", "Second item", "Cell one", "Cell two", "Final paragraph."];

    [Fact(Timeout = 30000)]
    public async Task RenderedText_MapsParagraphsNestedBlocksAndWrappedLines()
    {
        await fixture.Session.Dispatch(() =>
        {
            const string source = "# Heading\n\nFirst paragraph.\n\n> Quoted paragraph.\n\n- First item\n- Second item\n\n" +
                "A long paragraph with **bold text** and many words that wraps over several rendered lines. " +
                "More words continue here to make the paragraph wrap even further.\n\n" +
                "```text\nfirst code line\nsecond code line\n```\n\n" +
                "| Column |\n| --- |\n| Cell one |\n| Cell two |\n\nFinal paragraph.\n";
            var parsed = Markdown.Parse(source, MarkdownUpdateProducer.DefaultPipeline);
            var preview = new MarkdownRenderer
            {
                DocumentUpdate = new MarkdownDocumentUpdate.Full(parsed, 1),
            };
            var window = new Window { Width = 240, Height = 2000, Content = preview };
            window.Show();
            window.UpdateLayout();

            try
            {
                var map = PreviewScrollMap.Create(preview, source);
                var blocks = preview.GetVisualDescendants().OfType<MarkdownTextBlock>().ToArray();
                Assert.NotEmpty(blocks);
                foreach (var text in ExpectedBlocks)
                {
                    var block = Assert.Single(blocks, block => block.LayoutText == text);
                    var top = block.TranslatePoint(default, preview)!.Value.Y;
                    var offset = source.IndexOf(text, StringComparison.Ordinal);
                    Assert.True(Math.Abs(map.ToPreview(offset) - top) <= 1,
                        $"{text}: source {offset}, span {block.SourceSpan}, top {top}, mapped {map.ToPreview(offset)}");
                    Assert.InRange(Math.Abs(map.ToSource(top) - offset), 0, 3);
                }

                var wrapped = Assert.Single(blocks, block => block.LayoutText.StartsWith("A long paragraph", StringComparison.Ordinal));
                Assert.True(wrapped.TextLayout.TextLines.Count > 1);
                var origin = wrapped.TranslatePoint(default, preview)!.Value;
                var lastLine = wrapped.TextLayout.TextLines[^1];
                var localOffset = lastLine.FirstTextSourceIndex;
                var lineText = wrapped.LayoutText[localOffset..].TrimEnd('\r', '\n');
                var sourceOffset = source.IndexOf(lineText, StringComparison.Ordinal);
                Assert.True(sourceOffset > 0);
                var bounds = wrapped.GetTextRangeBoundsInControl(localOffset, 1)[0];
                Assert.InRange(Math.Abs(map.ToPreview(sourceOffset) - origin.Y - bounds.Top), 0, 1);

                var code = Assert.Single(blocks, block => block.LayoutText.Contains("second code line", StringComparison.Ordinal));
                var codeOrigin = code.TranslatePoint(default, preview)!.Value;
                var codeIndex = code.LayoutText.IndexOf("second code line", StringComparison.Ordinal);
                var codeBounds = code.GetTextRangeBoundsInControl(codeIndex, 1)[0];
                var codeOffset = source.IndexOf("second code line", StringComparison.Ordinal);
                Assert.InRange(Math.Abs(map.ToPreview(codeOffset) - codeOrigin.Y - codeBounds.Top), 0, 1);
            }
            finally
            {
                window.Close();
            }
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task SplitView_ScrollsToMatchingTextInBothDirectionsWithoutMovingCaret()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var source = string.Concat(Enumerable.Range(0, 40).Select(index => $"# Heading {index}\n\nParagraph number {index}.\n\n"));
            var settings = new EditorSettingsViewModel(Microsoft.Extensions.Options.Options.Create(new EditorOptions()));
            using var model = new DocumentViewModel(source, null, "Scroll test", DocumentViewMode.Split, settings,
                TimeSpan.FromSeconds(5), TimeProvider.System);
            var view = new DocumentView { DataContext = model };
            var window = new Window { Width = 1000, Height = 500, Content = view };
            window.Show();
            try
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(500, TestContext.Current.CancellationToken);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                window.UpdateLayout();
                var editor = view.FindControl<TextEditor>("Editor")!;
                var preview = view.FindControl<MarkdownRenderer>("Preview")!;
                var previewScroll = view.FindControl<ScrollViewer>("PreviewScrollViewer")!;
                var editorScroll = editor.GetVisualDescendants().OfType<ScrollViewer>().First();
                var textView = editor.TextArea.TextView;
                var sourceOffset = source.IndexOf("Paragraph number 15.", StringComparison.Ordinal);
                var sourceY = textView.GetVisualPosition(new TextViewPosition(editor.Document.GetLocation(sourceOffset)), VisualYPosition.LineTop).Y;
                var block = Assert.Single(preview.GetVisualDescendants().OfType<MarkdownTextBlock>(), block => block.LayoutText == "Paragraph number 15.");
                var previewY = block.TranslatePoint(default, previewScroll)!.Value.Y + previewScroll.Offset.Y;
                var caret = editor.CaretOffset;
                Assert.Equal(source, preview.MarkdownBuilder!.ToString());
                Assert.NotNull(preview.DocumentUpdate);
                Assert.Equal(DocumentViewMode.Split, model.ViewMode);
                Assert.Equal(source, editor.Document.Text);
                Assert.True(editorScroll.Extent.Height > editorScroll.Viewport.Height,
                    $"Editor extent {editorScroll.Extent}, viewport {editorScroll.Viewport}, bounds {editor.Bounds}, window {window.Bounds}");
                Assert.True(previewScroll.Extent.Height > previewScroll.Viewport.Height,
                    $"Preview extent {previewScroll.Extent}, viewport {previewScroll.Viewport}, bounds {preview.Bounds}");

                editorScroll.SetCurrentValue(ScrollViewer.OffsetProperty, new Vector(0, sourceY));
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(500, TestContext.Current.CancellationToken);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.InRange(Math.Abs(previewScroll.Offset.Y - previewY), 0, 2);

                previewScroll.Offset = new Vector(0, previewY + 150);
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                previewScroll.Offset = new Vector(0, previewY);
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.InRange(Math.Abs(editorScroll.Offset.Y - sourceY), 0, 2);
                Assert.Equal(caret, editor.CaretOffset);

                // While the debounced preview is stale, its old source spans must not move the editor.
                editor.Document.Insert(0, "New text\n\n");
                window.UpdateLayout();
                var staleOffset = editorScroll.Offset.Y;
                previewScroll.Offset = new Vector(0, previewY + 100);
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.Equal(staleOffset, editorScroll.Offset.Y);
            }
            finally
            {
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    public static class TestApplication
    {
        public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
    }
}

public sealed class PreviewTestSession : IAsyncDisposable
{
    public HeadlessUnitTestSession Session { get; } = HeadlessUnitTestSession.StartNew(
        typeof(PreviewScrollMapTests.TestApplication), AvaloniaTestIsolationLevel.PerAssembly);

    public ValueTask DisposeAsync() => Session.DisposeAsync();
}
