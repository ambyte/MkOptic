using System.Reflection;

namespace MkOptic;

/// <summary>Files compiled into the executable so that it can be distributed as a single file.</summary>
internal static class EmbeddedResources
{
    public const string DefaultSettings = "appsettings.json";
    public const string WelcomeDocument = "welcome.md";

    public static Stream Open(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
        ?? throw new InvalidOperationException($"Embedded resource '{name}' was not found.");

    /// <summary>
    /// Writes the embedded resource to the per-user data folder (the editor works with files on disk)
    /// and returns its path. The file is rewritten only when its content differs.
    /// </summary>
    public static async Task<string> ExtractToUserDataAsync(string name)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MkOptic");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);

        await using var resource = Open(name);
        using var buffer = new MemoryStream();
        await resource.CopyToAsync(buffer);
        var content = buffer.ToArray();

        if (!File.Exists(path) || !(await File.ReadAllBytesAsync(path)).AsSpan().SequenceEqual(content))
        {
            await File.WriteAllBytesAsync(path, content);
        }

        return path;
    }
}
