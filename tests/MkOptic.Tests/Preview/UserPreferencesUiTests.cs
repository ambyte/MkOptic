using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Core.Options;
using MkOptic.Options;
using MkOptic.Services;
using MkOptic.Tests.ViewModels;
using MkOptic.ViewModels;
using MkOptic.Views;
using SukiUI.Dialogs;
using SukiUI.Toasts;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MkOptic.Tests.Preview;

[Collection("Preview UI")]
public sealed class UserPreferencesUiTests(PreviewTestSession fixture)
{
    [Fact(Timeout = 30000)]
    public async Task MenuOptionsAndRealAppearance_RoundTripThroughUserProfile()
    {
        await fixture.Session.Dispatch(async () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), $"mkoptic-preferences-ui-{Guid.NewGuid():N}");
            var path = Path.Combine(directory, "preferences.json");
            var theme = new SukiThemeService(MsOptions.Create(new AppearanceOptions()));
            theme.ApplyConfiguredTheme();
            var viewModel = CreateViewModel(theme);
            using var preferences = new UserPreferencesService(path, NullLogger<UserPreferencesService>.Instance);
            preferences.ApplyAndTrack(viewModel, theme);
            var window = new MainWindow(viewModel, new SukiDialogManager(), new SukiToastManager(), preferences);
            window.Show();
            try
            {
                window.UpdateLayout();
                var menu = Assert.Single(window.GetLogicalDescendants().OfType<ViewMenuItem>());
                var items = menu.Items.OfType<MenuItem>().ToArray();
                var wrap = Assert.Single(items, item => Equals(item.Header, "Word wrap"));
                var numbers = Assert.Single(items, item => Equals(item.Header, "Line numbers"));
                var autoReload = Assert.Single(items, item => Equals(item.Header, "Auto-reload unmodified files"));
                wrap.SetCurrentValue(MenuItem.IsCheckedProperty, false);
                numbers.SetCurrentValue(MenuItem.IsCheckedProperty, false);
                autoReload.SetCurrentValue(MenuItem.IsCheckedProperty, true);
                viewModel.ToggleSidebarCommand.Execute(null);
                viewModel.ToggleThemeCommand.Execute(null);
                var accent = theme.ColorThemes.First(color => color.DisplayName != theme.AccentColorName);
                viewModel.ChangeColorThemeCommand.Execute(accent);

                // Closing must wait for the save queue rather than losing the final menu/theme change.
                window.Close();
                await preferences.FlushAsync();
                await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                Assert.False(window.IsVisible);

                theme.ApplyConfiguredTheme();
                var restored = CreateViewModel(theme);
                using var restoredPreferences = new UserPreferencesService(path, NullLogger<UserPreferencesService>.Instance);
                restoredPreferences.ApplyAndTrack(restored, theme);

                Assert.False(restored.IsSidebarOpen);
                Assert.False(restored.EditorSettings.WordWrap);
                Assert.False(restored.EditorSettings.ShowLineNumbers);
                Assert.True(restored.EditorSettings.AutoReloadUnmodifiedFiles);
                Assert.False(theme.IsDark);
                Assert.Equal(accent.DisplayName, theme.AccentColorName);
            }
            finally
            {
                window.Close();
                await preferences.FlushAsync();
                theme.ApplyConfiguredTheme();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }

            return true;
        }, TestContext.Current.CancellationToken);
    }

    private static MainWindowViewModel CreateViewModel(IThemeService theme)
    {
        var options = MsOptions.Create(new EditorOptions { FileExtensions = [".md"] });
        var settings = new EditorSettingsViewModel(options);
        return new MainWindowViewModel(new InMemoryFileService(), new FakeFileDialogService(),
            new FakeUserInteractionService(), theme, new DocumentViewModelFactory(options, settings, TimeProvider.System),
            settings, options, NullLogger<MainWindowViewModel>.Instance);
    }
}
