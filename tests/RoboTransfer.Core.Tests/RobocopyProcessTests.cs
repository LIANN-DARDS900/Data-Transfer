using System.Diagnostics;
using RoboTransfer.Core;
using RoboTransfer.Robocopy;

namespace RoboTransfer.Core.Tests;

public sealed class RobocopyProcessTests
{
    [Fact]
    public async Task Injected_runner_receives_argument_list_and_success()
    {
        using var tool = new TempFile(); var runner = new FakeRunner(new(1, false, ["copied"], ["warning"], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)); var result = await new RobocopyExecutor(runner).ExecuteAsync(Request(tool.Path, ConflictPolicy.Skip)); Assert.True(result.Succeeded); Assert.NotNull(runner.Start); Assert.False(runner.Start!.UseShellExecute); Assert.Contains("/XJ", runner.Start.ArgumentList);
    }
    [Fact]
    public async Task Cancellation_result_is_interrupted()
    {
        using var tool = new TempFile(); var result = await new RobocopyExecutor(new FakeRunner(new(0, true, [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))).ExecuteAsync(Request(tool.Path, ConflictPolicy.Skip)); Assert.True(result.Cancelled); Assert.Equal(ErrorCategory.Cancelled, Assert.Single(result.Errors).Category);
    }
    [Fact]
    public async Task Failure_exit_is_process_failure()
    {
        using var tool = new TempFile(); var result = await new RobocopyExecutor(new FakeRunner(new(8, false, [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow))).ExecuteAsync(Request(tool.Path, ConflictPolicy.Skip)); Assert.False(result.Succeeded); Assert.Equal(ErrorCategory.ProcessFailure, Assert.Single(result.Errors).Category);
    }
    [Fact]
    public void Unsafe_switches_are_absent_for_every_automatic_policy()
    {
        foreach (var policy in new[] { ConflictPolicy.Skip, ConflictPolicy.ReplaceIfSourceNewer }) { var args = RobocopyArgumentPolicy.Build(Request("tool", policy)); foreach (var unsafeSwitch in new[] { "/MIR", "/MOV", "/MOVE", "/PURGE" }) Assert.DoesNotContain(unsafeSwitch, args, StringComparer.OrdinalIgnoreCase); Assert.Single(args, a => a == "/R:2"); Assert.Single(args, a => a == "/W:2"); }
    }
    [Fact]
    public void Output_retention_is_bounded_and_keeps_recent_lines() { var buffer = new BoundedLineBuffer(64); for (var index = 0; index < 100; index++) buffer.Add(index.ToString()); var lines = buffer.Snapshot(); Assert.Equal(64, lines.Count); Assert.Equal("36", lines[0]); Assert.Equal("99", lines[^1]); }
    private static MigrationExecutionRequest Request(string executable, ConflictPolicy policy) => new(new(Guid.NewGuid(), DateTimeOffset.UtcNow, "machine", "profile", [KnownFolderKind.Documents], "id", "manifest", 1, 1, MigrationRoute.ExternalStorage, MigrationStrategy.RobocopyKnownFolders, "disk", "destination", 2, policy, "skip", VerificationLevel.Standard, 1, "policy", executable, "1", "1"), "source", "destination", KnownFolderKind.Documents);
    private sealed class FakeRunner(ProcessExecutionResult result) : IRobocopyProcessRunner { public ProcessStartInfo? Start { get; private set; } public Task<ProcessExecutionResult> RunAsync(ProcessStartInfo startInfo, CancellationToken cancellationToken) { Start = startInfo; return Task.FromResult(result); } }
    private sealed class TempFile : IDisposable { public TempFile() { Path = System.IO.Path.GetTempFileName(); } public string Path { get; } public void Dispose() => File.Delete(Path); }
}
