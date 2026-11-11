using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MkOptic.Core.Documents;
using MkOptic.Core.Files;
using MkOptic.Core.Options;
using MkOptic.Services;
using SukiUI.Models;

namespace MkOptic.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private const string AppName = "MkOptic";

    private readonly IMarkdownFileService _fileService;
    private readonly IFileDialogService _fileDialogs;
    private readonly IUserInteractionService _interaction;
    private readonly IThemeService _themeService;
    private readonly DocumentViewModelFactory _documentFactory;
    private readonly EditorOptions _options;
    private readonly ILogger<MainWindowViewModel> _logger;
    private bool _checkingExternalChanges;
    private bool _pendingFocusCheck;

    public MainWindowViewModel(
        IMarkdownFileService fileService,
        IFileDialogService fileDialogs,
        IUserInteractionService interaction,
        IThemeService themeService,
        DocumentViewModelFactory documentFactory,
        EditorSettingsViewModel editorSettings,
        IOptions<EditorOptions> options,
        ILogger<MainWindowViewModel> logger)
    {
        _fileService = fileService;
        _fileDialogs = fileDialogs;
        _interaction = interaction;
        _themeService = themeService;
        _documentFactory = documentFactory;
        _options = options.Value;
        _logger = logger;
        EditorSettings = editorSettings;
        Documents.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasDocuments));
    }

    public ObservableCollection<DocumentViewModel> Documents { get; } = [];

    public EditorSettingsViewModel EditorSettings { get; }

    public IReadOnlyList<SukiColorTheme> ColorThemes => _themeService.ColorThemes;

    public bool HasDocuments => Documents.Count > 0;

    public bool IsDarkTheme => _themeService.IsDark;

    public string WindowTitle => ActiveDocument is null
        ? AppName
        : $"{(ActiveDocument.IsDirty ? "● " : string.Empty)}{ActiveDocument.Title} — {AppName}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand), nameof(SaveAsCommand), nameof(CloseActiveCommand), nameof(SetViewModeCommand))]
    public partial DocumentViewModel? ActiveDocument { get; set; }

    [ObservableProperty]
    public partial bool IsSidebarOpen { get; set; } = true;

    public bool HasUnsavedChanges => Documents.Any(document => document.IsDirty);

    [RelayCommand]
    private void NewDocument() => AddDocument(_documentFactory.Create());

    [RelayCommand]
    private async Task OpenAsync()
    {
        var paths = await _fileDialogs.PickFilesToOpenAsync();
        await OpenFilesAsync(paths);
    }

    /// <summary>
    /// Opens the given files; already open files are activated instead of being loaded twice.
    /// </summary>
    public async Task OpenFilesAsync(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        foreach (var path in paths)
        {
            var existing = Documents.FirstOrDefault(document => document.IsSameFile(path));
            if (existing is not null)
            {
                ActiveDocument = existing;
                continue;
            }

            try
            {
                var content = await _fileService.ReadAsync(path);
                var replaceable = ActiveDocument is { IsPristine: true } pristine ? pristine : null;
                AddDocument(_documentFactory.Create(content, path));
                if (replaceable is not null)
                {
                    RemoveDocument(replaceable);
                }
            }
            catch (Exception ex) when (IsFileAccessException(ex))
            {
                LogOpenFailed(_logger, ex, Path.GetFileName(path));
                _interaction.ShowError("Failed to open file", $"{Path.GetFileName(path)}: {DescribeFileError(ex)}");
            }
        }
    }

    [RelayCommand]
    private async Task OpenSampleAsync()
    {
        try
        {
            await OpenFilesAsync([await EmbeddedResources.ExtractToUserDataAsync(EmbeddedResources.WelcomeDocument)]);
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            LogOpenFailed(_logger, ex, EmbeddedResources.WelcomeDocument);
            _interaction.ShowError("Failed to open file", $"{EmbeddedResources.WelcomeDocument}: {DescribeFileError(ex)}");
        }
    }

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private Task<bool> SaveAsync() => SaveDocumentAsync(ActiveDocument!);

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private Task<bool> SaveAsAsync() => SaveDocumentAsAsync(ActiveDocument!);

    [RelayCommand]
    private async Task SaveAllAsync()
    {
        foreach (var document in Documents.Where(document => document.IsDirty).ToArray())
        {
            if (!await SaveDocumentAsync(document))
            {
                return;
            }
        }
    }

    [RelayCommand]
    private Task<bool> CloseAsync(DocumentViewModel document) => TryCloseDocumentAsync(document);

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private Task<bool> CloseActiveAsync() => TryCloseDocumentAsync(ActiveDocument!);

    [RelayCommand(CanExecute = nameof(HasActiveDocument))]
    private void SetViewMode(DocumentViewMode mode) => ActiveDocument!.ViewMode = mode;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarOpen = !IsSidebarOpen;

    [RelayCommand]
    private void ToggleTheme()
    {
        _themeService.ToggleBaseTheme();
        OnPropertyChanged(nameof(IsDarkTheme));
    }

    [RelayCommand]
    private void ChangeColorTheme(SukiColorTheme theme) => _themeService.ChangeColorTheme(theme);

    [RelayCommand]
    private void NextDocument() => ActivateRelative(1);

    [RelayCommand]
    private void PreviousDocument() => ActivateRelative(-1);

    /// <summary>
    /// Asks about every unsaved document. Returns <c>false</c> if the user cancelled or a save failed.
    /// </summary>
    public async Task<bool> ConfirmCloseAllAsync()
    {
        foreach (var document in Documents.Where(document => document.IsDirty).ToArray())
        {
            ActiveDocument = document;
            if (!await ResolveUnsavedChangesAsync(document))
            {
                return false;
            }
        }

        return true;
    }

    public async Task<bool> SaveDocumentAsync(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return document.FilePath is null || document.RequiresSaveAs
            ? await SaveDocumentAsAsync(document)
            : await WriteDocumentAsync(document, document.FilePath);
    }

    public async Task<bool> TryCloseDocumentAsync(DocumentViewModel document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.IsDirty && !await ResolveUnsavedChangesAsync(document))
        {
            return false;
        }

        RemoveDocument(document);
        return true;
    }

    private bool HasActiveDocument() => ActiveDocument is not null;

    /// <summary>Checks the focused document against its last successfully loaded or saved contents.</summary>
    public async Task CheckActiveDocumentForExternalChangesAsync()
    {
        if (_checkingExternalChanges)
        {
            _pendingFocusCheck = true;
            return;
        }

        _checkingExternalChanges = true;
        try
        {
            DocumentViewModel? document;
            do
            {
                document = ActiveDocument;
                if (document?.FilePath is not { } path)
                {
                    return;
                }

                await CheckDocumentForExternalChangesAsync(document, path);
            }
            while (!ReferenceEquals(ActiveDocument, document));
        }
        finally
        {
            _checkingExternalChanges = false;
            _pendingFocusCheck = false;
        }
    }

    /// <summary>Silently refreshes clean documents without requiring window or document focus.</summary>
    public async Task ReloadUnmodifiedDocumentsAsync()
    {
        if (!EditorSettings.AutoReloadUnmodifiedFiles || _checkingExternalChanges)
        {
            return;
        }

        _checkingExternalChanges = true;
        try
        {
            foreach (var document in Documents.ToArray())
            {
                if (!EditorSettings.AutoReloadUnmodifiedFiles)
                {
                    break;
                }

                if (!document.IsDirty && document.FilePath is { } path)
                {
                    await CheckDocumentForExternalChangesAsync(document, path, background: true);
                }
            }
        }
        finally
        {
            _checkingExternalChanges = false;
        }

        // A focus event received during asynchronous polling must not be lost.
        if (_pendingFocusCheck)
        {
            _pendingFocusCheck = false;
            await CheckActiveDocumentForExternalChangesAsync();
        }
    }

    private async Task CheckDocumentForExternalChangesAsync(DocumentViewModel document, string path, bool background = false)
    {
        try
        {
            var version = document.Document.Version;
            var content = await _fileService.ReadAsync(path);
            if (!Documents.Contains(document) || (!background && !ReferenceEquals(ActiveDocument, document))
                || !document.IsSameFile(path) || document.MatchesSavedText(content))
            {
                return;
            }

            var reload = EditorSettings.AutoReloadUnmodifiedFiles && !document.IsDirty;
            var automaticReload = reload;
            if (background && !reload)
            {
                return;
            }

            if (!reload)
            {
                var decision = await _interaction.AskToReloadFileAsync(document.Title, document.IsDirty);
                if (decision is null)
                {
                    return;
                }

                reload = decision.Value;
            }

            // Do not apply a delayed response to a closed, renamed, or newly edited document.
            if (!Documents.Contains(document) || (!background && !ReferenceEquals(ActiveDocument, document)) || !document.IsSameFile(path)
                || !ReferenceEquals(version, document.Document.Version))
            {
                return;
            }

            if (reload)
            {
                document.Reload(content);
                if (automaticReload)
                {
                    _interaction.ShowInfo("File reloaded", $"{document.Title} was updated from disk.");
                }
            }
            else
            {
                document.RequireSaveAs();
                await SaveDocumentAsAsync(document);
            }
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            if (background)
            {
                // Atomic replacements and temporary locks can fail a polling read. Retry on the next tick;
                // foreground focus checks report persistent failures without flooding the user with toasts.
                return;
            }

            LogOpenFailed(_logger, ex, Path.GetFileName(path));
            _interaction.ShowError("Failed to check changed file", $"{Path.GetFileName(path)}: {DescribeFileError(ex)}");
        }
    }

    private async Task<bool> SaveDocumentAsAsync(DocumentViewModel document)
    {
        var path = await _fileDialogs.PickSaveLocationAsync(document.SuggestedFileName);
        if (path is not null && document.RequiresSaveAs && document.IsSameFile(path))
        {
            _interaction.ShowError("Choose a different file name", "The original file has changed on disk. Save your version under a different name to keep both versions.");
            return false;
        }

        return path is not null && await WriteDocumentAsync(document, path);
    }

    private async Task<bool> WriteDocumentAsync(DocumentViewModel document, string path)
    {
        try
        {
            var content = document.GetText();
            await _fileService.WriteAsync(path, content);
            document.MarkSaved(path, content);
            return true;
        }
        catch (Exception ex) when (IsFileAccessException(ex))
        {
            LogSaveFailed(_logger, ex, Path.GetFileName(path));
            _interaction.ShowError("Failed to save file", $"{Path.GetFileName(path)}: {DescribeFileError(ex)}");
            return false;
        }
    }

    private async Task<bool> ResolveUnsavedChangesAsync(DocumentViewModel document) =>
        await _interaction.AskToSaveChangesAsync(document.Title) switch
        {
            UnsavedChangesDecision.Save => await SaveDocumentAsync(document),
            UnsavedChangesDecision.Discard => true,
            _ => false,
        };

    private void AddDocument(DocumentViewModel document)
    {
        document.PropertyChanged += OnDocumentPropertyChanged;
        document.LinkedFileOpenRequested += OnLinkedFileOpenRequested;
        Documents.Add(document);
        ActiveDocument = document;
    }

    private void RemoveDocument(DocumentViewModel document)
    {
        var index = Documents.IndexOf(document);
        if (index < 0)
        {
            return;
        }

        Documents.RemoveAt(index);
        document.PropertyChanged -= OnDocumentPropertyChanged;
        document.LinkedFileOpenRequested -= OnLinkedFileOpenRequested;
        document.Dispose();

        if (ReferenceEquals(ActiveDocument, document))
        {
            ActiveDocument = Documents.Count == 0 ? null : Documents[Math.Min(index, Documents.Count - 1)];
        }
    }

    private void ActivateRelative(int step)
    {
        if (Documents.Count == 0)
        {
            return;
        }

        var index = ActiveDocument is null ? 0 : Documents.IndexOf(ActiveDocument);
        ActiveDocument = Documents[(index + step + Documents.Count) % Documents.Count];
    }

    private void OnDocumentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (ReferenceEquals(sender, ActiveDocument) && e.PropertyName is nameof(DocumentViewModel.IsDirty) or nameof(DocumentViewModel.Title))
        {
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    // Event handlers cannot be awaited; OpenFilesAsync reports expected file errors itself.
    private async void OnLinkedFileOpenRequested(object? sender, string path)
    {
        if (!IsSupportedMarkdownFile(path))
        {
            _interaction.ShowInfo("Link not opened", $"{Path.GetFileName(path)}: only Markdown files can be opened.");
            return;
        }

        await OpenFilesAsync([path]);
    }

    private bool IsSupportedMarkdownFile(string path) =>
        _options.FileExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool IsFileAccessException(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException;

    private static string DescribeFileError(Exception ex) => ex switch
    {
        FileTooLargeException tooLarge => $"file is too large (limit {tooLarge.MaxBytes / 1024 / 1024} MB).",
        FileNotFoundException or DirectoryNotFoundException => "file not found.",
        UnauthorizedAccessException or System.Security.SecurityException => "access to the file is denied.",
        _ => ex.Message,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to open file {FileName}")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, string fileName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to save file {FileName}")]
    private static partial void LogSaveFailed(ILogger logger, Exception exception, string fileName);
}
