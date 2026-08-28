using RoboTransfer.Core;
using RoboTransfer.Windows;
using Xunit;
namespace RoboTransfer.Windows.Tests;
public sealed class ApprovedNetworkShareDetectorTests
{
    [Fact] public async Task Forbidden_policy_does_not_probe_configured_path() { var policy = new PolicyProfile(1, false, false, false, false, VerificationLevel.Strong, ConflictPolicy.KeepBoth, [@"\\unreachable.invalid\migration"]); var result = await new ApprovedNetworkShareDetector().DetectAsync(policy, TestContext.Current.CancellationToken); Assert.Equal(CapabilityState.ForbiddenByPolicy, Assert.Single(result).State); }
    [Fact] public async Task Non_unc_path_is_rejected_before_access_check() { var policy = new PolicyProfile(1, true, false, false, false, VerificationLevel.Strong, ConflictPolicy.KeepBoth, ["C:\\not-approved"]); var result = await new ApprovedNetworkShareDetector().DetectAsync(policy, TestContext.Current.CancellationToken); Assert.Equal(CapabilityState.NotConfigured, Assert.Single(result).State); }
    [Fact] public async Task Cancellation_is_observed() { using var cancellation = new CancellationTokenSource(); cancellation.Cancel(); var policy = new PolicyProfile(1, true, false, false, false, VerificationLevel.Strong, ConflictPolicy.KeepBoth, [@"\\fileserver\migration"]); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ApprovedNetworkShareDetector().DetectAsync(policy, cancellation.Token)); }
}
