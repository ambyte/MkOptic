using Microsoft.Extensions.Time.Testing;
using MkOptic.Core.Documents;
using MkOptic.Core.Options;
using MkOptic.ViewModels;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MkOptic.Tests.ViewModels;

public sealed class DocumentViewModelTests
{
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(250);

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public void NewDocument_IsNotDirty_AndPreviewMatchesContent()
    {
        using var document = Create("# Title");

        Assert.False(document.IsDirty);
        Assert.Equal("# Title", document.PreviewText);
        Assert.Equal(1, document.Statistics.Words);
    }

    [Fact]
    public void Editing_MarksDirty_AndUpdatesPreviewAfterDelay()
    {
        using var document = Create("# Title");
        var dirtyNotifications = 0;
        document.PropertyChanged += (_, e) => dirtyNotifications += e.PropertyName == nameof(DocumentViewModel.IsDirty) ? 1 : 0;

        document.Document.Insert(document.Document.TextLength, " and more");

        Assert.True(document.IsDirty);
        Assert.Equal(1, dirtyNotifications);
        Assert.Equal("# Title", document.PreviewText);

        _time.Advance(PreviewDelay);

        Assert.Equal("# Title and more", document.PreviewText);
        Assert.Equal(3, document.Statistics.Words);
    }

    [Fact]
    public void UndoToSavedState_ClearsDirtyFlag()
    {
        using var document = Create("text");

        document.Document.Insert(0, "x");
        document.Document.UndoStack.Undo();

        Assert.False(document.IsDirty);
    }

    [Fact]
    public void MarkSaved_UpdatesPathAndTitle_AndClearsDirtyFlag()
    {
        using var document = Create("text");
        document.Document.Insert(0, "x");
        var path = Path.Combine(Path.GetTempPath(), "saved.md");

        document.MarkSaved(path);

        Assert.False(document.IsDirty);
        Assert.False(document.IsUntitled);
        Assert.Equal("saved.md", document.Title);
        Assert.Equal(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), document.DirectoryPath);
    }

    [Fact]
    public void IsPristine_OnlyForUntouchedEmptyUntitledDocument()
    {
        using var empty = Create(string.Empty);
        using var withText = Create("text");

        Assert.True(empty.IsPristine);
        Assert.False(withText.IsPristine);
    }

    [Fact]
    public void IsSameFile_ComparesNormalizedPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "a.md");
        using var document = Create("text", path);

        Assert.True(document.IsSameFile(Path.Combine(Path.GetTempPath(), ".", "a.md")));
        Assert.False(document.IsSameFile(Path.Combine(Path.GetTempPath(), "b.md")));
    }

    private DocumentViewModel Create(string content, string? path = null) =>
        new(content, path, "Untitled 1", DocumentViewMode.Split, CreateSettings(), PreviewDelay, _time);

    internal static EditorSettingsViewModel CreateSettings() =>
        new(MsOptions.Create(new EditorOptions { FileExtensions = [".md"] }));
}
