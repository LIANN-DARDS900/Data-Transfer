using RoboTransfer.Core;
using RoboTransfer.Persistence;
using RoboTransfer.Robocopy;

namespace RoboTransfer.Core.Tests;

public sealed class ManifestAndTransferTests
{
    [Fact]
    public async Task Completed_manifest_has_authoritative_footer_and_streams_entries()
    {
        using var temp = new Temp(); var path = Path.Combine(temp.Path, "manifest.jsonl"); var id = Guid.NewGuid();
        await using (var writer = new JsonLinesManifestWriter(path)) { await writer.WriteHeaderAsync(new(id, DateTimeOffset.UtcNow, 0, 0, VerificationLevel.Standard, ConflictPolicy.KeepBoth)); await writer.WriteEntryAsync(Entry("a.txt", 3)); await writer.CompleteAsync(new(id, DateTimeOffset.UtcNow, 1, 3, 1, 3, 0, 0, ManifestCompletionState.Complete)); }
        var reader = new JsonLinesManifestReader(); var result = await reader.InspectAsync(path); Assert.Equal(ManifestReadState.Complete, result.State); Assert.Equal(1, result.Footer!.EligibleEntryCount);
        var count = 0; await foreach (var ignored in reader.ReadEntriesAsync(path)) count++; Assert.Equal(1, count);
    }
    [Fact]
    public async Task Missing_footer_is_incomplete_not_complete()
    {
        using var temp = new Temp(); var path = Path.Combine(temp.Path, "manifest.jsonl"); await using (var writer = new JsonLinesManifestWriter(path)) await writer.WriteHeaderAsync(new(Guid.NewGuid(), DateTimeOffset.UtcNow, 0, 0, VerificationLevel.Standard, ConflictPolicy.KeepBoth));
        Assert.Equal(ManifestReadState.Incomplete, (await new JsonLinesManifestReader().InspectAsync(path)).State);
    }
    [Fact]
    public async Task Invalid_json_is_corrupt()
    {
        using var temp = new Temp(); var path = Path.Combine(temp.Path, "manifest.jsonl"); await File.WriteAllTextAsync(path, "not-json"); Assert.Equal(ManifestReadState.Corrupt, (await new JsonLinesManifestReader().InspectAsync(path)).State);
    }
    [Fact]
    public async Task Keep_both_copies_without_overwrite_and_sequences_collisions()
    {
        using var source = new Temp(); using var destination = new Temp(); await File.WriteAllTextAsync(Path.Combine(source.Path, "a.txt"), "new"); await File.WriteAllTextAsync(Path.Combine(destination.Path, "a.txt"), "old"); await File.WriteAllTextAsync(Path.Combine(destination.Path, "a (RoboTransfer copy).txt"), "older");
        var plan = Plan(ConflictPolicy.KeepBoth); var engine = new KeepBothTransferEngine(new FakeReader([Entry("a.txt", 3)])); var result = await engine.ExecuteAsync(new(plan, source.Path, destination.Path, KnownFolderKind.Documents));
        Assert.True(result.Succeeded); Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(destination.Path, "a.txt"))); Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(destination.Path, "a (RoboTransfer copy 2).txt")));
    }
    [Fact]
    public async Task Reconciliation_never_claims_verification()
    {
        var footer = new MigrationManifestFooter(Guid.Empty, DateTimeOffset.UtcNow, 2, 8, 1, 3, 1, 1, ManifestCompletionState.Complete); var reader = new FakeReader([], footer); var result = await new ManifestTransferReconciler(reader).ReconcileAsync(Plan(ConflictPolicy.KeepBoth), new(true, false, 1, 3, [])); Assert.Equal(TransferCompletionState.TransferCompletedVerificationPending, result.State);
    }
    private static MigrationManifestEntry Entry(string path, long bytes) => new(path, bytes, DateTimeOffset.UtcNow, FileAttributes.Normal, KnownFolderKind.Documents, CloudContentState.LocallyAvailable, TransferState.Pending, VerificationState.NotVerified, null, null);
    private static MigrationExecutionPlan Plan(ConflictPolicy policy) => new(Guid.Empty, DateTimeOffset.UtcNow, "machine", "profile", [KnownFolderKind.Documents], "manifest", "manifest", 1, 3, MigrationRoute.ExternalStorage, MigrationStrategy.RobocopyKnownFolders, "disk", "destination", 10, policy, "skip", VerificationLevel.Standard, 1, "policy", "robocopy.exe", "1", "1");
    private sealed class FakeReader(IReadOnlyList<MigrationManifestEntry> entries, MigrationManifestFooter? footer = null) : IManifestReader { public Task<ManifestReadResult> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new ManifestReadResult(ManifestReadState.Complete, new(Guid.Empty, DateTimeOffset.UtcNow, 0, 0, VerificationLevel.Standard, ConflictPolicy.KeepBoth), footer ?? new(Guid.Empty, DateTimeOffset.UtcNow, entries.Count, entries.Sum(e => e.FileSize), entries.Count, entries.Sum(e => e.FileSize), 0, 0, ManifestCompletionState.Complete), null)); public async IAsyncEnumerable<MigrationManifestEntry> ReadEntriesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { foreach (var entry in entries) { cancellationToken.ThrowIfCancellationRequested(); yield return entry; await Task.Yield(); } } }
    private sealed class Temp : IDisposable { public Temp() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RoboTransferTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); } public string Path { get; } public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } } }
}
