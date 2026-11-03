using RoboTransfer.Core;
using RoboTransfer.Persistence;
using RoboTransfer.Robocopy;
using Xunit;

namespace RoboTransfer.Core.Tests;

public sealed class NetworkFailureClassificationTests
{
    [Theory]
    [InlineData("data.pst", 53, ErrorCategory.Unknown)]
    [InlineData("data.pst", 64, ErrorCategory.Unknown)]
    [InlineData("data.pst", 32, ErrorCategory.FileLocked)]
    [InlineData("data.txt", 32, ErrorCategory.FileLocked)]
    [InlineData("data.docx", 33, ErrorCategory.FileLocked)]
    public async Task Windows_error_evidence_not_extension_determines_lock(string name, int code, ErrorCategory expected)
    {
        var root = Path.Combine(Path.GetTempPath(), "RoboTransferTests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source"); var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        var token = TestContext.Current.CancellationToken;
        try
        {
            var file = Path.Combine(source, name); await File.WriteAllTextAsync(file, "test", token);
            var manifest = Path.Combine(root, "manifest.jsonl"); var id = Guid.NewGuid();
            await using (var writer = new JsonLinesManifestWriter(manifest))
            {
                await writer.WriteHeaderAsync(new(id, DateTimeOffset.UtcNow, 0, 0, VerificationLevel.Strong, ConflictPolicy.KeepBoth), token);
                await writer.WriteEntryAsync(new(name, 4, File.GetLastWriteTimeUtc(file), FileAttributes.Normal, KnownFolderKind.Documents, CloudContentState.LocallyAvailable, TransferState.Pending, VerificationState.NotVerified, null, null), token);
                await writer.CompleteAsync(new(id, DateTimeOffset.UtcNow, 1, 4, 1, 4, 0, 0, ManifestCompletionState.Complete), token);
            }
            var plan = new MigrationExecutionPlan(id, DateTimeOffset.UtcNow, "machine", "profile", [KnownFolderKind.Documents], "manifest", manifest, 1, 4, MigrationRoute.ConfiguredNetworkShare, MigrationStrategy.RobocopyKnownFolders, "destination", destination, 100, ConflictPolicy.KeepBoth, "skip", VerificationLevel.Strong, 1, "policy", "robocopy", "1", "1");
            var engine = new KeepBothTransferEngine(new JsonLinesManifestReader(), (_, _, _) => throw new IOException("Injected Windows I/O fault", unchecked((int)0x80070000) | code));
            var result = await engine.ExecuteAsync(new(plan, source, destination, KnownFolderKind.Documents), cancellationToken: token);
            Assert.False(result.Succeeded);
            Assert.Equal(expected, Assert.Single(result.Errors).Category);
            Assert.False(File.Exists(Path.Combine(destination, name)));
            Assert.Equal("test", await File.ReadAllTextAsync(file, token));
        }
        finally { Directory.Delete(root, true); }
    }
}
