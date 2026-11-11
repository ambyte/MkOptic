using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MkOptic.Core.Documents;
using MkOptic.Core.Files;
using MkOptic.Core.Options;
using MkOptic.Services;
using MkOptic.Services.Export;
using MkOptic.ViewModels;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MkOptic.Tests.ViewModels;

public sealed class MainWindowViewModelTests
{
    private static readonly string DocsDirectory = Path.Combine(Path.GetTempPath(), "mkoptic-vm");
    private static readonly string FirstPath = Path.Combine(DocsDirectory, "first.md");
    private static readonly string SecondPath = Path.Combine(DocsDirectory, "second.md");

    private readonly InMemoryFileService _files = new();
    private readonly FakeFileDialogService _dialogs = new();
    private readonly FakeUserInteractionService _interaction = new();
    private readonly FakeDocumentExportService _export = new();
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        _files.Files[FirstPath] = "# First";
        _files.Files[SecondPath] = "# Second";

        var options = MsOptions.Create(new EditorOptions
        {
            FileExtensions = [".md"],
            PreviewDelayMilliseconds = 0,
        });
        var settings = new EditorSettingsViewModel(options);
        _viewModel = new MainWindowViewModel(
            _files,
            _dialogs,
            _interaction,
            new FakeThemeService(),
            new DocumentViewModelFactory(options, settings, new FakeTimeProvider()),
            settings,
            options,
            NullLogger<MainWindowViewModel>.Instance, _export);
    }

    [Fact]
    public async Task OpenFiles_AddsDocuments_AndActivatesLastOne()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);

        Assert.Equal(["first.md", "second.md"], _viewModel.Documents.Select(d => d.Title));
        Assert.Equal("second.md", _viewModel.ActiveDocument?.Title);
        Assert.All(_viewModel.Documents, document => Assert.Equal(DocumentViewMode.Preview, document.ViewMode));
    }

    [Fact]
    public async Task OpenFiles_AlreadyOpenFile_IsActivatedNotDuplicated()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);

        await _viewModel.OpenFilesAsync([FirstPath]);

        Assert.Equal(2, _viewModel.Documents.Count);
        Assert.Equal("first.md", _viewModel.ActiveDocument?.Title);
    }

    [Fact]
    public async Task OpenFiles_ReplacesPristineUntitledDocument()
    {
        _viewModel.NewDocumentCommand.Execute(null);

        await _viewModel.OpenFilesAsync([FirstPath]);

        Assert.Single(_viewModel.Documents);
        Assert.Equal("first.md", _viewModel.ActiveDocument?.Title);
    }

    [Fact]
    public async Task OpenFiles_ReadFailure_ShowsErrorAndKeepsGoing()
    {
        _files.ReadFailure = new FileTooLargeException(FirstPath, 100, 10);

        await _viewModel.OpenFilesAsync([FirstPath]);

        Assert.Empty(_viewModel.Documents);
        Assert.Single(_interaction.Errors);
        Assert.Contains("too large", _interaction.Errors[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_UntitledDocument_AsksForLocationAndWritesFile()
    {
        var target = Path.Combine(DocsDirectory, "new.md");
        _dialogs.SaveLocation = target;
        _viewModel.NewDocumentCommand.Execute(null);
        var document = _viewModel.ActiveDocument!;
        document.Document.Text = "hello";

        var saved = await _viewModel.SaveDocumentAsync(document);

        Assert.True(saved);
        Assert.Equal(1, _dialogs.SaveDialogCalls);
        Assert.Equal("hello", _files.Files[target]);
        Assert.False(document.IsDirty);
        Assert.Equal("new.md", document.Title);
    }

    [Fact]
    public async Task Save_WhenDialogCancelled_ReturnsFalseAndKeepsDirtyFlag()
    {
        _viewModel.NewDocumentCommand.Execute(null);
        var document = _viewModel.ActiveDocument!;
        document.Document.Text = "hello";

        Assert.False(await _viewModel.SaveDocumentAsync(document));
        Assert.True(document.IsDirty);
    }

    [Fact]
    public async Task Close_DirtyDocument_CancelKeepsItOpen()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        document.Document.Insert(0, "x");
        _interaction.Decision = UnsavedChangesDecision.Cancel;

        var closed = await _viewModel.TryCloseDocumentAsync(document);

        Assert.False(closed);
        Assert.Single(_viewModel.Documents);
        Assert.Equal(["first.md"], _interaction.AskedDocuments);
    }

    [Fact]
    public async Task Close_DirtyDocument_SaveWritesThenCloses()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        document.Document.Insert(0, "x");
        _interaction.Decision = UnsavedChangesDecision.Save;

        Assert.True(await _viewModel.TryCloseDocumentAsync(document));

        Assert.Empty(_viewModel.Documents);
        Assert.Null(_viewModel.ActiveDocument);
        Assert.Equal("x# First", _files.Files[FirstPath]);
    }

    [Fact]
    public async Task Close_ActivatesNeighbourDocument()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);

        await _viewModel.TryCloseDocumentAsync(_viewModel.ActiveDocument!);

        Assert.Equal("first.md", _viewModel.ActiveDocument?.Title);
    }

    [Fact]
    public async Task ConfirmCloseAll_DiscardAllowsClosing_WithoutWriting()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);
        foreach (var document in _viewModel.Documents)
        {
            document.Document.Insert(0, "x");
        }

        _interaction.Decision = UnsavedChangesDecision.Discard;

        Assert.True(await _viewModel.ConfirmCloseAllAsync());
        Assert.Equal(2, _interaction.AskedDocuments.Count);
        Assert.Equal("# First", _files.Files[FirstPath]);
    }

    [Fact]
    public async Task WindowTitle_ShowsDirtyMarker()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);

        _viewModel.ActiveDocument!.Document.Insert(0, "x");

        Assert.Equal("● first.md — MkOptic", _viewModel.WindowTitle);
    }

    [Fact]
    public async Task LinkedFile_WithUnsupportedExtension_IsNotOpened()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);

        _viewModel.ActiveDocument!.RequestOpenLinkedFile(Path.Combine(DocsDirectory, "program.exe"));

        Assert.Single(_viewModel.Documents);
        Assert.Single(_interaction.Infos);
    }

    [Fact]
    public void NextDocument_WrapsAround()
    {
        _viewModel.NewDocumentCommand.Execute(null);
        _viewModel.ActiveDocument!.Document.Text = "a";
        _viewModel.NewDocumentCommand.Execute(null);

        _viewModel.NextDocumentCommand.Execute(null);

        Assert.Same(_viewModel.Documents[0], _viewModel.ActiveDocument);
    }

    [Fact]
    public async Task ExternalChanges_UnchangedFile_DoesNotPromptOrDiscardEdits()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        document.Document.Insert(0, "local ");

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        Assert.Empty(_interaction.ReloadQuestions);
        Assert.Equal("local # First", document.GetText());
        Assert.True(document.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalChanges_Reload_ReplacesTextAndResetsDirtyState(bool localEdits)
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        if (localEdits)
        {
            document.Document.Insert(0, "local ");
        }

        _files.Files[FirstPath] = "# External";
        _interaction.ReloadDecision = true;

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();
        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        Assert.Equal(("first.md", localEdits), Assert.Single(_interaction.ReloadQuestions));
        Assert.Equal("# External", document.GetText());
        Assert.Equal("# External", document.PreviewText);
        Assert.False(document.IsDirty);
        Assert.False(document.Document.UndoStack.CanUndo);
        Assert.Equal(0, _dialogs.SaveDialogCalls);
    }

    [Fact]
    public async Task ExternalChanges_DeclineReload_SavesCopyWithoutOverwritingOriginal()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        document.Document.Insert(0, "local ");
        _files.Files[FirstPath] = "external";
        _dialogs.SaveLocation = Path.Combine(DocsDirectory, "copy.md");

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        Assert.Equal(1, _dialogs.SaveDialogCalls);
        Assert.Equal("external", _files.Files[FirstPath]);
        Assert.Equal("local # First", _files.Files[_dialogs.SaveLocation]);
        Assert.Equal(_dialogs.SaveLocation, document.FilePath);
        Assert.False(document.IsDirty);
        Assert.False(document.RequiresSaveAs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalChanges_CancelSaveAs_ProtectsOriginalAndKeepsDocumentDirty(bool chooseOriginal)
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        _files.Files[FirstPath] = "external";
        _dialogs.SaveLocation = chooseOriginal ? FirstPath : null;

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();
        var saved = await _viewModel.SaveDocumentAsync(document);

        Assert.False(saved);
        Assert.True(document.IsDirty);
        Assert.True(document.RequiresSaveAs);
        Assert.Equal("# First", document.GetText());
        Assert.Equal("external", _files.Files[FirstPath]);
        Assert.Equal(2, _dialogs.SaveDialogCalls);
        Assert.False(await _viewModel.TryCloseDocumentAsync(document));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExternalChanges_AutoReload_OnlyReloadsUnmodifiedDocuments(bool localEdits)
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        Assert.False(_viewModel.EditorSettings.AutoReloadUnmodifiedFiles);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        if (localEdits)
        {
            document.Document.Insert(0, "local ");
        }

        _files.Files[FirstPath] = "external";
        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        if (localEdits)
        {
            Assert.Equal(("first.md", true), Assert.Single(_interaction.ReloadQuestions));
            Assert.Equal("local # First", document.GetText());
            Assert.True(document.IsDirty);
        }
        else
        {
            Assert.Empty(_interaction.ReloadQuestions);
            Assert.Equal("external", document.GetText());
            Assert.False(document.IsDirty);
        }
    }

    [Fact]
    public async Task ExternalChanges_AfterOwnSave_DoesNotPrompt()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.ActiveDocument!.Document.Insert(0, "local ");
        await _viewModel.SaveDocumentAsync(_viewModel.ActiveDocument);

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        Assert.Empty(_interaction.ReloadQuestions);
    }

    [Fact]
    public async Task ExternalChanges_ReadFailure_PreservesDocumentAndReportsError()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _files.ReadFailure = new IOException("File is busy");

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        Assert.Single(_interaction.Errors);
        Assert.Empty(_interaction.ReloadQuestions);
        Assert.Equal("# First", _viewModel.ActiveDocument!.GetText());
    }

    [Fact]
    public async Task ExternalChanges_OverlappingFocusChecks_ShowOnlyOnePrompt()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _files.Files[FirstPath] = "external";
        var decision = new TaskCompletionSource<bool?>();
        _interaction.ReloadHandler = () => decision.Task;

        var check = _viewModel.CheckActiveDocumentForExternalChangesAsync();
        await _viewModel.CheckActiveDocumentForExternalChangesAsync();
        Assert.Single(_interaction.ReloadQuestions);
        decision.SetResult(true);
        await check;

        Assert.Equal("external", _viewModel.ActiveDocument!.GetText());
    }

    [Fact]
    public async Task ExternalChanges_SwitchDuringRead_ChecksNewActiveDocument()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);
        var pendingRead = new TaskCompletionSource<string>();
        _files.ReadHandler = path => path == SecondPath
            ? pendingRead.Task
            : Task.FromResult("first external");
        _interaction.ReloadDecision = true;

        var check = _viewModel.CheckActiveDocumentForExternalChangesAsync();
        _viewModel.ActiveDocument = _viewModel.Documents[0];
        await _viewModel.CheckActiveDocumentForExternalChangesAsync();
        pendingRead.SetResult("second external");
        await check;

        Assert.Equal(("first.md", false), Assert.Single(_interaction.ReloadQuestions));
        Assert.Equal("first external", _viewModel.ActiveDocument.GetText());
        Assert.Equal("# Second", _viewModel.Documents[1].GetText());
    }

    [Fact]
    public async Task ExternalChanges_EditDuringRead_DoesNotSilentlyReload()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        var pendingRead = new TaskCompletionSource<string>();
        _files.ReadHandler = _ => pendingRead.Task;
        _interaction.ReloadDecision = true;

        var check = _viewModel.CheckActiveDocumentForExternalChangesAsync();
        _viewModel.ActiveDocument!.Document.Insert(0, "local ");
        pendingRead.SetResult("external");
        await check;

        Assert.Equal(("first.md", true), Assert.Single(_interaction.ReloadQuestions));
        Assert.Equal("local # First", _viewModel.ActiveDocument.GetText());
        Assert.True(_viewModel.ActiveDocument.IsDirty);
    }

    [Fact]
    public async Task ExternalChanges_DialogUnavailable_DoesNotAssumeSaveAsDecision()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _files.Files[FirstPath] = "external";
        _interaction.ReloadHandler = () => Task.FromResult<bool?>(null);

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        Assert.Equal(0, _dialogs.SaveDialogCalls);
        Assert.False(_viewModel.ActiveDocument!.RequiresSaveAs);
        Assert.Equal("# First", _viewModel.ActiveDocument.GetText());
    }

    [Fact]
    public void AutoReloadSetting_UsesConfiguredStartupValue()
    {
        var settings = new EditorSettingsViewModel(MsOptions.Create(new EditorOptions
        {
            AutoReloadUnmodifiedFiles = true,
        }));

        Assert.True(settings.AutoReloadUnmodifiedFiles);
    }

    [Fact]
    public async Task BackgroundReload_Disabled_DoesNotReadOrPrompt()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _files.ReadHandler = _ => throw new InvalidOperationException("Disabled polling must not read files.");

        await _viewModel.ReloadUnmodifiedDocumentsAsync();

        Assert.Empty(_interaction.ReloadQuestions);
        Assert.Equal("# First", _viewModel.ActiveDocument!.GetText());
    }

    [Fact]
    public async Task BackgroundReload_RefreshesAllCleanDocumentsWithoutFocusOrPrompts()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        _files.Files[FirstPath] = "first external";
        _files.Files[SecondPath] = "second external";

        await _viewModel.ReloadUnmodifiedDocumentsAsync();

        Assert.Equal("first external", _viewModel.Documents[0].GetText());
        Assert.Equal("second external", _viewModel.Documents[1].GetText());
        Assert.All(_viewModel.Documents, document => Assert.False(document.IsDirty));
        Assert.Empty(_interaction.ReloadQuestions);
        Assert.Equal(0, _dialogs.SaveDialogCalls);
    }

    [Fact]
    public async Task BackgroundReload_DirtyDocument_IsNotReadOrPrompted()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        _viewModel.ActiveDocument!.Document.Insert(0, "local ");
        _files.ReadHandler = _ => throw new InvalidOperationException("Dirty documents must not be polled.");

        await _viewModel.ReloadUnmodifiedDocumentsAsync();

        Assert.Equal("local # First", _viewModel.ActiveDocument.GetText());
        Assert.Empty(_interaction.ReloadQuestions);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BackgroundReload_LocalEditOrSettingDisabledDuringRead_PreventsReload(bool edit)
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        var pendingRead = new TaskCompletionSource<string>();
        _files.ReadHandler = _ => pendingRead.Task;

        var check = _viewModel.ReloadUnmodifiedDocumentsAsync();
        if (edit)
        {
            _viewModel.ActiveDocument!.Document.Insert(0, "local ");
        }
        else
        {
            _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = false;
        }

        pendingRead.SetResult("external");
        await check;

        Assert.Equal(edit ? "local # First" : "# First", _viewModel.ActiveDocument!.GetText());
        Assert.Empty(_interaction.ReloadQuestions);
        Assert.Equal(0, _dialogs.SaveDialogCalls);
    }

    [Fact]
    public async Task BackgroundReload_FocusDuringRead_DefersDirtyDocumentPrompt()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        _viewModel.Documents[1].Document.Insert(0, "local ");
        var pendingRead = new TaskCompletionSource<string>();
        _files.ReadHandler = path => path == FirstPath ? pendingRead.Task : Task.FromResult("second external");

        var poll = _viewModel.ReloadUnmodifiedDocumentsAsync();
        await _viewModel.CheckActiveDocumentForExternalChangesAsync();
        Assert.Empty(_interaction.ReloadQuestions);
        pendingRead.SetResult("first external");
        await poll;

        Assert.Equal(("second.md", true), Assert.Single(_interaction.ReloadQuestions));
        Assert.Equal("local # Second", _viewModel.ActiveDocument!.GetText());
    }

    [Fact]
    public async Task BackgroundReload_ReadFailure_RetriesWithoutRepeatedNotifications()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        _files.ReadFailure = new IOException("File is temporarily locked.");
        await _viewModel.ReloadUnmodifiedDocumentsAsync();
        Assert.Empty(_interaction.Errors);
        Assert.Equal("# First", _viewModel.ActiveDocument!.GetText());

        _files.ReadFailure = null;
        _files.Files[FirstPath] = "external";
        await _viewModel.ReloadUnmodifiedDocumentsAsync();

        Assert.Equal("external", _viewModel.ActiveDocument.GetText());
        Assert.Empty(_interaction.ReloadQuestions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AutoReload_ShowsOneInfoNotificationOnlyAfterContentsChange(bool background)
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;

        if (background)
        {
            await _viewModel.ReloadUnmodifiedDocumentsAsync();
        }
        else
        {
            await _viewModel.CheckActiveDocumentForExternalChangesAsync();
        }

        Assert.Empty(_interaction.Infos);
        _files.Files[FirstPath] = "external update";

        for (var check = 0; check < 2; check++)
        {
            if (background)
            {
                await _viewModel.ReloadUnmodifiedDocumentsAsync();
            }
            else
            {
                await _viewModel.CheckActiveDocumentForExternalChangesAsync();
            }
        }

        Assert.Equal("first.md was updated from disk.", Assert.Single(_interaction.Infos));
        Assert.Equal("external update", _viewModel.ActiveDocument!.GetText());
        Assert.Empty(_interaction.ReloadQuestions);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ManuallyConfirmedReload_DoesNotShowAutomaticReloadNotification(bool enabled, bool localEdits)
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = enabled;
        if (localEdits)
        {
            _viewModel.ActiveDocument!.Document.Insert(0, "local ");
        }

        _files.Files[FirstPath] = "external update";
        _interaction.ReloadDecision = true;

        await _viewModel.CheckActiveDocumentForExternalChangesAsync();

        Assert.Equal("external update", _viewModel.ActiveDocument!.GetText());
        Assert.Single(_interaction.ReloadQuestions);
        Assert.Empty(_interaction.Infos);
    }

    [Theory]
    [InlineData(DocumentExportFormat.Html)]
    [InlineData(DocumentExportFormat.Pdf)]
    public async Task Export_UsesUnsavedText_WithoutChangingSavedState(DocumentExportFormat format)
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var document = _viewModel.ActiveDocument!;
        document.Document.Text = "# Unsaved changes";
        _dialogs.ExportLocation = Path.Combine(DocsDirectory, format == DocumentExportFormat.Html ? "first.html" : "first.pdf");

        await (format == DocumentExportFormat.Html ? _viewModel.ExportHtmlCommand : _viewModel.ExportPdfCommand).ExecuteAsync(null);

        var export = Assert.Single(_export.Exports);
        Assert.Equal("# Unsaved changes", export.Markdown);
        Assert.Equal(FirstPath, export.SourcePath);
        Assert.Equal(format, export.Format);
        Assert.Equal(_dialogs.ExportLocation, export.DestinationPath);
        Assert.True(document.IsDirty);
        Assert.Equal(FirstPath, document.FilePath);
        Assert.Equal("# First", _files.Files[FirstPath]);
        Assert.Single(_interaction.Infos);
    }

    [Fact]
    public async Task Export_Cancel_DoesNotWriteOrNotify()
    {
        _viewModel.NewDocumentCommand.Execute(null);
        await _viewModel.ExportHtmlCommand.ExecuteAsync(null);

        Assert.Empty(_export.Exports);
        Assert.Empty(_interaction.Infos);
        Assert.Empty(_interaction.Errors);
    }

    [Fact]
    public void Export_CommandsFollowActiveDocument()
    {
        Assert.False(_viewModel.ExportHtmlCommand.CanExecute(null));
        Assert.False(_viewModel.ExportPdfCommand.CanExecute(null));
        var notifications = 0;
        _viewModel.ExportHtmlCommand.CanExecuteChanged += (_, _) => notifications++;
        _viewModel.NewDocumentCommand.Execute(null);
        Assert.True(_viewModel.ExportHtmlCommand.CanExecute(null));
        Assert.True(_viewModel.ExportPdfCommand.CanExecute(null));
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task Export_Failure_IsReported()
    {
        _viewModel.NewDocumentCommand.Execute(null);
        _dialogs.ExportLocation = Path.Combine(DocsDirectory, "new.pdf");
        _export.Failure = new IOException("Export failed");
        await _viewModel.ExportPdfCommand.ExecuteAsync(null);

        Assert.Single(_interaction.Errors);
        Assert.Empty(_interaction.Infos);
    }

    [Fact]
    public async Task Export_RejectsOverwriteOfAnyOpenSource()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);
        _dialogs.ExportLocation = FirstPath;
        await _viewModel.ExportHtmlCommand.ExecuteAsync(null);

        Assert.Empty(_export.Exports);
        Assert.Single(_interaction.Errors);
    }

    [Fact]
    public async Task Export_TabSwitchDuringDialog_UsesOriginalSnapshot()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        var original = _viewModel.ActiveDocument!;
        _dialogs.ExportHandler = () =>
        {
            original.Document.Text = "Changed while dialog was open";
            _viewModel.NewDocumentCommand.Execute(null);
            return Task.FromResult<string?>(Path.Combine(DocsDirectory, "first.html"));
        };

        await _viewModel.ExportHtmlCommand.ExecuteAsync(null);

        var export = Assert.Single(_export.Exports);
        Assert.Equal("# First", export.Markdown);
        Assert.Equal(FirstPath, export.SourcePath);
        Assert.NotSame(original, _viewModel.ActiveDocument);
    }

    [Fact]
    public async Task AutoReload_ReadFailure_DoesNotShowSuccessNotification()
    {
        await _viewModel.OpenFilesAsync([FirstPath]);
        _viewModel.EditorSettings.AutoReloadUnmodifiedFiles = true;
        _files.ReadFailure = new IOException("File is temporarily locked.");

        await _viewModel.ReloadUnmodifiedDocumentsAsync();

        Assert.Empty(_interaction.Infos);
        Assert.Equal("# First", _viewModel.ActiveDocument!.GetText());
    }
}
