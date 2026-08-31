using RoboTransfer.Core;
using RoboTransfer.Robocopy;
using Xunit;

namespace RoboTransfer.Core.Tests;

public sealed class MigrationEngineTests
{
    [Theory]
    [InlineData(0, RobocopyOutcome.CleanSuccess, true)]
    [InlineData(1, RobocopyOutcome.Copied, true)]
    [InlineData(2, RobocopyOutcome.ExtraOrMismatch, true)]
    [InlineData(3, RobocopyOutcome.ExtraOrMismatch, true)]
    [InlineData(4, RobocopyOutcome.PartialNonfatal, true)]
    [InlineData(7, RobocopyOutcome.PartialNonfatal, true)]
    [InlineData(8, RobocopyOutcome.Failure, false)]
    [InlineData(16, RobocopyOutcome.Failure, false)]
    public void Exit_codes_follow_robocopy_bitmask_classes(int code, RobocopyOutcome outcome, bool succeeded)
    {
        var result = RobocopyExitCode.Interpret(code);
        Assert.Equal(outcome, result.Outcome); Assert.Equal(succeeded, result.Succeeded);
    }

    [Fact]
    public void Safe_arguments_never_delete_or_use_shell_semantics()
    {
        var arguments = RobocopyArgumentPolicy.Build(Request(ConflictPolicy.Skip));
        Assert.Contains("/R:2", arguments); Assert.Contains("/W:2", arguments); Assert.Contains("/XJ", arguments);
        Assert.DoesNotContain(arguments, value => value.Equals("/MIR", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(arguments, value => value.Contains("MOVE", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(ConflictPolicy.KeepBoth)]
    [InlineData(ConflictPolicy.ManualDecision)]
    public void Policies_requiring_preparation_cannot_be_misrepresented(ConflictPolicy policy) =>
        Assert.Throws<InvalidOperationException>(() => RobocopyArgumentPolicy.Build(Request(policy)));

    [Fact]
    public void Keep_both_names_are_deterministic_and_collision_safe()
    {
        var output = Path.Combine("out", "document.docx"); var firstCopy = Path.Combine("out", "document (RoboTransfer copy).docx"); var secondCopy = Path.Combine("out", "document (RoboTransfer copy 2).docx");
        var existing = new HashSet<string>(StringComparer.Ordinal) { output, firstCopy };
        Assert.Equal(secondCopy, ConflictResolver.GetKeepBothPath(output, existing.Contains));
    }

    [Fact]
    public void Replace_requires_policy_and_confirmation()
    {
        Assert.False(ConflictResolver.CanReplace(ConflictPolicy.Replace, true, false));
        Assert.False(ConflictResolver.CanReplace(ConflictPolicy.Replace, false, true));
        Assert.True(ConflictResolver.CanReplace(ConflictPolicy.Replace, true, true));
        Assert.Throws<InvalidOperationException>(() => RobocopyArgumentPolicy.Build(Request(ConflictPolicy.Replace)));
    }

    [Fact]
    public void Execution_snapshot_fingerprint_changes_with_destination_and_policy()
    {
        var first = Plan();
        Assert.NotEqual(first.Fingerprint, (first with { DestinationIdentity = "disk-2" }).Fingerprint);
        Assert.NotEqual(first.Fingerprint, (first with { PolicyFingerprint = "changed" }).Fingerprint);
    }

    [Fact]
    public void Policy_fingerprint_is_stable()
    {
        var policy = PolicyProfile.Conservative;
        Assert.Equal(PolicyFingerprint.Create(policy), PolicyFingerprint.Create(policy));
    }

    private static MigrationExecutionRequest Request(ConflictPolicy policy) => new(Plan() with { ConflictPolicy = policy }, "/source", "/destination", KnownFolderKind.Documents);
    private static MigrationExecutionPlan Plan() => new(Guid.Parse("10000000-0000-0000-0000-000000000001"), DateTimeOffset.UnixEpoch, "machine", "profile", [KnownFolderKind.Documents], "manifest-id", "/manifest.jsonl", 2, 10, MigrationRoute.ExternalStorage, MigrationStrategy.RobocopyKnownFolders, "disk-1", "/destination", 100, ConflictPolicy.Skip, "SkipUnavailable", VerificationLevel.Standard, 1, "policy", "/windows/system32/robocopy.exe", "1", "2");
}
