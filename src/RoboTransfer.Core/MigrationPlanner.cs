using Microsoft.Extensions.Logging;
namespace RoboTransfer.Core;

public sealed class MigrationPlanner(ILogger<MigrationPlanner> logger) : IMigrationPlanner
{
    public MigrationPlan CreatePlan(PolicyProfile policy, EnvironmentCapabilities capabilities, long? requiredBytes = null, bool cloudStateUncertain = false)
    {
        ArgumentNullException.ThrowIfNull(policy); ArgumentNullException.ThrowIfNull(capabilities);
        var reasons = new List<PlanReason>();
        var approved = new HashSet<string>(policy.ApprovedNetworkSharePaths, StringComparer.OrdinalIgnoreCase);
        var share = capabilities.ApprovedShares.FirstOrDefault(candidate => candidate.State == CapabilityState.Available && approved.Contains(candidate.Path));
        EvaluateNetwork(policy, share, reasons);

        var external = capabilities.ExternalCandidates.Where(volume => requiredBytes is null || volume.AvailableBytes >= requiredBytes.Value).OrderByDescending(volume => volume.AvailableBytes).FirstOrDefault();
        EvaluateExternal(policy, external, requiredBytes, reasons);

        MigrationRoute route; MigrationDestination? destination;
        if (policy.AllowConfiguredNetworkShare && share is not null) { route = MigrationRoute.ConfiguredNetworkShare; destination = new(route, share.Path, null); }
        else if (policy.AllowExternalStorage && external is not null) { route = MigrationRoute.ExternalStorage; destination = new(route, external.RootPath, external.AvailableBytes); }
        else { route = MigrationRoute.NoAvailableRoute; destination = null; }

        var strategy = SelectStrategy(policy, capabilities, route, reasons);
        if (cloudStateUncertain) reasons.Add(new("Cloud content", CapabilityState.RequiresApproval, "One or more files have unknown or online-only cloud state. Content availability must be reviewed before preparation."));
        var ready = route != MigrationRoute.NoAvailableRoute && strategy != MigrationStrategy.ManualApprovalRequired && !cloudStateUncertain;
        var status = ready ? MigrationStatus.Ready : MigrationStatus.Blocked;
        logger.LogInformation("Plan evaluated: Route={Route}, Strategy={Strategy}, Status={Status}, Verification={Verification}", route, strategy, status, policy.RequiredVerification);
        return new(route, strategy, status, policy.RequiredVerification, policy.DefaultConflictPolicy, reasons, destination);
    }

    private static void EvaluateNetwork(PolicyProfile policy, NetworkShareCapability? share, ICollection<PlanReason> reasons)
    {
        if (!policy.AllowConfiguredNetworkShare) reasons.Add(new("Approved network route", CapabilityState.ForbiddenByPolicy, "Network-share migration is disabled by enterprise policy."));
        else if (policy.ApprovedNetworkSharePaths.Count == 0) reasons.Add(new("Approved network route", CapabilityState.NotConfigured, "No approved UNC destination is configured."));
        else if (share is null) reasons.Add(new("Approved network route", CapabilityState.NotAvailable, "Configured destinations are not currently accessible."));
        else reasons.Add(new("Approved network route", CapabilityState.Available, "An explicitly approved destination is accessible."));
    }

    private static void EvaluateExternal(PolicyProfile policy, StorageVolume? external, long? requiredBytes, ICollection<PlanReason> reasons)
    {
        if (!policy.AllowExternalStorage) reasons.Add(new("External migration media", CapabilityState.ForbiddenByPolicy, "External storage is disabled by enterprise policy."));
        else if (external is null) reasons.Add(new("External migration media", CapabilityState.NotAvailable, requiredBytes.HasValue ? "No externally attached volume has sufficient confirmed capacity." : "No externally attached volume was detected with sufficient classification confidence."));
        else reasons.Add(new("External migration media", CapabilityState.Available, $"{external.BusType} media has {external.AvailableBytes:N0} bytes available."));
    }

    private static MigrationStrategy SelectStrategy(PolicyProfile policy, EnvironmentCapabilities capabilities, MigrationRoute route, ICollection<PlanReason> reasons)
    {
        if (route == MigrationRoute.NoAvailableRoute) return MigrationStrategy.ManualApprovalRequired;
        if (policy.AllowUsmt && capabilities.Usmt.State == CapabilityState.Available) { reasons.Add(new("USMT", CapabilityState.Available, "USMT is installed and permitted.")); return MigrationStrategy.Usmt; }
        if (!policy.AllowUsmt) reasons.Add(new("USMT", CapabilityState.ForbiddenByPolicy, "USMT is disabled by enterprise policy."));
        if (policy.AllowRobocopy && capabilities.Robocopy.State == CapabilityState.Available) { reasons.Add(new("Robocopy", CapabilityState.Available, "Robocopy Known Folders is installed and permitted.")); return MigrationStrategy.RobocopyKnownFolders; }
        reasons.Add(new("Migration tool", policy.AllowRobocopy ? CapabilityState.NotAvailable : CapabilityState.ForbiddenByPolicy, policy.AllowRobocopy ? "Robocopy was not detected." : "Robocopy is disabled by enterprise policy."));
        return MigrationStrategy.ManualApprovalRequired;
    }
}
