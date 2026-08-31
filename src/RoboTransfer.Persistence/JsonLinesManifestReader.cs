using System.Runtime.CompilerServices;
using System.Text.Json;
using RoboTransfer.Core;

namespace RoboTransfer.Persistence;

public sealed class JsonLinesManifestReader : IManifestReader
{
    public async Task<ManifestReadResult> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path)) return new(ManifestReadState.Incomplete, null, null, new(ErrorCategory.ConfigurationInvalid, "Manifest is missing."));
        MigrationManifestHeader? header = null; MigrationManifestFooter? footer = null; long entries = 0, bytes = 0, eligible = 0, eligibleBytes = 0;
        try
        {
            await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
            {
                using var document = JsonDocument.Parse(line); var root = document.RootElement;
                var type = root.GetProperty("Type").GetString();
                if (type == "header") header = root.GetProperty("Value").Deserialize<MigrationManifestHeader>();
                else if (type == "footer") footer = root.GetProperty("Value").Deserialize<MigrationManifestFooter>();
                else if (type == "entry") { var entry = root.GetProperty("Value").Deserialize<MigrationManifestEntry>() ?? throw new InvalidDataException("Manifest entry is invalid."); entries++; bytes += entry.FileSize; if (entry.TransferState != TransferState.Skipped) { eligible++; eligibleBytes += entry.FileSize; } }
                else return Corrupt("Manifest contains an unknown record.");
            }
            if (header is null || header.FormatVersion != 2) return Corrupt("Manifest header or schema is invalid.");
            if (footer is null || footer.CompletionState != ManifestCompletionState.Complete) return new(ManifestReadState.Incomplete, header, footer, new(ErrorCategory.ConfigurationInvalid, "Manifest scan did not complete."));
            if (footer.SessionId != header.SessionId) return Corrupt("Manifest identity is inconsistent.");
            if (footer.EntryCount != entries || footer.TotalBytes != bytes || footer.EligibleEntryCount != eligible || footer.EligibleBytes != eligibleBytes) return Corrupt("Manifest footer totals do not match its streamed entries.");
            return new(ManifestReadState.Complete, header, footer, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or KeyNotFoundException or InvalidOperationException) { return Corrupt("Manifest is corrupt."); }
        ManifestReadResult Corrupt(string message) => new(ManifestReadState.Corrupt, header, footer, new(ErrorCategory.ConfigurationInvalid, message));
    }

    public async IAsyncEnumerable<MigrationManifestEntry> ReadEntriesAsync(string path, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inspection = await InspectAsync(path, cancellationToken); if (inspection.State != ManifestReadState.Complete) throw new InvalidDataException(inspection.Error?.TechnicianMessage);
        await foreach (var line in File.ReadLinesAsync(path, cancellationToken))
        {
            using var document = JsonDocument.Parse(line); var root = document.RootElement;
            if (root.GetProperty("Type").GetString() == "entry") yield return root.GetProperty("Value").Deserialize<MigrationManifestEntry>() ?? throw new InvalidDataException("Manifest entry is invalid.");
        }
    }
}
