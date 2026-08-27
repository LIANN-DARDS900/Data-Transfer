using Microsoft.Extensions.Logging;

namespace RoboTransfer.Core;

public sealed class MigrationPlanner(ILogger<MigrationPlanner> logger) : IMigrationPlanner
{
    public MigrationPlan CreatePlan(PolicyProfile policy, EnvironmentCapabilities capabilities, long? requiredBytes = null)
    {
        var reasons = new List<PlanReason>();
        var reachableShare = capabilities.ApprovedShares.FirstOrDefault(x => x.State == CapabilityState.Available && policy.ApprovedNetworkSharePaths.Contains(x.Path, StringComparer.OrdinalIgnoreCase));
        MigrationDestination? destination = null;
        MigrationRoute route;

        if (!policy.AllowConfiguredNetworkShare)
            reasons.Add(new("Configured network share", CapabilityState.ForbiddenByPolicy, "Configured network shares are forbidden by policy."));
        else if (policy.ApprovedNetworkSharePaths.Count == 0)
            reasons.Add(new("Configured network share", CapabilityState.NotConfigured, "No approved UNC destination is configured."));
        else if (reachableShare is null)
            reasons.Add(new("Configured network share", CapabilityState.NotAvailable, "No configured approved share is currently accessible."));
        else
            reasons.Add(new("Configured network share", CapabilityState.Available, "An explicitly approved share is accessible."));

        var external = capabilities.Volumes.Where(x => x.IsReady && x.Kind == StorageKind.Removable)
            .Where(x => requiredBytes is null || x.AvailableBytes >= requiredBytes).OrderByDescending(x => x.AvailableBytes).FirstOrDefault();
        if (!policy.AllowExternalStorage)
            reasons.Add(new("External storage", CapabilityState.ForbiddenByPolicy, "External storage is forbidden by policy."));
        else if (external is null)
            reasons.Add(new("External storage", CapabilityState.NotAvailable, requiredBytes is null ? "No removable storage is detected." : "No removable storage has sufficient free capacity."));
        else
            reasons.Add(new("External storage", CapabilityState.Available, $"A removable volume with {external.AvailableBytes} bytes free is detected."));

        if (policy.AllowConfiguredNetworkShare && reachableShare is not null)
        { route = MigrationRoute.ConfiguredNetworkShare; destination = new(route, reachableShare.Path, null); }
        else if (policy.AllowExternalStorage && external is not null)
        { route = MigrationRoute.ExternalStorage; destination = new(route, external.RootPath, external.AvailableBytes); }
        else
        { route = MigrationRoute.NoAvailableRoute; }

        MigrationStrategy strategy;
        if (route == MigrationRoute.NoAvailableRoute) strategy = MigrationStrategy.ManualApprovalRequired;
        else if (policy.AllowUsmt && capabilities.Usmt.State == CapabilityState.Available)
        { strategy = MigrationStrategy.Usmt; reasons.Add(new("USMT", CapabilityState.Available, "Policy permits USMT and both approved executables were detected.")); }
        else if (policy.AllowRobocopy && capabilities.Robocopy.State == CapabilityState.Available)
        { strategy = MigrationStrategy.RobocopyKnownFolders; reasons.Add(new("Robocopy", CapabilityState.Available, "Policy permits Robocopy and the signed-in Windows installation provides it.")); }
        else
        { strategy = MigrationStrategy.ManualApprovalRequired; reasons.Add(new("Migration tool", CapabilityState.RequiresApproval, "No detected migration tool is both available and permitted.")); }

        var status = route != MigrationRoute.NoAvailableRoute && strategy != MigrationStrategy.ManualApprovalRequired ? MigrationStatus.Ready : MigrationStatus.Blocked;
        logger.LogInformation("Migration plan evaluated with route {Route}, strategy {Strategy}, and status {Status}", route, strategy, status);
        return new(route, strategy, status, reasons, destination);
    }
}
