using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoboTransfer.Core;
namespace RoboTransfer.Persistence;

public sealed class JsonMigrationJournal(string directory, ILogger<JsonMigrationJournal> logger) : IMigrationJournal
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private const int JournalSchema = 1;
    private sealed record JournalEnvelope(int SchemaVersion, MigrationSession Session);

    public async Task SaveAsync(MigrationSession session, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory); var target = PathFor(session.Id); var temporary = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, new JournalEnvelope(JournalSchema, session), Options, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            File.Move(temporary, target, true);
            logger.LogInformation("Session journal committed. SessionId={SessionId}; Status={Status}", session.Id, session.Status);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<MigrationSession?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var path = PathFor(sessionId); if (!File.Exists(path)) return null;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var envelope = await JsonSerializer.DeserializeAsync<JournalEnvelope>(stream, Options, cancellationToken).ConfigureAwait(false);
            if (envelope?.SchemaVersion != JournalSchema) throw new InvalidDataException("The journal schema is missing or unsupported.");
            if (envelope.Session.Id != sessionId) throw new InvalidDataException("The journal identity does not match its file name.");
            return envelope.Session;
        }
        catch (JsonException ex) { throw new InvalidDataException("The session journal is corrupt and was not loaded.", ex); }
    }

    public async IAsyncEnumerable<MigrationSession> FindIncompleteAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(directory)) yield break;
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out var id)) continue;
            MigrationSession? session;
            try { session = await LoadAsync(id, cancellationToken).ConfigureAwait(false); }
            catch (InvalidDataException) { logger.LogWarning("Corrupt session journal was excluded from recovery discovery. SessionId={SessionId}", id); continue; }
            if (session is not null && session.Status is not (MigrationStatus.Completed or MigrationStatus.Abandoned)) yield return session;
        }
    }
    private string PathFor(Guid id) => Path.Combine(directory, $"{id:N}.json");
}
