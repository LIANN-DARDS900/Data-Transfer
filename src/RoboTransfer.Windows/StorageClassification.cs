using RoboTransfer.Core;
namespace RoboTransfer.Windows;

public sealed record PhysicalDiskEvidence(string? DeviceId, string? Model, string? InterfaceType, string? MediaType, string? PnpDeviceId);
public sealed record StorageClassification(AttachmentType Attachment, StorageBusType BusType, string Explanation);

public static class StorageClassifier
{
    public static StorageClassification Classify(StorageKind logicalKind, PhysicalDiskEvidence? evidence)
    {
        if (logicalKind == StorageKind.Removable) return new(AttachmentType.External, InferBus(evidence), "Windows reports a removable logical drive; bus type is reported only when physical evidence is available.");
        if (evidence is null) return new(AttachmentType.Unknown, StorageBusType.Unknown, "No physical-disk association was available.");
        var bus = InferBus(evidence);
        var searchable = string.Join(' ', evidence.InterfaceType, evidence.MediaType, evidence.PnpDeviceId, evidence.Model).ToUpperInvariant();
        if (bus == StorageBusType.Usb || searchable.Contains("USB", StringComparison.Ordinal)) return new(AttachmentType.External, StorageBusType.Usb, "The associated physical disk reports a USB interface.");
        if (searchable.Contains("EXTERNAL", StringComparison.Ordinal)) return new(AttachmentType.External, bus, "The physical disk identifies itself as externally attached.");
        if (logicalKind == StorageKind.Fixed && bus is StorageBusType.Nvme or StorageBusType.Sata or StorageBusType.Sas) return new(AttachmentType.Internal, bus, "A fixed disk reports an internal storage interface with no external evidence.");
        return new(AttachmentType.Unknown, bus, "Windows did not provide enough evidence to classify attachment safely.");
    }

    private static StorageBusType InferBus(PhysicalDiskEvidence? evidence)
    {
        if (evidence is null) return StorageBusType.Unknown;
        var value = string.Join(' ', evidence.InterfaceType, evidence.PnpDeviceId, evidence.Model).ToUpperInvariant();
        if (value.Contains("USB", StringComparison.Ordinal)) return StorageBusType.Usb;
        if (value.Contains("NVME", StringComparison.Ordinal)) return StorageBusType.Nvme;
        if (value.Contains("SATA", StringComparison.Ordinal) || value.Contains("IDE", StringComparison.Ordinal)) return StorageBusType.Sata;
        if (value.Contains("SAS", StringComparison.Ordinal)) return StorageBusType.Sas;
        if (value.Contains("MMC", StringComparison.Ordinal)) return StorageBusType.Mmc;
        if (value.Contains("SD", StringComparison.Ordinal)) return StorageBusType.Sd;
        if (value.Contains("VIRTUAL", StringComparison.Ordinal)) return StorageBusType.Virtual;
        return StorageBusType.Unknown;
    }
}
