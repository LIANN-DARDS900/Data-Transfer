using System.Management;
using Microsoft.Extensions.Logging;
using RoboTransfer.Core;
namespace RoboTransfer.Windows;

public sealed class WindowsStorageDetector(ILogger<WindowsStorageDetector> logger) : IStorageDetector
{
    public Task<IReadOnlyList<StorageVolume>> DetectAsync(CancellationToken cancellationToken = default) => Task.Run(() => Detect(cancellationToken), cancellationToken);

    private IReadOnlyList<StorageVolume> Detect(CancellationToken cancellationToken)
    {
        var volumes = new List<StorageVolume>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var kind = Map(drive.DriveType); var evidence = OperatingSystem.IsWindows() ? FindPhysicalDisk(drive.Name[..2]) : null;
                var classification = StorageClassifier.Classify(kind, evidence);
                volumes.Add(drive.IsReady
                    ? new(drive.Name, Safe(() => drive.VolumeLabel), Safe(() => drive.DriveFormat), drive.TotalSize, drive.AvailableFreeSpace, kind, true, classification.Attachment, classification.BusType, evidence?.DeviceId, evidence?.Model)
                    : new(drive.Name, null, null, 0, 0, kind, false, classification.Attachment, classification.BusType, evidence?.DeviceId, evidence?.Model));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ManagementException)
            { logger.LogWarning("Storage inspection skipped one volume. ErrorCategory={ErrorCategory}; ExceptionType={ExceptionType}", ErrorCategory.AccessDenied, ex.GetType().Name); }
        }
        return volumes;
    }

    private PhysicalDiskEvidence? FindPhysicalDisk(string logicalDeviceId)
    {
        try
        {
            var escaped = logicalDeviceId.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);
            using var partitions = new ManagementObjectSearcher($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{escaped}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (ManagementObject partition in partitions.Get())
            {
                using (partition)
                using (var disks = new ManagementObjectSearcher($"ASSOCIATORS OF {{{partition.Path.RelativePath}}} WHERE AssocClass=Win32_DiskDriveToDiskPartition"))
                foreach (ManagementObject disk in disks.Get())
                {
                    using (disk) return new(Convert.ToString(disk["DeviceID"]), Convert.ToString(disk["Model"]), Convert.ToString(disk["InterfaceType"]), Convert.ToString(disk["MediaType"]), Convert.ToString(disk["PNPDeviceID"]));
                }
            }
        }
        catch (ManagementException ex) { logger.LogWarning("Physical disk metadata is unavailable. ExceptionType={ExceptionType}", ex.GetType().Name); }
        return null;
    }

    private static string? Safe(Func<string> read) { try { return read(); } catch (IOException) { return null; } }
    private static StorageKind Map(DriveType type) => type switch { DriveType.Fixed => StorageKind.Fixed, DriveType.Removable => StorageKind.Removable, DriveType.Network => StorageKind.Network, DriveType.CDRom => StorageKind.Optical, DriveType.Ram => StorageKind.Ram, _ => StorageKind.Unknown };
}
