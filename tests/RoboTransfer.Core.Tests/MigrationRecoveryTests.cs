using RoboTransfer.Core;
using Xunit;

namespace RoboTransfer.Core.Tests;

public sealed class MigrationRecoveryTests
{
    [Fact]
    public async Task Discovery_returns_incomplete_and_abandon_persists()
    {
        var session = Session(); var journal = new MemoryJournal(session); var recovery = new MigrationRecovery(journal, new ValidDestination()); var discovered = new List<MigrationSession>(); await foreach (var item in recovery.DiscoverAsync(TestContext.Current.CancellationToken)) discovered.Add(item); Assert.Single(discovered); await recovery.AbandonAsync(session, TestContext.Current.CancellationToken); Assert.Equal(MigrationStatus.Abandoned, journal.Saved!.Status);
    }
    [Fact]
    public async Task Valid_resume_passes_complete_identity_checks()
    {
        using var manifest = new TempFile(); using var tool = new TempFile(); var plan = Plan(manifest.Path, tool.Path); var session = Session() with { Id = plan.SessionId, ManifestReference = manifest.Path }; var result = await new MigrationRecovery(new MemoryJournal(session), new ValidDestination(), new CompleteReader()).ValidateResumeAsync(session, plan, Context(plan), TestContext.Current.CancellationToken); Assert.True(result.IsValid);
    }
    [Theory]
    [InlineData("policy")]
    [InlineData("destination")]
    [InlineData("source")]
    public async Task Changed_critical_identity_blocks_resume(string changed)
    {
        using var manifest = new TempFile(); using var tool = new TempFile(); var plan = Plan(manifest.Path, tool.Path); var session = Session() with { Id = plan.SessionId, ManifestReference = manifest.Path }; var context = Context(plan);
        if (changed == "policy") context = context with { Policy = context.Policy with { SchemaVersion = 9 } }; else if (changed == "source") session = session with { Source = new("other", "profile") };
        IDestinationValidator validator = changed == "destination" ? new InvalidDestination(ErrorCategory.DestinationChanged) : new ValidDestination(); var result = await new MigrationRecovery(new MemoryJournal(session), validator, new CompleteReader()).ValidateResumeAsync(session, plan, context, TestContext.Current.CancellationToken); Assert.False(result.IsValid);
    }
    [Fact]
    public async Task Missing_manifest_and_corrupt_manifest_block_resume()
    {
        using var tool = new TempFile(); var plan = Plan(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".missing"), tool.Path); var session = Session() with { Id = plan.SessionId, ManifestReference = plan.ManifestPath }; Assert.False((await new MigrationRecovery(new MemoryJournal(session), new ValidDestination(), new CompleteReader()).ValidateResumeAsync(session, plan, Context(plan), TestContext.Current.CancellationToken)).IsValid);
        using var corrupt = new TempFile(); plan = plan with { ManifestPath = corrupt.Path }; session = session with { ManifestReference = corrupt.Path }; Assert.False((await new MigrationRecovery(new MemoryJournal(session), new ValidDestination(), new CorruptReader()).ValidateResumeAsync(session, plan, Context(plan), TestContext.Current.CancellationToken)).IsValid);
    }
    [Fact]
    public async Task Changed_robocopy_version_and_insufficient_space_block_resume()
    {
        using var manifest = new TempFile(); using var tool = new TempFile(); var plan = Plan(manifest.Path, tool.Path); var session = Session() with { Id = plan.SessionId, ManifestReference = manifest.Path }; var context = Context(plan) with { CurrentRobocopy = new("Robocopy", CapabilityState.Available, tool.Path, "changed") }; var result = await new MigrationRecovery(new MemoryJournal(session), new InvalidDestination(ErrorCategory.InsufficientSpace), new CompleteReader()).ValidateResumeAsync(session, plan, context, TestContext.Current.CancellationToken); Assert.Contains(result.Errors, e => e.Category == ErrorCategory.ToolUnavailable); Assert.Contains(result.Errors, e => e.Category == ErrorCategory.InsufficientSpace);
    }
    private static MigrationSession Session() => new(Guid.NewGuid(), MigrationRole.OldPc, MigrationStatus.Interrupted, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new("machine", "profile"));
    private static MigrationExecutionPlan Plan(string manifest, string tool) { var policy = Policy(); return new(Guid.NewGuid(), DateTimeOffset.UtcNow, "machine", "profile", [KnownFolderKind.Documents], "id", manifest, 1, 1, MigrationRoute.ExternalStorage, MigrationStrategy.RobocopyKnownFolders, "disk", "destination", 2, ConflictPolicy.KeepBoth, "skip", VerificationLevel.Standard, 1, PolicyFingerprint.Create(policy), tool, "1", "1"); }
    private static PolicyProfile Policy() => new(1, false, true, false, true, VerificationLevel.Standard, ConflictPolicy.KeepBoth, []);
    private static DestinationValidationContext Context(MigrationExecutionPlan plan) => new(plan, ["source"], Policy(), null, true, new("Robocopy", CapabilityState.Available, plan.RobocopyExecutablePath, plan.RobocopyVersion));
    private sealed class MemoryJournal(MigrationSession session) : IMigrationJournal { public MigrationSession? Saved { get; private set; } public Task SaveAsync(MigrationSession value, CancellationToken cancellationToken = default) { Saved = value; return Task.CompletedTask; } public Task<MigrationSession?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult<MigrationSession?>(session); public async IAsyncEnumerable<MigrationSession> FindIncompleteAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { yield return session; await Task.Yield(); } }
    private sealed class ValidDestination : IDestinationValidator { public Task<DestinationValidationResult> ValidateAsync(DestinationValidationContext context, CancellationToken cancellationToken = default) => Task.FromResult(new DestinationValidationResult(true, [])); }
    private sealed class InvalidDestination(ErrorCategory category) : IDestinationValidator { public Task<DestinationValidationResult> ValidateAsync(DestinationValidationContext context, CancellationToken cancellationToken = default) => Task.FromResult(new DestinationValidationResult(false, [new(category, "blocked")])); }
    private class CompleteReader : IManifestReader { public virtual Task<ManifestReadResult> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new ManifestReadResult(ManifestReadState.Complete, null, null, null)); public async IAsyncEnumerable<MigrationManifestEntry> ReadEntriesAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) { await Task.CompletedTask; yield break; } }
    private sealed class CorruptReader : CompleteReader { public override Task<ManifestReadResult> InspectAsync(string path, CancellationToken cancellationToken = default) => Task.FromResult(new ManifestReadResult(ManifestReadState.Corrupt, null, null, new(ErrorCategory.ConfigurationInvalid, "corrupt"))); }
    private sealed class TempFile : IDisposable { public TempFile() { Path = System.IO.Path.GetTempFileName(); } public string Path { get; } public void Dispose() => File.Delete(Path); }
}
