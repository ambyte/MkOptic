using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using LiveMarkdown.Avalonia;
using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Core.Documents;
using MkOptic.Core.Options;
using MkOptic.Tests.ViewModels;
using MkOptic.ViewModels;
using MkOptic.Views;

namespace MkOptic.Tests.Preview;

[Collection("Preview UI")]
public sealed class DocumentSearchTests(PreviewTestSession fixture)
{
    [Theory(Timeout = 30000)]
    [InlineData(DocumentViewMode.Editor)]
    [InlineData(DocumentViewMode.Split)]
    [InlineData(DocumentViewMode.Preview)]
    public async Task MenuSearch_IsAlwaysVisibleRightAlignedAndKeyboardAccessible(DocumentViewMode mode)
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var document = CreateDocument("alpha beta alpha", mode);
            var window = CreateWindow(document);
            window.Width = 760;
            window.Show();
            try
            {
                await SettleAsync(window);
                var host = window.FindControl<DocumentHost>("DocumentsHost")!;
                var view = Assert.IsType<DocumentView>(host.ActiveView);
                var bar = window.FindControl<DocumentSearchBar>("SearchBar")!;
                var input = bar.FindControl<TextBox>("QueryInput")!;
                var row = window.FindControl<Grid>("MenuRow")!;
                var menu = window.FindControl<Menu>("MainMenu")!;
                Assert.Same(view.Search, bar.DataContext);
                Assert.True(bar.IsEffectivelyVisible);
                Assert.InRange(bar.Bounds.Right, row.Bounds.Width - 1, row.Bounds.Width + 1);
                Assert.True(menu.Bounds.Right <= bar.Bounds.Left);
                Assert.InRange(Math.Abs(menu.Bounds.Center.Y - bar.Bounds.Center.Y), 0, 1);
                Assert.DoesNotContain(view.GetVisualDescendants(), control => control is SearchPanel);
                Assert.DoesNotContain(bar.GetVisualDescendants().OfType<TextBox>(), box => box.Name?.Contains("replace", StringComparison.OrdinalIgnoreCase) == true);
                Assert.Empty(bar.GetVisualDescendants().OfType<ToggleButton>());
                Assert.Equal(360, bar.Bounds.Width);
                Assert.Equal(34, bar.FindControl<Border>("SearchChrome")!.Bounds.Height);
                Assert.Equal(new Thickness(0), input.BorderThickness);
                Assert.All(bar.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("FindAction")), button =>
                {
                    Assert.Equal(new Thickness(0), button.BorderThickness);
                    Assert.All(button.GetVisualDescendants().OfType<Border>(), border => Assert.Equal(new Thickness(0), border.BorderThickness));
                });

                view.Search.FocusContent();
                window.KeyPress(Key.F, RawInputModifiers.Control, PhysicalKey.F, "f");
                Assert.True(input.IsFocused);
                input.Text = "alpha";
                Assert.Equal("1 / 2", view.Search.ResultLabel);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                Assert.Equal("2 / 2", view.Search.ResultLabel);
                window.KeyPress(Key.Enter, RawInputModifiers.Shift, PhysicalKey.Enter, null);
                Assert.Equal("1 / 2", view.Search.ResultLabel);
                window.KeyPress(Key.F3, RawInputModifiers.None, PhysicalKey.F3, null);
                Assert.Equal("2 / 2", view.Search.ResultLabel);
                window.KeyPress(Key.F3, RawInputModifiers.Shift, PhysicalKey.F3, null);
                Assert.Equal("1 / 2", view.Search.ResultLabel);
                window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                Assert.Empty(view.Search.Query);
                Assert.True(bar.IsEffectivelyVisible);
                Assert.Empty(view.FindControl<MarkdownRenderer>("Preview")!.TextSearchMatches);
                Assert.Equal("alpha beta alpha", document.GetText());

                window.RequestedThemeVariant = ThemeVariant.Dark;
                await FlushAsync(window);
                Assert.Equal(Color.Parse("#E6EAF0"), Assert.IsAssignableFrom<ISolidColorBrush>(input.Foreground).Color);
                window.RequestedThemeVariant = ThemeVariant.Light;
                await FlushAsync(window);
                Assert.Equal(Color.Parse("#202631"), Assert.IsAssignableFrom<ISolidColorBrush>(input.Foreground).Color);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task Preview_SearchesVisibleTextHighlightsAndScrollsWithoutChangingSourceSelection()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var markdown = "**needle** beta [needle](https://hidden-destination.invalid)\n\n" +
                string.Concat(Enumerable.Range(0, 70).Select(index => $"Paragraph {index} with enough text for scrolling.\n\n")) +
                "Last **needle** here.";
            using var document = CreateDocument(markdown, DocumentViewMode.Preview);
            var view = new DocumentView { DataContext = document };
            var window = new Window { Width = 800, Height = 350, Content = view };
            window.Show();
            try
            {
                await SettleAsync(window);
                var editor = view.FindControl<TextEditor>("Editor")!;
                var preview = view.FindControl<MarkdownRenderer>("Preview")!;
                var scroll = view.FindControl<ScrollViewer>("PreviewScrollViewer")!;
                editor.Select(2, 6);
                var caret = editor.CaretOffset;
                view.Search.Query = "hidden-destination";
                Assert.Equal(0, view.Search.MatchCount);
                view.Search.Query = "**";
                Assert.Equal(0, view.Search.MatchCount);
                view.Search.Query = "needle beta needle";
                Assert.Equal(1, view.Search.MatchCount);
                view.Search.Query = "needle";
                Assert.Equal(3, view.Search.MatchCount);
                Assert.Equal(3, preview.TextSearchMatches.Count);
                Assert.All(preview.TextSearchMatches, match => Assert.True(match.Block.Highlights.Count > 0));
                view.Search.Navigate(-1);
                await FlushAsync(window);
                Assert.Equal("3 / 3", view.Search.ResultLabel);
                Assert.True(scroll.Offset.Y > 300);
                Assert.Equal(2, editor.SelectionStart);
                Assert.Equal(6, editor.SelectionLength);
                Assert.Equal(caret, editor.CaretOffset);
                view.Search.Query = string.Empty;
                Assert.Empty(preview.TextSearchMatches);
                Assert.Equal(markdown, document.GetText());
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
    [InlineData(DocumentViewMode.Preview)]
    public async Task Search_IsAlwaysCaseInsensitiveLiteralSubstringAndTracksDocumentUpdates(DocumentViewMode mode)
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var document = CreateDocument("alpha Alpha alphabet alpha ТАБЛИЦА таблицы [x] a.b axb .*", mode);
            var view = new DocumentView { DataContext = document };
            var window = new Window { Width = 800, Height = 350, Content = view };
            window.Show();
            try
            {
                await SettleAsync(window);
                var search = view.Search;
                search.Query = "alpha";
                Assert.Equal(4, search.MatchCount);
                search.Query = "ALPHA";
                Assert.Equal(4, search.MatchCount);
                search.Query = "лИц";
                Assert.Equal(2, search.MatchCount);
                search.Query = "[";
                Assert.Equal(1, search.MatchCount);
                search.Query = ".*";
                Assert.Equal(1, search.MatchCount);
                search.Query = "a.b";
                Assert.Equal(1, search.MatchCount);
                search.Query = "alpha";
                Assert.Equal(4, search.MatchCount);
                var editor = view.FindControl<TextEditor>("Editor")!;
                editor.Document.Insert(editor.Document.TextLength, " ALPHA");
                await SettleAsync(window);
                Assert.Equal(5, search.MatchCount);
                Assert.Equal("1 / 5", search.ResultLabel);
                search.Query = "not present";
                Assert.Equal("0 / 0", search.ResultLabel);
                Assert.False(search.NextCommand.CanExecute(null));
                Assert.False(search.PreviousCommand.CanExecute(null));
                Assert.True(search.ClearCommand.CanExecute(null));
                search.ClearCommand.Execute(null);
                Assert.Empty(search.ResultLabel);
                Assert.False(search.HasQuery);
                Assert.False(search.ClearCommand.CanExecute(null));
            }
            finally
            {
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task SwitchingModesAndDocuments_RetainsIndependentSearchAndRebindsMenu()
    {
        await fixture.Session.Dispatch(async () =>
        {
            using var first = CreateDocument("**alpha** and alpha", DocumentViewMode.Split);
            using var second = CreateDocument("beta beta beta", DocumentViewMode.Preview);
            var window = CreateWindow(first);
            var model = Assert.IsType<MainWindowViewModel>(window.DataContext);
            model.Documents.Add(second);
            window.Show();
            try
            {
                await SettleAsync(window);
                var host = window.FindControl<DocumentHost>("DocumentsHost")!;
                var bar = window.FindControl<DocumentSearchBar>("SearchBar")!;
                var firstView = host.ActiveView!;
                firstView.Search.Query = "**";
                Assert.Equal(2, firstView.Search.MatchCount);
                firstView.FindControl<MarkdownRenderer>("Preview")!.Focus();
                Assert.True(firstView.Search.IsPreview);
                Assert.Equal(0, firstView.Search.MatchCount);
                first.ViewMode = DocumentViewMode.Editor;
                Assert.False(firstView.Search.IsPreview);
                Assert.Equal(2, firstView.Search.MatchCount);
                model.ActiveDocument = second;
                await SettleAsync(window);
                Assert.Same(host.ActiveView!.Search, bar.DataContext);
                host.ActiveView.Search.Query = "beta";
                Assert.Equal(3, host.ActiveView.Search.MatchCount);
                model.ActiveDocument = first;
                await FlushAsync(window);
                Assert.Same(firstView.Search, bar.DataContext);
                Assert.Equal("**", firstView.Search.Query);
                model.Documents.Clear();
                await FlushAsync(window);
                Assert.Null(host.ActiveView);
                Assert.Null(bar.DataContext);
                Assert.False(bar.IsEffectivelyEnabled);
                Assert.True(bar.IsEffectivelyVisible);
            }
            finally
            {
                window.DataContext = null;
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    private static DocumentViewModel CreateDocument(string text, DocumentViewMode mode)
    {
        var settings = new EditorSettingsViewModel(Microsoft.Extensions.Options.Options.Create(new EditorOptions()));
        return new DocumentViewModel(text, null, "Search", mode, settings, TimeSpan.FromMilliseconds(10), TimeProvider.System);
    }

    private static MainWindow CreateWindow(DocumentViewModel document)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new EditorOptions());
        var settings = new EditorSettingsViewModel(options);
        var model = new MainWindowViewModel(new InMemoryFileService(), new FakeFileDialogService(),
            new FakeUserInteractionService(), new FakeThemeService(),
            new DocumentViewModelFactory(options, settings, TimeProvider.System), settings, options,
            NullLogger<MainWindowViewModel>.Instance, new FakeDocumentExportService());
        model.Documents.Add(document);
        model.ActiveDocument = document;
        return new MainWindow { DataContext = model, Width = 1000, Height = 450 };
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
