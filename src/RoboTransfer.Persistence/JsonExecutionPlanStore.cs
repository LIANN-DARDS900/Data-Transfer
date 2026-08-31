using System.Text.Json;
using RoboTransfer.Core;
namespace RoboTransfer.Persistence;
public sealed class JsonExecutionPlanStore(string directory) : IExecutionPlanStore
{
    public async Task SaveAsync(MigrationExecutionPlan plan, CancellationToken cancellationToken = default) { Directory.CreateDirectory(directory); var target = Path.Combine(directory, $"{plan.SessionId:N}.plan.json"); var temporary = target + $".{Guid.NewGuid():N}.tmp"; try { await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)) { await JsonSerializer.SerializeAsync(stream, plan, cancellationToken: cancellationToken); await stream.FlushAsync(cancellationToken); } File.Move(temporary, target, true); } finally { if (File.Exists(temporary)) File.Delete(temporary); } }
    public async Task<MigrationExecutionPlan?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default) { var path = Path.Combine(directory, $"{sessionId:N}.plan.json"); if (!File.Exists(path)) return null; await using var stream = File.OpenRead(path); return await JsonSerializer.DeserializeAsync<MigrationExecutionPlan>(stream, cancellationToken: cancellationToken); }
}
