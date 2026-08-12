using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Infrastructure.Persistence;
using Xunit;

namespace SvitloSk.Publisher.Tests.Integration.Persistence;

public class FileSystemAtomicWriterTests : IDisposable
{
    private readonly string _testDirectory;

    public FileSystemAtomicWriterTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SvitloSk_WriterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    [Fact]
    public async Task D01_T01_WriteToNonExistingTarget_ShouldSucceed()
    {
        var writer = new FileSystemAtomicWriter();
        string target = Path.Combine(_testDirectory, "test.txt");
        string expectedContent = "Hello World";

        await writer.WriteAtomicAsync(target, expectedContent);

        Assert.True(File.Exists(target));
        string actualContent = await File.ReadAllTextAsync(target, Encoding.UTF8);
        Assert.Equal(expectedContent, actualContent);
    }

    [Fact]
    public async Task D01_T02_OverwriteExistingTarget_ShouldReplaceContentFully()
    {
        var writer = new FileSystemAtomicWriter();
        string target = Path.Combine(_testDirectory, "test.txt");
        
        await File.WriteAllTextAsync(target, "Old long content here...", Encoding.UTF8);
        string newContent = "New short";

        await writer.WriteAtomicAsync(target, newContent);

        Assert.True(File.Exists(target));
        string actualContent = await File.ReadAllTextAsync(target, Encoding.UTF8);
        Assert.Equal(newContent, actualContent);
    }

    [Fact]
    public async Task D01_T03_WriteUkrainianUtf8Content_ShouldRestoreDeterministically()
    {
        var writer = new FileSystemAtomicWriter();
        string target = Path.Combine(_testDirectory, "ukr.txt");
        string expectedContent = "Світловодськ, Україна 2026!";

        await writer.WriteAtomicAsync(target, expectedContent);

        string actualContent = await File.ReadAllTextAsync(target, Encoding.UTF8);
        Assert.Equal(expectedContent, actualContent);
    }

    [Fact]
    public async Task D01_T04_FailureDuringWrite_ShouldLeaveOriginalTargetUntouched()
    {
        var writer = new FileSystemAtomicWriter();
        string target = Path.Combine(_testDirectory, "original.txt");
        string originalContent = "Do not change me";
        await File.WriteAllTextAsync(target, originalContent, Encoding.UTF8);

        // We want to trigger a failure during writing. 
        // Passing a target path that is in an invalid directory structure but not using a file as a directory.
        // Let's use invalid characters in path (like '|' or '*' on Windows).
        string invalidTarget = Path.Combine(_testDirectory, "invalid|char*file.txt");

        await Assert.ThrowsAnyAsync<Exception>(() => writer.WriteAtomicAsync(invalidTarget, "Some content"));

        // Verify original remains
        string actualContent = await File.ReadAllTextAsync(target, Encoding.UTF8);
        Assert.Equal(originalContent, actualContent);
    }

    [Fact]
    public async Task D01_T05_SuccessfulWrite_ShouldLeaveNoTmpFiles()
    {
        var writer = new FileSystemAtomicWriter();
        string target = Path.Combine(_testDirectory, "clean.txt");

        await writer.WriteAtomicAsync(target, "clean content");

        string tempFilePattern = $".{Path.GetFileName(target)}.tmp";
        Assert.False(File.Exists(Path.Combine(_testDirectory, tempFilePattern)));
    }

    [Fact]
    public async Task D01_T06_CanceledToken_ShouldThrowAndCancelWrite()
    {
        var writer = new FileSystemAtomicWriter();
        string target = Path.Combine(_testDirectory, "cancel.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => writer.WriteAtomicAsync(target, "content", cts.Token));
        Assert.False(File.Exists(target));
    }

    [Fact]
    public async Task D01_T08_NestedTargetDirectoryHandling_ShouldCreateDirectories()
    {
        var writer = new FileSystemAtomicWriter();
        string nestedPath = Path.Combine(_testDirectory, "level1", "level2", "file.txt");

        await writer.WriteAtomicAsync(nestedPath, "nested content");

        Assert.True(File.Exists(nestedPath));
        string actualContent = await File.ReadAllTextAsync(nestedPath, Encoding.UTF8);
        Assert.Equal("nested content", actualContent);
    }
}
