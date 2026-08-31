using System.Security.Cryptography;
using System.Text.Json;
using RoboTransfer.Core;

namespace RoboTransfer.Persistence;

public sealed class JsonVerificationStore(string directory) : IVerificationStore
{
    private sealed record Envelope(int SchemaVersion, string Digest, VerificationResult Result);
    public async Task SaveAsync(VerificationResult result, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(directory); var target = Path.Combine(directory, $"{result.SessionId:N}.verification.json"); var temporary = target + $".{Guid.NewGuid():N}.tmp"; var digest = Digest(result);
        try { await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough)) { await JsonSerializer.SerializeAsync(stream, new Envelope(1, digest, result), cancellationToken: cancellationToken); await stream.FlushAsync(cancellationToken); } File.Move(temporary, target, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task<VerificationResult?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(directory, $"{sessionId:N}.verification.json"); if (!File.Exists(path)) return null; try { await using var stream = File.OpenRead(path); var envelope = await JsonSerializer.DeserializeAsync<Envelope>(stream, cancellationToken: cancellationToken); if (envelope is null || envelope.SchemaVersion != 1 || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(envelope.Digest), Convert.FromHexString(Digest(envelope.Result)))) throw new InvalidDataException("Verification record integrity check failed."); return envelope.Result; } catch (JsonException ex) { throw new InvalidDataException("Verification record is corrupt.", ex); }
    }
    private static string Digest(VerificationResult result) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(result)));
}
