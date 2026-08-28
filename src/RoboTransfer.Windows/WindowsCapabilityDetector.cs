using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using RoboTransfer.Core;
using RoboTransfer.Usmt;
namespace RoboTransfer.Windows;

public sealed class ApprovedNetworkShareDetector
{
    public async Task<IReadOnlyList<NetworkShareCapability>> DetectAsync(PolicyProfile policy, CancellationToken cancellationToken)
    {
        if (!policy.AllowConfiguredNetworkShare) return policy.ApprovedNetworkSharePaths.Select(path => new NetworkShareCapability(path, CapabilityState.ForbiddenByPolicy, "Network shares are forbidden by policy; no access check was attempted.")).ToArray();
        var results = new List<NetworkShareCapability>();
        foreach (var path in policy.ApprovedNetworkSharePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || !uri.IsUnc) { results.Add(new(path, CapabilityState.NotConfigured, "The configured location is not a valid absolute UNC path.")); continue; }
            try
            {
                var accessible = await Task.Run(() => Directory.Exists(path), cancellationToken).ConfigureAwait(false);
                results.Add(new(path, accessible ? CapabilityState.Available : CapabilityState.NotAvailable, accessible ? "The configured location is accessible." : "The configured location is not accessible with the current identity."));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { results.Add(new(path, CapabilityState.NotAvailable, $"Access check failed ({ex.GetType().Name}).")); }
        }
        return results;
    }
}

public sealed class WindowsCapabilityDetector(IStorageDetector storage, IUserProfileDetector profiles, IToolDetector robocopy, UsmtToolDetector usmt, ApprovedNetworkShareDetector shares, ILogger<WindowsCapabilityDetector> logger) : ICapabilityDetector
{
    public async Task<EnvironmentCapabilities> DetectAsync(PolicyProfile policy, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        if (!OperatingSystem.IsWindows()) warnings.Add("Full endpoint analysis requires Windows 11.");
        var os = new OperatingSystemInfo(RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(), Environment.MachineName, Environment.UserName, GetElevation());
        var storageTask = storage.DetectAsync(cancellationToken); var profileTask = profiles.DetectAsync(cancellationToken); var roboTask = robocopy.DetectAsync(cancellationToken); var usmtTask = usmt.DetectAsync(cancellationToken); var shareTask = shares.DetectAsync(policy, cancellationToken);
        await Task.WhenAll(storageTask, profileTask, roboTask, usmtTask, shareTask).ConfigureAwait(false);
        logger.LogInformation("Endpoint analysis completed. Volumes={VolumeCount}; Profiles={ProfileCount}; Warnings={WarningCount}", storageTask.Result.Count, profileTask.Result.Count, warnings.Count);
        return new(os, storageTask.Result, profileTask.Result, roboTask.Result, usmtTask.Result, shareTask.Result, warnings);
    }
    private static bool? GetElevation()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
