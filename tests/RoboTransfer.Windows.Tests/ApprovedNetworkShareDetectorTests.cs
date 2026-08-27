using Xunit;
using RoboTransfer.Core;
using RoboTransfer.Windows;
namespace RoboTransfer.Windows.Tests;
public sealed class ApprovedNetworkShareDetectorTests
{
    [Fact] public void Does_not_probe_paths_when_policy_forbids_network_shares()
    { var policy = new PolicyProfile(false, false, false, false, true, [@"\\invalid\never-probe"]); var result = new ApprovedNetworkShareDetector().Detect(policy); Assert.Single(result); Assert.Equal(CapabilityState.ForbiddenByPolicy, result[0].State); }
    [Fact] public void Rejects_non_unc_configured_path()
    { var policy = new PolicyProfile(true, false, false, false, true, ["C:\\not-a-share"]); var result = new ApprovedNetworkShareDetector().Detect(policy); Assert.Equal(CapabilityState.NotConfigured, result[0].State); }
}
