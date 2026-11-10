using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using LiveMarkdown.Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MkOptic.Services;
using MkOptic.Views;

namespace MkOptic;

public sealed partial class App : Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    internal SingleInstanceService? SingleInstance { get; set; }

    public override void Initialize()
    {
        ConfigureImageDecoders();
        MarkdownRenderer.ConfigurePipeline += pipeline => pipeline.UseExtendedMathematics();
        MarkdownNode.Edit(nodes => nodes
            .Register<MathInlineNode>()
            .Register<MathBlockNode>());

        AvaloniaXamlLoader.Load(this);
    }

    internal static void ConfigureImageDecoders()
    {
        // Badge URLs often have no .svg extension; let the SVG decoder inspect the image data.
        AsyncImageLoader.DefaultDecoders = [SvgImageDecoder.Shared, DefaultBitmapDecoder.Shared];
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ServiceRegistration.BuildServiceProvider();
            _logger = _services.GetRequiredService<ILogger<App>>();
            RegisterGlobalExceptionHandlers();
            _services.GetRequiredService<IThemeService>().ApplyConfiguredTheme();

            var window = _services.GetRequiredService<MainWindow>();
            window.StartupFiles = desktop.Args ?? [];
            desktop.MainWindow = window;
            OpenFilesActivatedByOs(window);
            // Wait until the window is shown, including when another launch arrives during startup.
            window.Opened += StartActivationListener;
            void StartActivationListener(object? sender, EventArgs e)
            {
                window.Opened -= StartActivationListener;
                SingleInstance?.StartListening(
                    paths => Dispatcher.UIThread.Post(async () => await window.OpenFilesAsync(paths)), _logger);
            }

            desktop.Exit += (_, _) => _services.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// macOS delivers files opened from Finder (double-click, "Open With") as an activation event
    /// instead of command-line arguments.
    /// </summary>
    private void OpenFilesActivatedByOs(MainWindow window)
    {
        if (TryGetFeature(typeof(IActivatableLifetime)) is not IActivatableLifetime activatable)
        {
            return;
        }

        activatable.Activated += async (_, e) =>
        {
            if (e is FileActivatedEventArgs { Files: { Count: > 0 } files })
            {
                var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
                await window.OpenFilesAsync(paths);
            }
        };
    }

    private void RegisterGlobalExceptionHandlers()
    {
        // A failure in a single UI action should not take down the editor with unsaved documents,
        // so UI-thread exceptions are logged and reported instead of terminating the process.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            LogUnhandled(_logger!, e.Exception, "UI thread");
            _services?.GetRequiredService<IUserInteractionService>()
                .ShowError("Unexpected error", e.Exception.Message);
            e.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            LogUnhandled(_logger!, e.Exception, "background task");
            e.SetObserved();
        };

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                LogUnhandled(_logger!, exception, "application domain");
            }
        };
    }

    [LoggerMessage(Level = LogLevel.Critical, Message = "Unhandled exception on {Source}")]
    private static partial void LogUnhandled(ILogger logger, Exception exception, string source);
}
