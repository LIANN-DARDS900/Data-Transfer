using System.Text.Json;
using RoboTransfer.Core;
namespace RoboTransfer.Persistence;
public sealed class JsonOperationalRecordStore(string directory) : IOperationalRecordStore
{
    public async Task SaveAsync(MigrationOperationalRecord record, CancellationToken cancellationToken = default) { Directory.CreateDirectory(directory); var target = Path.Combine(directory, $"{record.SessionId:N}.operations.json"); var temporary = target + $".{Guid.NewGuid():N}.tmp"; try { await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)) { await JsonSerializer.SerializeAsync(stream, record, cancellationToken: cancellationToken); await stream.FlushAsync(cancellationToken); } File.Move(temporary, target, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); } }
    public async Task<MigrationOperationalRecord?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default) { var path = Path.Combine(directory, $"{sessionId:N}.operations.json"); if (!File.Exists(path)) return null; await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<MigrationOperationalRecord>(stream, cancellationToken: cancellationToken); }
}
