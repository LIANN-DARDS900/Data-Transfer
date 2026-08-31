using System.Text.Json;
using RoboTransfer.Core;

namespace RoboTransfer.Persistence;

public sealed class JsonLinesManifestWriter : IManifestWriter
{
    private readonly FileStream stream;
    private bool headerWritten;
    private bool completed;

    public JsonLinesManifestWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
    }

    public async Task WriteHeaderAsync(MigrationManifestHeader header, CancellationToken cancellationToken = default)
    {
        if (headerWritten) throw new InvalidOperationException("Manifest header is immutable and may only be written once.");
        await WriteLineAsync(new ManifestLine<MigrationManifestHeader>("header", header), cancellationToken); headerWritten = true;
    }

    public async Task WriteEntryAsync(MigrationManifestEntry entry, CancellationToken cancellationToken = default)
    {
        if (!headerWritten) throw new InvalidOperationException("Write the manifest header first.");
        if (completed) throw new InvalidOperationException("The completed manifest is immutable.");
        await WriteLineAsync(new ManifestLine<MigrationManifestEntry>("entry", entry), cancellationToken);
    }

    public async Task CompleteAsync(MigrationManifestFooter footer, CancellationToken cancellationToken = default)
    {
        if (!headerWritten || completed) throw new InvalidOperationException("Manifest completion can only be written once after its header.");
        await WriteLineAsync(new ManifestLine<MigrationManifestFooter>("footer", footer), cancellationToken); completed = true;
        await stream.FlushAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync() { await stream.FlushAsync(); await stream.DisposeAsync(); }
    private async Task WriteLineAsync<T>(ManifestLine<T> line, CancellationToken cancellationToken)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(line);
        await stream.WriteAsync(bytes, cancellationToken); await stream.WriteAsync("\n"u8.ToArray(), cancellationToken);
    }
    private sealed record ManifestLine<T>(string Type, T Value);
}
