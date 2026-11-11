using System.ComponentModel.DataAnnotations;
using MkOptic.Core.Documents;

namespace MkOptic.Core.Options;

/// <summary>
/// Editor and preview settings bound from the <c>Editor</c> configuration section.
/// </summary>
public sealed class EditorOptions
{
    public const string SectionName = "Editor";

    /// <summary>Delay between the last keystroke and the preview refresh.</summary>
    [Range(0, 5_000)]
    public int PreviewDelayMilliseconds { get; set; } = 250;

    /// <summary>Files larger than this limit are rejected to keep the UI responsive.</summary>
    [Range(1, 512)]
    public int MaxFileSizeMegabytes { get; set; } = 20;

    /// <summary>Extensions offered by the open/save dialogs, including the leading dot.</summary>
    [Required]
    [MinLength(1)]
    public string[] FileExtensions { get; set; } = [];

    public DocumentViewMode DefaultViewMode { get; set; } = DocumentViewMode.Preview;

    [Range(8, 48)]
    public double FontSize { get; set; } = 14;

    [Required(AllowEmptyStrings = false)]
    public string FontFamily { get; set; } = "Cascadia Code, Consolas, Menlo, monospace";

    public bool WordWrap { get; set; } = true;

    public bool ShowLineNumbers { get; set; } = true;

    /// <summary>Reload externally changed files in the background only when there are no local edits.</summary>
    public bool AutoReloadUnmodifiedFiles { get; set; }

    public long MaxFileSizeBytes => MaxFileSizeMegabytes * 1024L * 1024L;
}
