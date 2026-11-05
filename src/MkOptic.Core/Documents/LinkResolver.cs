namespace MkOptic.Core.Documents;

public enum LinkKind
{
    /// <summary>Not supported or unsafe; ignored.</summary>
    Unsupported,

    /// <summary>A web or mail link that should be opened by the operating system.</summary>
    External,

    /// <summary>A file on the local disk.</summary>
    LocalFile,
}

public readonly record struct ResolvedLink(LinkKind Kind, Uri? ExternalUri = null, string? LocalPath = null);

/// <summary>
/// Classifies links clicked in the preview. Only http(s)/mailto links are handed to the OS,
/// relative links are resolved against the document folder.
/// </summary>
public static class LinkResolver
{
    private static readonly string[] ExternalSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeMailto];

    public static ResolvedLink Resolve(Uri? href, string? documentDirectory)
    {
        if (href is null)
        {
            return default;
        }

        if (href.IsAbsoluteUri)
        {
            if (ExternalSchemes.Contains(href.Scheme, StringComparer.OrdinalIgnoreCase))
            {
                return new ResolvedLink(LinkKind.External, ExternalUri: href);
            }

            return href.IsFile ? new ResolvedLink(LinkKind.LocalFile, LocalPath: href.LocalPath) : default;
        }

        var relative = StripQueryAndFragment(Uri.UnescapeDataString(href.OriginalString));
        if (relative.Length == 0 || documentDirectory is null)
        {
            return default;
        }

        return new ResolvedLink(LinkKind.LocalFile, LocalPath: Path.GetFullPath(Path.Combine(documentDirectory, relative)));
    }

    private static string StripQueryAndFragment(string value)
    {
        var end = value.IndexOfAny(['#', '?']);
        return (end < 0 ? value : value[..end]).Trim();
    }
}
