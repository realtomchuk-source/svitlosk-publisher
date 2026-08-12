using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Infrastructure.Git;
using Xunit;

namespace SvitloSk.Publisher.Tests.Integration.Git;

public class GitTransportTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly GitTransport _gitTransport;

    public GitTransportTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), "SvitloSk_GitTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDirectory);
        _gitTransport = new GitTransport();

        // Initialize local test repository
        RunCmd("init");
        RunCmd("config", "user.name", "TestUser");
        RunCmd("config", "user.email", "test@example.com");
        // Ensure master/main branch initialization
        RunCmd("checkout", "-b", "main");

        // Commit an initial dummy file so the repository actually has commits (prevents exit code 128 "no commits yet" during logs)
        string dummy = Path.Combine(_testDirectory, "dummy.txt");
        File.WriteAllText(dummy, "dummy content");
        RunCmd("add", "dummy.txt");
        RunCmd("commit", "-m", "Initial commit");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, true);
            }
            catch
            {
                // Ignore delete errors
            }
        }
    }

    [Fact]
    public async Task D04_T01_CommitAndPushAsync_WithChanges_ShouldStageAndCommit()
    {
        string targetFile = Path.Combine(_testDirectory, "registry.json");
        await File.WriteAllTextAsync(targetFile, "{}");

        // Git push will fail because there is no configured remote. We expect the push step to throw.
        // But the commit itself should be staged and in log before push fails.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gitTransport.CommitAndPushAsync(targetFile, "Commit 1"));

        var log = RunCmd("log", "-n", "1", "--oneline");
        Assert.Contains("Commit 1", log);
    }

    [Fact]
    public async Task D04_T02_CommitAndPushAsync_NoChanges_ShouldNotThrowOrPush()
    {
        string targetFile = Path.Combine(_testDirectory, "registry.json");
        await File.WriteAllTextAsync(targetFile, "{}");

        // First commit
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gitTransport.CommitAndPushAsync(targetFile, "Commit 1"));

        // Second write with identical content
        await _gitTransport.CommitAndPushAsync(targetFile, "Commit 2"); // should return gracefully due to no changes
    }

    [Fact]
    public async Task D04_T03_CommitMessage_WithSpecialCharacters_ShouldSucceedWithoutInjection()
    {
        string targetFile = Path.Combine(_testDirectory, "registry.json");
        await File.WriteAllTextAsync(targetFile, "{}");

        string specialMessage = "Update; rm -rf /; && echo 'Hacked'";
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gitTransport.CommitAndPushAsync(targetFile, specialMessage));

        var log = RunCmd("log", "-n", "1", "--pretty=format:%s");
        Assert.Equal(specialMessage, log.Trim());
    }

    [Fact]
    public async Task D04_T04_RestoreFromHistoryAsync_ShouldRecoverPreviousVersion()
    {
        string targetFile = Path.Combine(_testDirectory, "registry.json");
        
        await File.WriteAllTextAsync(targetFile, "version 1");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gitTransport.CommitAndPushAsync(targetFile, "Commit 1"));

        await File.WriteAllTextAsync(targetFile, "version 2");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gitTransport.CommitAndPushAsync(targetFile, "Commit 2"));

        var restored = await _gitTransport.RestoreFromHistoryAsync(targetFile);

        Assert.Equal("version 2", restored);
    }

    [Fact]
    public async Task D04_T05_RestoreFromHistoryAsync_NonExistentFile_ShouldReturnNull()
    {
        string path = Path.Combine(_testDirectory, "missing.json");
        var restored = await _gitTransport.RestoreFromHistoryAsync(path);

        Assert.Null(restored);
    }

    [Fact]
    public async Task D04_T07_CancellationRequested_ShouldThrowTaskCanceledException()
    {
        string targetFile = Path.Combine(_testDirectory, "registry.json");
        await File.WriteAllTextAsync(targetFile, "{}");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() => _gitTransport.CommitAndPushAsync(targetFile, "Commit", cts.Token));
    }

    private string RunCmd(params string[] args)
    {
        using var process = new Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.WorkingDirectory = _testDirectory;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        process.Start();
        process.WaitForExit();
        return process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
    }
}
