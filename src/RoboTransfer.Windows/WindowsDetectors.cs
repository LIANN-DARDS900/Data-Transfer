using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Extensions.Logging;
using RoboTransfer.Core;

namespace RoboTransfer.Windows;

public sealed class WindowsStorageDetector(ILogger<WindowsStorageDetector> logger) : IStorageDetector
{
    public Task<IReadOnlyList<StorageVolume>> DetectAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<StorageVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady) { result.Add(new(drive.Name, null, null, 0, 0, Map(drive.DriveType), false)); continue; }
                result.Add(new(drive.Name, drive.VolumeLabel, drive.DriveFormat, drive.TotalSize, drive.AvailableFreeSpace, Map(drive.DriveType), true));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { logger.LogWarning("A storage volume could not be inspected: {ExceptionType}", ex.GetType().Name); }
        }
        return Task.FromResult<IReadOnlyList<StorageVolume>>(result);
    }
    private static StorageKind Map(DriveType type) => type switch { DriveType.Fixed => StorageKind.Fixed, DriveType.Removable => StorageKind.Removable, DriveType.Network => StorageKind.Network, DriveType.CDRom => StorageKind.Optical, DriveType.Ram => StorageKind.Ram, _ => StorageKind.Unknown };
}

public sealed class WindowsUserProfileDetector(ILogger<WindowsUserProfileDetector> logger) : IUserProfileDetector
{
    public Task<IReadOnlyList<UserProfile>> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows()) return Task.FromResult<IReadOnlyList<UserProfile>>(Array.Empty<UserProfile>());
        var root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var parent = Directory.GetParent(root)?.FullName;
        if (parent is null) return Task.FromResult<IReadOnlyList<UserProfile>>(Array.Empty<UserProfile>());
        var profiles = new List<UserProfile>();
        try
        {
            foreach (var path in Directory.EnumerateDirectories(parent))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var name = Path.GetFileName(path);
                    var special = name.Equals("Public", StringComparison.OrdinalIgnoreCase) || name.Equals("Default", StringComparison.OrdinalIgnoreCase);
                    if (special) continue;
                    profiles.Add(new(name, name, path, path.Equals(root, StringComparison.OrdinalIgnoreCase), false, KnownFolders(path)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning("A user profile could not be inspected: {ExceptionType}", ex.GetType().Name); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning("User profile enumeration was incomplete: {ExceptionType}", ex.GetType().Name); }
        return Task.FromResult<IReadOnlyList<UserProfile>>(profiles);
    }
    private static IReadOnlyList<KnownFolder> KnownFolders(string root) => Enum.GetValues<KnownFolderKind>().Select(kind => { var path = Path.Combine(root, kind.ToString()); return new KnownFolder(kind, path, Directory.Exists(path)); }).ToArray();
}

public sealed class ApprovedNetworkShareDetector
{
    public IReadOnlyList<NetworkShareCapability> Detect(PolicyProfile policy)
    {
        if (!policy.AllowConfiguredNetworkShare)
            return policy.ApprovedNetworkSharePaths.Select(x => new NetworkShareCapability(x, CapabilityState.ForbiddenByPolicy, "Configured network shares are forbidden by policy.")).ToArray();
        return policy.ApprovedNetworkSharePaths.Select(path =>
        {
            if (!path.StartsWith("\\\\", StringComparison.Ordinal)) return new(path, CapabilityState.NotConfigured, "The approved path is not a UNC path.");
            try { return Directory.Exists(path) ? new(path, CapabilityState.Available, "The configured path is accessible.") : new(path, CapabilityState.NotAvailable, "The configured path is not accessible."); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(path, CapabilityState.NotAvailable, $"Access check failed ({ex.GetType().Name})."); }
        }).ToArray();
    }
}

public sealed class WindowsCapabilityDetector(IStorageDetector storage, IUserProfileDetector profiles, IToolDetector robocopy, UsmtToolDetector usmt, ApprovedNetworkShareDetector shares, ILogger<WindowsCapabilityDetector> logger) : ICapabilityDetector
{
    public async Task<EnvironmentCapabilities> DetectAsync(PolicyProfile policy, CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        if (!OperatingSystem.IsWindows()) warnings.Add("Windows capability detection requires a Windows runtime.");
        var os = new OperatingSystemInfo(RuntimeInformation.OSDescription, RuntimeInformation.OSArchitecture.ToString(), Environment.MachineName, Environment.UserName, GetElevation());
        var storageTask = storage.DetectAsync(cancellationToken); var profileTask = profiles.DetectAsync(cancellationToken); var roboTask = robocopy.DetectAsync(cancellationToken); var usmtTask = usmt.DetectAsync(cancellationToken);
        await Task.WhenAll(storageTask, profileTask, roboTask, usmtTask).ConfigureAwait(false);
        logger.LogInformation("Environment analysis completed with {VolumeCount} volumes and {ProfileCount} selectable profiles", storageTask.Result.Count, profileTask.Result.Count);
        return new(os, storageTask.Result, profileTask.Result, roboTask.Result, usmtTask.Result, shares.Detect(policy), warnings);
    }
    private static bool? GetElevation()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
