using System.Text.Json;

namespace RoboTransfer.Persistence;

public sealed record StructuredLogEvent(DateTimeOffset Timestamp, Guid? SessionId, string Component, string Severity, string Category, string SafeMessage);
public sealed class RotatingStructuredLog(string directory, long maximumBytes = 2 * 1024 * 1024, int retainedFiles = 5)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task WriteAsync(StructuredLogEvent value, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken); try { Directory.CreateDirectory(directory); var path = Path.Combine(directory, "robotransfer.log.jsonl"); if (File.Exists(path) && new FileInfo(path).Length >= maximumBytes) Rotate(path); var safe = value with { SafeMessage = DiagnosticsRedactor.RedactMessage(value.SafeMessage) }; await File.AppendAllTextAsync(path, JsonSerializer.Serialize(safe) + Environment.NewLine, cancellationToken); } finally { gate.Release(); }
    }
    public async Task<string> ExportAsync(string destinationDirectory, CancellationToken cancellationToken = default) { Directory.CreateDirectory(destinationDirectory); var target = Path.Combine(destinationDirectory, $"RoboTransfer-diagnostics-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.jsonl"); IEnumerable<string> files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "robotransfer.log.jsonl*").OrderBy(path => path) : []; await using var output = File.Create(target); foreach (var file in files) { await using var input = File.OpenRead(file); await input.CopyToAsync(output, cancellationToken); } return target; }
    private void Rotate(string path) { for (var index = retainedFiles - 1; index >= 1; index--) { var source = index == 1 ? path : path + $".{index - 1}"; var target = path + $".{index}"; if (File.Exists(source)) File.Move(source, target, true); } }
}
