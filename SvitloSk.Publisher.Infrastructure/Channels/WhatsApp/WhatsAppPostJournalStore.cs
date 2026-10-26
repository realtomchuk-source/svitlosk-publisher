using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using SvitloSk.Publisher.Core.Engine;
using SvitloSk.Publisher.Infrastructure.Persistence;

namespace SvitloSk.Publisher.Infrastructure.Channels.WhatsApp;

public record WhatsAppJournalEntry(
    [property: JsonPropertyName("marker")] string Marker,
    [property: JsonPropertyName("marker_type")] string MarkerType,
    [property: JsonPropertyName("edition_date")] string EditionDate,
    [property: JsonPropertyName("sequence_number")] int? SequenceNumber,
    [property: JsonPropertyName("territory_id")] string TerritoryId,
    [property: JsonPropertyName("external_message_id")] string? ExternalMessageId,
    [property: JsonPropertyName("content_hash")] string ContentHash,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("attempts")] int Attempts,
    [property: JsonPropertyName("updated_at_kyiv")] string UpdatedAtKyiv
);

public record WhatsAppPostJournalModel(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("legacy_cleanup_completed_date")] string? LegacyCleanupCompletedDate,
    [property: JsonPropertyName("last_reconciled_at_kyiv")] string LastReconciledAtKyiv,
    [property: JsonPropertyName("entries")] List<WhatsAppJournalEntry> Entries
);

/// <summary>
/// Persists the WhatsApp Reconciliation audit journal (`local/registry/whatsapp_post_journal.json`)
/// with 30-day automatic entry rotation.
/// </summary>
public class WhatsAppPostJournalStore
{
    private readonly FileSystemAtomicWriter _atomicWriter;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public WhatsAppPostJournalStore(FileSystemAtomicWriter? atomicWriter = null)
    {
        _atomicWriter = atomicWriter ?? new FileSystemAtomicWriter();
    }

    public async Task<WhatsAppPostJournalModel> LoadAsync(string journalPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(journalPath) || !File.Exists(journalPath))
        {
            return new WhatsAppPostJournalModel(
                SchemaVersion: 1,
                LegacyCleanupCompletedDate: null,
                LastReconciledAtKyiv: KyivClock.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Entries: new List<WhatsAppJournalEntry>()
            );
        }

        try
        {
            string json = await File.ReadAllTextAsync(journalPath, cancellationToken).ConfigureAwait(false);
            var model = JsonSerializer.Deserialize<WhatsAppPostJournalModel>(json, JsonOptions);
            if (model != null)
            {
                return model with { Entries = model.Entries ?? new List<WhatsAppJournalEntry>() };
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN][WhatsApp] Could not read post journal '{journalPath}' ({ex.Message}). Starting with fresh journal.");
        }

        return new WhatsAppPostJournalModel(
            SchemaVersion: 1,
            LegacyCleanupCompletedDate: null,
            LastReconciledAtKyiv: KyivClock.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Entries: new List<WhatsAppJournalEntry>()
        );
    }

    public async Task SaveWithRotationAsync(
        string journalPath,
        WhatsAppPostJournalModel journal,
        int retentionDays = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(journalPath))
            throw new ArgumentException("Journal path cannot be null or empty.", nameof(journalPath));

        DateTime cutoffDate = KyivClock.Now.Date.AddDays(-retentionDays);

        var rotatedEntries = (journal.Entries ?? new List<WhatsAppJournalEntry>())
            .Where(e =>
            {
                if (DateTime.TryParse(e.EditionDate, out var dt))
                {
                    return dt.Date >= cutoffDate;
                }
                return true;
            })
            .ToList();

        var updated = journal with
        {
            LastReconciledAtKyiv = KyivClock.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Entries = rotatedEntries
        };

        string json = JsonSerializer.Serialize(updated, JsonOptions);
        await _atomicWriter.WriteAtomicAsync(journalPath, json, cancellationToken).ConfigureAwait(false);
    }
}
