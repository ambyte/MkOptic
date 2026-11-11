using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MkOptic.Core.Files;
using MkOptic.Core.Options;
using MkOptic.Options;
using MkOptic.Services;
using MkOptic.ViewModels;
using MkOptic.Views;
using SukiUI.Dialogs;
using SukiUI.Toasts;

namespace MkOptic;

internal static class ServiceRegistration
{
    private const string EnvironmentVariablePrefix = "MKOPTIC_";

    public static ServiceProvider BuildServiceProvider()
    {
        var environment = Environment.GetEnvironmentVariable($"{EnvironmentVariablePrefix}ENVIRONMENT") ?? "Production";
        // Defaults are embedded so that the app works as a single file; appsettings files next to the executable override them.
        using var defaultSettings = EmbeddedResources.Open(EmbeddedResources.DefaultSettings);
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonStream(defaultSettings)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables(EnvironmentVariablePrefix)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging
            .AddConfiguration(configuration.GetSection("Logging"))
            .AddSimpleConsole(console => console.SingleLine = true)
            .AddDebug());

        services.AddOptions<EditorOptions>()
            .Bind(configuration.GetSection(EditorOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => options.FileExtensions.All(extension => extension.Length > 1 && extension[0] == '.'),
                "Editor:FileExtensions must contain extensions starting with a dot, e.g. \".md\".");

        services.AddOptions<AppearanceOptions>()
            .Bind(configuration.GetSection(AppearanceOptions.SectionName))
            .Validate(
                options => Enum.IsDefined(options.BaseTheme) && Enum.IsDefined(options.AccentColor),
                "Appearance:BaseTheme or Appearance:AccentColor has an unknown value.");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMarkdownFileService, MarkdownFileService>();
        services.AddSingleton<IFileDialogService, StorageProviderFileDialogService>();
        services.AddSingleton<ISukiDialogManager, SukiDialogManager>();
        services.AddSingleton<ISukiToastManager, SukiToastManager>();
        services.AddSingleton<IUserInteractionService, SukiUserInteractionService>();
        services.AddSingleton<IThemeService, SukiThemeService>();
        services.AddSingleton(provider => new UserPreferencesService(
            UserPreferencesService.DefaultPath, provider.GetRequiredService<ILogger<UserPreferencesService>>()));

        services.AddSingleton<EditorSettingsViewModel>();
        services.AddSingleton<DocumentViewModelFactory>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // Fail fast on invalid configuration instead of on first use.
        _ = provider.GetRequiredService<IOptions<EditorOptions>>().Value;
        _ = provider.GetRequiredService<IOptions<AppearanceOptions>>().Value;
        return provider;
    }
}
