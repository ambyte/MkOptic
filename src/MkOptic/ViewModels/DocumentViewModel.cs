using System.ComponentModel;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using MkOptic.Core.Documents;
using MkOptic.Core.Threading;

namespace MkOptic.ViewModels;

/// <summary>
/// One open Markdown document: its source text, file location, dirty state and debounced preview.
/// </summary>
public sealed partial class DocumentViewModel : ObservableObject, IDisposable
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly string _untitledName;
    private readonly Debouncer _previewDebouncer;

    public DocumentViewModel(
        string content,
        string? filePath,
        string untitledName,
        DocumentViewMode viewMode,
        EditorSettingsViewModel settings,
        TimeSpan previewDelay,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(untitledName);

        _untitledName = untitledName;
        _previewDebouncer = new Debouncer(previewDelay, timeProvider);

        Settings = settings;
        FilePath = filePath is null ? null : Path.GetFullPath(filePath);
        ViewMode = viewMode;
        PreviewText = content;
        Statistics = DocumentStatistics.Calculate(content);

        Document = new TextDocument(content);
        OutlineVersion = Document.Version;
        Headings = DocumentHeading.Parse(content);
        Document.TextChanged += OnTextChanged;
        Document.UndoStack.PropertyChanged += OnUndoStackPropertyChanged;
    }

    /// <summary>Raised when a relative link in the preview points to another local Markdown file.</summary>
    public event EventHandler<string>? LinkedFileOpenRequested;

    public TextDocument Document { get; }

    public EditorSettingsViewModel Settings { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(DirectoryPath), nameof(IsUntitled), nameof(SuggestedFileName))]
    public partial string? FilePath { get; private set; }

    [ObservableProperty]
    public partial string PreviewText { get; private set; }

    [ObservableProperty]
    public partial IReadOnlyList<DocumentHeading> Headings { get; private set; }

    [ObservableProperty]
    public partial bool IsOutlineOpen { get; set; }

    internal ITextSourceVersion OutlineVersion { get; private set; }

    [ObservableProperty]
    public partial DocumentStatistics Statistics { get; private set; }

    [ObservableProperty]
    public partial DocumentViewMode ViewMode { get; set; }

    [ObservableProperty]
    public partial bool IsSourceFocused { get; set; }

    [ObservableProperty]
    public partial int CaretLine { get; set; } = 1;

    [ObservableProperty]
    public partial int CaretColumn { get; set; } = 1;

    public string Title => FilePath is null ? _untitledName : Path.GetFileName(FilePath);

    public string? DirectoryPath => Path.GetDirectoryName(FilePath);

    public bool IsUntitled => FilePath is null;

    public bool IsDirty => !Document.UndoStack.IsOriginalFile;

    /// <summary>An untitled document that was never edited can be silently replaced by an opened file.</summary>
    public bool IsPristine => IsUntitled && !IsDirty && Document.TextLength == 0;

    public string SuggestedFileName => FilePath is null ? $"{_untitledName}.md" : Path.GetFileName(FilePath);

    public string GetText() => Document.Text;

    public void MarkSaved(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        FilePath = Path.GetFullPath(filePath);
        Document.UndoStack.MarkAsOriginalFile();
    }

    public bool IsSameFile(string filePath) =>
        FilePath is not null && string.Equals(FilePath, Path.GetFullPath(filePath), PathComparison);

    public void RequestOpenLinkedFile(string filePath) => LinkedFileOpenRequested?.Invoke(this, filePath);

    public void Dispose()
    {
        Document.TextChanged -= OnTextChanged;
        Document.UndoStack.PropertyChanged -= OnUndoStackPropertyChanged;
        _previewDebouncer.Dispose();
    }

    private void OnTextChanged(object? sender, EventArgs e) => _previewDebouncer.Schedule(RefreshPreview);

    private void RefreshPreview()
    {
        var text = Document.Text;
        OutlineVersion = Document.Version;
        Headings = DocumentHeading.Parse(text);
        PreviewText = text;
        Statistics = DocumentStatistics.Calculate(text);
    }

    private void OnUndoStackPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(UndoStack.IsOriginalFile))
        {
            OnPropertyChanged(nameof(IsDirty));
        }
    }
}
