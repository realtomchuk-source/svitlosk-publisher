using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SvitloSk.Publisher.Infrastructure.Persistence;

public class FileSystemAtomicWriter
{
    public async Task WriteAtomicAsync(
        string targetPath,
        string content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetPath))
            throw new ArgumentException("Target path cannot be null or whitespace.", nameof(targetPath));

        if (content == null)
            throw new ArgumentNullException(nameof(content));

        string? directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string fileName = Path.GetFileName(targetPath);
        string tempPath = Path.Combine(directory ?? string.Empty, $".{fileName}.tmp");

        try
        {
            // Write to temporary file using UTF-8
            await File.WriteAllTextAsync(tempPath, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

            // Atomic swap
            if (File.Exists(targetPath))
            {
                File.Replace(tempPath, targetPath, null);
            }
            else
            {
                File.Move(tempPath, targetPath);
            }
        }
        catch
        {
            // Try to clean up temp file if write fails
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch
                {
                    // Ignore cleanup failure to preserve original exception
                }
            }
            throw;
        }
    }
}
