using System.Text.Json;
using Microsoft.Extensions.Logging;
using RoboTransfer.Core;
namespace RoboTransfer.Persistence;
public sealed class JsonMigrationJournal(string directory, ILogger<JsonMigrationJournal> logger) : IMigrationJournal
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public async Task SaveAsync(MigrationSession session, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"{session.Id:N}.json"); var temporary = target + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous)) await JsonSerializer.SerializeAsync(stream, session, Options, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, target, true);
        logger.LogInformation("Migration session {SessionId} journal saved", session.Id);
    }
    public async Task<MigrationSession?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(directory, $"{sessionId:N}.json"); if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync<MigrationSession>(stream, Options, cancellationToken).ConfigureAwait(false);
    }
}
