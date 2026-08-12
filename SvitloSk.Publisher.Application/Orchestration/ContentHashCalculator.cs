using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SvitloSk.Publisher.Application.Orchestration;

public class ContentHashCalculator
{
    public string ComputeHash(string markdownText, byte[]? imageBytes)
    {
        if (markdownText == null)
            throw new ArgumentNullException(nameof(markdownText));

        // 1. Unicode NFC Normalization
        string normalized = markdownText.Normalize(NormalizationForm.FormC);

        // 2. Line Ending Normalization (force LF)
        normalized = normalized.Replace("\r\n", "\n").Replace("\r", "\n");

        // 3. Trim trailing whitespace on each line
        string[] lines = normalized.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            lines[i] = lines[i].TrimEnd();
        }
        normalized = string.Join("\n", lines);

        // 4. Convert text to UTF-8 Bytes
        byte[] textBytes = Encoding.UTF8.GetBytes(normalized);

        // 5. Combine with Image bytes (if present)
        byte[] combinedBytes;
        if (imageBytes != null && imageBytes.Length > 0)
        {
            combinedBytes = new byte[textBytes.Length + imageBytes.Length];
            Buffer.BlockCopy(textBytes, 0, combinedBytes, 0, textBytes.Length);
            Buffer.BlockCopy(imageBytes, 0, combinedBytes, textBytes.Length, imageBytes.Length);
        }
        else
        {
            combinedBytes = textBytes;
        }

        // 6. SHA-256 Hash
        byte[] hashBytes = SHA256.HashData(combinedBytes);

        // 7. Output lowercase hexadecimal string
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
