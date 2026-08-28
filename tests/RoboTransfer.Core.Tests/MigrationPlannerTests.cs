using Microsoft.Extensions.Logging.Abstractions;
using RoboTransfer.Core;
using Xunit;
namespace RoboTransfer.Core.Tests;

public sealed class MigrationPlannerTests
{
    private readonly MigrationPlanner planner = new(NullLogger<MigrationPlanner>.Instance);
    [Fact] public void Selects_reachable_approved_network_share_before_external_storage()
    { var plan = planner.CreatePlan(Policy(network: true, external: true, robo: true, paths: [@"\\fileserver\migration"]), Capabilities(shares: [new(@"\\fileserver\migration", CapabilityState.Available, "Accessible")], external: External())); Assert.Equal(MigrationRoute.ConfiguredNetworkShare, plan.Route); Assert.Equal(MigrationStrategy.RobocopyKnownFolders, plan.Strategy); }
    [Fact] public void Selects_external_storage_when_network_is_forbidden()
    { var plan = planner.CreatePlan(Policy(false, true, robo: true), Capabilities(external: External())); Assert.Equal(MigrationRoute.ExternalStorage, plan.Route); }
    [Fact] public void Returns_no_route_when_all_routes_are_forbidden()
    { var plan = planner.CreatePlan(Policy(false, false), Capabilities(external: External())); Assert.Equal(MigrationRoute.NoAvailableRoute, plan.Route); Assert.Equal(MigrationStatus.Blocked, plan.Status); }
    [Fact] public void Never_selects_technically_available_forbidden_network()
    { var plan = planner.CreatePlan(Policy(false, true, robo: true, paths: [@"\\fileserver\migration"]), Capabilities(shares: [new(@"\\fileserver\migration", CapabilityState.Available, "Accessible")], external: External())); Assert.Equal(MigrationRoute.ExternalStorage, plan.Route); Assert.Contains(plan.Reasons, reason => reason.Subject == "Approved network route" && reason.State == CapabilityState.ForbiddenByPolicy); }
    [Fact] public void Prefers_usmt_when_permitted_and_detected()
    { var plan = planner.CreatePlan(Policy(false, true, usmt: true, robo: true), Capabilities(external: External(), usmt: true)); Assert.Equal(MigrationStrategy.Usmt, plan.Strategy); }
    [Fact] public void Rejects_external_storage_with_insufficient_capacity()
    { var plan = planner.CreatePlan(Policy(false, true, robo: true), Capabilities(external: External(1_000)), 2_000); Assert.Equal(MigrationRoute.NoAvailableRoute, plan.Route); }
    [Fact] public void Does_not_treat_unknown_attachment_as_external()
    { var unknown = External() with { Attachment = AttachmentType.Unknown }; var plan = planner.CreatePlan(Policy(false, true, robo: true), Capabilities(external: unknown)); Assert.Equal(MigrationRoute.NoAvailableRoute, plan.Route); }
    [Fact] public void Cloud_uncertainty_blocks_otherwise_ready_plan()
    { var plan = planner.CreatePlan(Policy(false, true, robo: true), Capabilities(external: External()), cloudStateUncertain: true); Assert.Equal(MigrationStatus.Blocked, plan.Status); Assert.Contains(plan.Reasons, reason => reason.Subject == "Cloud content"); }
    [Fact] public void Conservative_policy_is_fail_closed_and_preserving()
    { var policy = PolicyProfile.Conservative; Assert.False(policy.AllowConfiguredNetworkShare); Assert.False(policy.AllowExternalStorage); Assert.Equal(VerificationLevel.Strong, policy.RequiredVerification); Assert.Equal(ConflictPolicy.KeepBoth, policy.DefaultConflictPolicy); }

    private static PolicyProfile Policy(bool network, bool external, bool usmt = false, bool robo = false, IReadOnlyList<string>? paths = null) => new(1, network, external, usmt, robo, VerificationLevel.Strong, ConflictPolicy.KeepBoth, paths ?? []);
    private static StorageVolume External(long free = 10_000) => new("E:\\", "Migration", "NTFS", 20_000, free, StorageKind.Fixed, true, AttachmentType.External, StorageBusType.Usb, "disk1", "USB SSD");
    private static EnvironmentCapabilities Capabilities(IReadOnlyList<NetworkShareCapability>? shares = null, StorageVolume? external = null, bool usmt = false) => new(new("Windows 11", "X64", "device", "technician", false), external is null ? [] : [external], [], new("Robocopy", CapabilityState.Available), new("USMT", usmt ? CapabilityState.Available : CapabilityState.NotAvailable), shares ?? [], []);
}
