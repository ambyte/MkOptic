using Avalonia;
using MkOptic.Services;

namespace MkOptic;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var instance = new SingleInstanceService();
        if (!instance.IsPrimary)
        {
            try
            {
                instance.ActivatePrimaryAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                System.Diagnostics.Trace.TraceError("Could not activate the running instance: {0}", exception);
                Environment.ExitCode = 1;
            }

            return;
        }

        BuildAvaloniaApp().AfterSetup(builder => ((App)builder.Instance!).SingleInstance = instance)
            .StartWithClassicDesktopLifetime(args);
    }

    // Used by the Avalonia designer as well.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
