using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using AvaloniaEdit;
using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Core.Documents;
using MkOptic.Core.Options;
using MkOptic.Tests.ViewModels;
using MkOptic.ViewModels;
using MkOptic.Views;
using MkOptic.Services;
using SukiUI.Controls;
using SukiUI.Enums;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MkOptic.Tests.Preview;

[Collection("Preview UI")]
public sealed class ExternalFileChangeTests(PreviewTestSession fixture)
{
    [Fact(Timeout = 30000)]
    public async Task StandardInfoToast_IsQueuedInBottomLeftHost()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var manager = new SukiToastManager();
            var interaction = new SukiUserInteractionService(new SukiDialogManager(), manager);
            var window = new MainWindow();
            var host = window.FindControl<SukiToastHost>("ToastHost")!;
            host.Manager = manager;
            window.Show();
            try
            {
                window.UpdateLayout();
                interaction.ShowInfo("File reloaded", "document.md was updated from disk.");
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

                Assert.Equal(ToastLocation.BottomLeft, host.Position);
                Assert.Single(host.Items);
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
    public async Task HandledDocumentFocus_ChecksExternalChangesInEveryViewMode(DocumentViewMode mode)
    {
        await fixture.Session.Dispatch(async () =>
        {
            var (viewModel, files, interaction, path) = CreateViewModel(mode);
            await viewModel.OpenFilesAsync([path]);
            var window = new MainWindow { DataContext = viewModel };
            window.Show();
            try
            {
                window.UpdateLayout();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                interaction.ReloadQuestions.Clear();
                files.Files[path] = "external";
                interaction.ReloadDecision = true;
                var view = window.FindControl<DocumentHost>("DocumentsHost")!.ActiveView!;
                var target = mode == DocumentViewMode.Preview
                    ? (InputElement)view.FindControl<Control>("Preview")!
                    : view.FindControl<TextEditor>("Editor")!.TextArea;

                target.RaiseEvent(new FocusChangedEventArgs(InputElement.GotFocusEvent) { Handled = true });

                Assert.Single(interaction.ReloadQuestions);
                Assert.Equal("external", viewModel.ActiveDocument!.GetText());
            }
            finally
            {
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 30000)]
    public async Task MenuCheckbox_EnablesBackgroundReloadWhileWindowIsInactive()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var (viewModel, files, interaction, path) = CreateViewModel(DocumentViewMode.Editor);
            await viewModel.OpenFilesAsync([path]);
            var window = new MainWindow { DataContext = viewModel };
            var otherWindow = new Window();
            window.Show();
            try
            {
                window.UpdateLayout();
                var menu = Assert.Single(window.GetLogicalDescendants().OfType<ViewMenuItem>());
                var toggle = Assert.Single(menu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Auto-reload unmodified files"));
                Assert.False(viewModel.EditorSettings.AutoReloadUnmodifiedFiles);
                toggle.SetCurrentValue(MenuItem.IsCheckedProperty, true);
                Assert.True(viewModel.EditorSettings.AutoReloadUnmodifiedFiles);

                otherWindow.Show();
                otherWindow.Activate();
                interaction.ReloadQuestions.Clear();
                files.Files[path] = "background external";
                for (var attempt = 0; attempt < 60 && viewModel.ActiveDocument!.GetText() != "background external"; attempt++)
                {
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }

                Assert.Equal("background external", viewModel.ActiveDocument!.GetText());
                Assert.Empty(interaction.ReloadQuestions);
                Assert.False(viewModel.ActiveDocument.IsDirty);

                toggle.SetCurrentValue(MenuItem.IsCheckedProperty, false);
                Assert.False(viewModel.EditorSettings.AutoReloadUnmodifiedFiles);
            }
            finally
            {
                otherWindow.Close();
                window.Close();
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    private static (MainWindowViewModel ViewModel, InMemoryFileService Files, FakeUserInteractionService Interaction, string Path)
        CreateViewModel(DocumentViewMode mode)
    {
        var path = Path.Combine(Path.GetTempPath(), "mkoptic-focus-test.md");
        var files = new InMemoryFileService();
        files.Files[path] = "original";
        var interaction = new FakeUserInteractionService();
        var options = MsOptions.Create(new EditorOptions { DefaultViewMode = mode, FileExtensions = [".md"] });
        var settings = new EditorSettingsViewModel(options);
        var viewModel = new MainWindowViewModel(files, new FakeFileDialogService(), interaction, new FakeThemeService(),
            new DocumentViewModelFactory(options, settings, TimeProvider.System), settings, options,
            NullLogger<MainWindowViewModel>.Instance, new FakeDocumentExportService());
        return (viewModel, files, interaction, path);
    }
}
