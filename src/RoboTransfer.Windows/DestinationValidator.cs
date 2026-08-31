using RoboTransfer.Core;

namespace RoboTransfer.Windows;

public sealed class DestinationValidator(IDestinationWriteProbe? writeProbe = null) : IDestinationValidator
{
    public async Task<DestinationValidationResult> ValidateAsync(DestinationValidationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var errors = new List<OperationError>();
        var probeService = writeProbe ?? new DestinationWriteProbe();
        var destination = Canonical(context.Plan.DestinationPath);
        if (context.Plan.PolicyFingerprint != PolicyFingerprint.Create(context.Policy)) Add(ErrorCategory.PolicyForbidden, "Policy changed after review.", "Review and prepare a new plan.");
        foreach (var source in context.SourceRoots.Select(Canonical))
        {
            if (Same(source, destination) || Within(source, destination) || Within(destination, source)) Add(ErrorCategory.InvalidPath, "Source and destination overlap.", "Choose an independent destination.");
        }
        if (IsProtected(destination)) Add(ErrorCategory.PolicyForbidden, "The destination is a protected Windows or application path.", "Choose approved migration storage.");
        if (context.Plan.Route == MigrationRoute.ConfiguredNetworkShare && (!context.Policy.AllowConfiguredNetworkShare || !context.Policy.ApprovedNetworkSharePaths.Any(p => Within(Canonical(p), destination) || Same(Canonical(p), destination)))) Add(ErrorCategory.PolicyForbidden, "The network destination is not explicitly approved.", "Use a share listed in policy.");
        if (context.Plan.Route == MigrationRoute.ExternalStorage)
        {
            var volume = context.CurrentVolume;
            if (volume is null || !volume.IsReady) Add(ErrorCategory.StorageDisconnected, "Migration media is not connected and ready.", "Reconnect the reviewed device.");
            else
            {
                if (!volume.IsExternalCandidate || !context.Policy.AllowExternalStorage) Add(ErrorCategory.PolicyForbidden, "The attached volume is not permitted external media.", "Choose policy-approved external media.");
                if (!string.Equals(volume.PhysicalDiskId, context.Plan.DestinationIdentity, StringComparison.OrdinalIgnoreCase)) Add(ErrorCategory.DestinationChanged, "The destination device changed after review.", "Reconnect the reviewed device and prepare again.");
                if (string.IsNullOrWhiteSpace(volume.FileSystem)) Add(ErrorCategory.ConfigurationInvalid, "Destination filesystem is unknown.", "Use a recognized writable filesystem.");
                if (volume.AvailableBytes < context.Plan.ExpectedBytes) Add(ErrorCategory.InsufficientSpace, "Destination capacity is insufficient.", "Free space or choose a larger device.");
            }
        }
        if (errors.Count == 0 && !await probeService.IsWritableAsync(destination, context.RequireExistingDestination, cancellationToken)) Add(context.RequireExistingDestination && !Directory.Exists(destination) ? ErrorCategory.StorageDisconnected : ErrorCategory.AccessDenied, "Destination is unavailable or not writable.", "Reconnect the reviewed destination and verify access without changing endpoint security.");
        return new(errors.Count == 0, errors);
        void Add(ErrorCategory category, string what, string action) => errors.Add(new(category, $"WHAT HAPPENED: {what} WHAT TO DO: {action}"));
    }
    private static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool Same(string a, string b) => string.Equals(a, b, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool Within(string root, string candidate) => candidate.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    private static bool IsProtected(string path)
    {
        var protectedRoots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), AppContext.BaseDirectory }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(Canonical);
        return protectedRoots.Any(root => Same(root, path) || Within(root, path));
    }
}

public sealed class DestinationWriteProbe : IDestinationWriteProbe
{
    public async Task<bool> IsWritableAsync(string path, bool requireExisting, CancellationToken cancellationToken = default)
    {
        string? probe = null;
        try { if (requireExisting && !Directory.Exists(path)) return false; Directory.CreateDirectory(path); probe = Path.Combine(path, $".robotransfer-write-{Guid.NewGuid():N}.tmp"); await File.WriteAllBytesAsync(probe, [], cancellationToken); File.Delete(probe); return true; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { return false; }
        finally { if (probe is not null && File.Exists(probe)) try { File.Delete(probe); } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) { } }
    }
}
