using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MkOptic.Core.Files;
using MkOptic.Core.Options;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace MkOptic.Tests.Core;

public sealed class MarkdownFileServiceTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("mkoptic-tests-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task WriteThenRead_RoundTripsUnicodeContent()
    {
        var service = CreateService();
        var path = Path.Combine(_directory, "note.md");
        const string content = "# Heading\n\n$E = mc^2$ — formula ✓";

        await service.WriteAsync(path, content, TestContext.Current.CancellationToken);
        var read = await service.ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal(content, read);
    }

    [Fact]
    public async Task Write_ProducesUtf8WithoutBom_AndLeavesNoTemporaryFiles()
    {
        var service = CreateService();
        var path = Path.Combine(_directory, "note.md");
        await File.WriteAllTextAsync(path, "old", TestContext.Current.CancellationToken);

        await service.WriteAsync(path, "new", TestContext.Current.CancellationToken);

        Assert.Equal("new"u8.ToArray(), await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task Read_DetectsUtf8Bom()
    {
        var path = Path.Combine(_directory, "bom.md");
        await File.WriteAllTextAsync(path, "text", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), TestContext.Current.CancellationToken);

        var read = await CreateService().ReadAsync(path, TestContext.Current.CancellationToken);

        Assert.Equal("text", read);
    }

    [Fact]
    public async Task Read_MissingFile_ThrowsFileNotFound()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            CreateService().ReadAsync(Path.Combine(_directory, "missing.md"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Read_FileOverLimit_ThrowsFileTooLarge()
    {
        var path = Path.Combine(_directory, "big.md");
        await File.WriteAllBytesAsync(path, new byte[(1024 * 1024) + 1], TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<FileTooLargeException>(() =>
            CreateService(maxMegabytes: 1).ReadAsync(path, TestContext.Current.CancellationToken));

        Assert.Equal(1024 * 1024, exception.MaxBytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Read_BlankPath_ThrowsArgumentException(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => CreateService().ReadAsync(path, TestContext.Current.CancellationToken));
    }

    private static MarkdownFileService CreateService(int maxMegabytes = 20) =>
        new(MsOptions.Create(new EditorOptions { MaxFileSizeMegabytes = maxMegabytes, FileExtensions = [".md"] }),
            NullLogger<MarkdownFileService>.Instance);
}
