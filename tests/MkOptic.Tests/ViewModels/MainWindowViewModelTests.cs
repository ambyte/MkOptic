using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using MkOptic.Core.Documents;
using MkOptic.Core.Files;
using MkOptic.Core.Options;
using MkOptic.Services;
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
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests()
    {
        _files.Files[FirstPath] = "# First";
        _files.Files[SecondPath] = "# Second";

        var options = MsOptions.Create(new EditorOptions
        {
            FileExtensions = [".md"],
            PreviewDelayMilliseconds = 0,
            DefaultViewMode = DocumentViewMode.Preview,
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
            NullLogger<MainWindowViewModel>.Instance);
    }

    [Fact]
    public async Task OpenFiles_AddsDocuments_AndActivatesLastOne()
    {
        await _viewModel.OpenFilesAsync([FirstPath, SecondPath]);

        Assert.Equal(["first.md", "second.md"], _viewModel.Documents.Select(d => d.Title));
        Assert.Equal("second.md", _viewModel.ActiveDocument?.Title);
        Assert.Equal(DocumentViewMode.Preview, _viewModel.ActiveDocument?.ViewMode);
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
}
