using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using RoboTransfer.Core;
namespace RoboTransfer.Core.Tests;
public sealed class MigrationPlannerTests
{
    private readonly MigrationPlanner planner = new(NullLogger<MigrationPlanner>.Instance);
    [Fact] public void Selects_reachable_approved_network_share_before_external_storage()
    { var policy = Policy(network: true, external: true, robo: true, paths: [@"\\server\migration"]); var plan = planner.CreatePlan(policy, Caps(shares: [new(@"\\server\migration", CapabilityState.Available, "reachable")], removable: true)); Assert.Equal(MigrationRoute.ConfiguredNetworkShare, plan.Route); Assert.Equal(MigrationStrategy.RobocopyKnownFolders, plan.Strategy); }
    [Fact] public void Selects_external_storage_when_network_is_forbidden()
    { var plan = planner.CreatePlan(Policy(false, true, robo: true), Caps(removable: true)); Assert.Equal(MigrationRoute.ExternalStorage, plan.Route); }
    [Fact] public void Returns_no_route_when_all_routes_are_forbidden()
    { var plan = planner.CreatePlan(Policy(false, false), Caps(removable: true)); Assert.Equal(MigrationRoute.NoAvailableRoute, plan.Route); Assert.Equal(MigrationStatus.Blocked, plan.Status); }
    [Fact] public void Never_selects_technically_available_forbidden_network()
    { var policy = Policy(false, true, robo: true, paths: [@"\\server\migration"]); var plan = planner.CreatePlan(policy, Caps(shares: [new(@"\\server\migration", CapabilityState.Available, "reachable")], removable: true)); Assert.Equal(MigrationRoute.ExternalStorage, plan.Route); Assert.Contains(plan.Reasons, x => x.Subject == "Configured network share" && x.State == CapabilityState.ForbiddenByPolicy); }
    [Fact] public void Prefers_usmt_strategy_when_permitted_and_available()
    { var plan = planner.CreatePlan(Policy(false, true, usmt: true, robo: true), Caps(removable: true, usmt: true)); Assert.Equal(MigrationStrategy.Usmt, plan.Strategy); }
    [Fact] public void Rejects_volume_without_required_capacity()
    { var plan = planner.CreatePlan(Policy(false, true, robo: true), Caps(removable: true), 2_000); Assert.Equal(MigrationRoute.NoAvailableRoute, plan.Route); }
    [Fact] public void Conservative_policy_forbids_every_mechanism()
    { Assert.False(PolicyProfile.Conservative.AllowConfiguredNetworkShare); Assert.False(PolicyProfile.Conservative.AllowExternalStorage); Assert.True(PolicyProfile.Conservative.StrongVerificationRequired); }
    private static PolicyProfile Policy(bool network, bool external, bool usmt = false, bool robo = false, IReadOnlyList<string>? paths = null) => new(network, external, usmt, robo, true, paths ?? []);
    private static EnvironmentCapabilities Caps(IReadOnlyList<NetworkShareCapability>? shares = null, bool removable = false, bool usmt = false) => new(new("Windows", "X64", "machine", "user", false), removable ? [new("E:\\", "USB", "NTFS", 10_000, 1_000, StorageKind.Removable, true)] : [], [], new("Robocopy", CapabilityState.Available), new("USMT", usmt ? CapabilityState.Available : CapabilityState.NotAvailable), shares ?? [], []);
}
