using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Application.Interfaces;

namespace SvitloSk.Publisher.Infrastructure.Git;

public class GitTransport : IGitTransport
{
    public async Task CommitAndPushAsync(string filePath, string commitMessage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        if (string.IsNullOrWhiteSpace(commitMessage))
            throw new ArgumentException("Commit message cannot be null or empty.", nameof(commitMessage));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("Target file not found.", filePath);

        string? workingDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        string relativePath = Path.GetFileName(filePath);

        // 1. Stage file
        await RunGitCommandAsync(workingDir, cancellationToken, "add", "--", relativePath).ConfigureAwait(false);

        // 2. Commit file
        var commitResult = await RunGitCommandAsync(workingDir, cancellationToken, "commit", "-m", commitMessage).ConfigureAwait(false);

        // If nothing was committed, do not push
        if (commitResult.ExitCode != 0 && (commitResult.StdOut.Contains("nothing to commit") || commitResult.StdErr.Contains("nothing to commit")))
        {
            return;
        }

        // 3. Push
        await RunGitCommandAsync(workingDir, cancellationToken, "push").ConfigureAwait(false);
    }

    public async Task<string?> RestoreFromHistoryAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        string? workingDir = Path.GetDirectoryName(Path.GetFullPath(filePath));
        string relativePath = Path.GetFileName(filePath);

        // Query last commit containing this file (we need the hash of the last commit where the file was modified)
        var logResult = await RunGitCommandAsync(workingDir, cancellationToken, "log", "-n", "1", "--pretty=format:%H", "--", relativePath).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(logResult.StdOut))
        {
            return null;
        }

        string commitHash = logResult.StdOut.Trim();

        // Restore file from that commit hash
        await RunGitCommandAsync(workingDir, cancellationToken, "checkout", commitHash, "--", relativePath).ConfigureAwait(false);

        if (File.Exists(filePath))
        {
            return await File.ReadAllTextAsync(filePath, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<GitCommandResult> RunGitCommandAsync(string? workingDirectory, CancellationToken cancellationToken, params string[] arguments)
    {
        using var process = new Process();
        process.StartInfo.FileName = "git";
        process.StartInfo.WorkingDirectory = workingDirectory ?? string.Empty;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        foreach (var arg in arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        var tcs = new TaskCompletionSource<bool>();
        
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Ignore kill errors
            }
            tcs.TrySetCanceled(cancellationToken);
        });

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start git process.");
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to initiate git process execution.", ex);
        }

        var readOutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var readErrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAny(Task.Run(() => process.WaitForExit(), cancellationToken), tcs.Task).ConfigureAwait(false);

        if (cancellationToken.IsCancellationRequested)
        {
            throw new TaskCanceledException("Git command execution was cancelled.");
        }

        string stdout = await readOutTask.ConfigureAwait(false);
        string stderr = await readErrTask.ConfigureAwait(false);

        int exitCode = process.ExitCode;

        // Certain exit codes from commit (like 1 when there's nothing to commit) are captured at callsite
        if (exitCode != 0)
        {
            bool isNoChangeCommit = arguments.Length > 0 && arguments[0] == "commit" && 
                                    (stdout.Contains("nothing to commit") || stderr.Contains("nothing to commit"));

            // Git push fails without a remote configured. We map this to a controlled operation exception rather than letting it exit silently.
            if (!isNoChangeCommit)
            {
                throw new InvalidOperationException($"Git command '{string.Join(" ", arguments)}' failed with exit code {exitCode}. Stderr: {stderr}");
            }
        }

        return new GitCommandResult(exitCode, stdout, stderr);
    }

    private record GitCommandResult(int ExitCode, string StdOut, string StdErr);
}
