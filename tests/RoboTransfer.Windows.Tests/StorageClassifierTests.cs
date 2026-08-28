using RoboTransfer.Core;
using RoboTransfer.Windows;
using Xunit;
namespace RoboTransfer.Windows.Tests;
public sealed class StorageClassifierTests
{
    [Fact] public void Fixed_usb_ssd_is_external() { var result = StorageClassifier.Classify(StorageKind.Fixed, new("disk1", "Portable SSD", "USB", "Fixed hard disk media", "USBSTOR\\DISK")); Assert.Equal(AttachmentType.External, result.Attachment); Assert.Equal(StorageBusType.Usb, result.BusType); }
    [Fact] public void Fixed_internal_nvme_is_internal() { var result = StorageClassifier.Classify(StorageKind.Fixed, new("disk0", "NVMe Drive", "NVMe", "Fixed hard disk media", "SCSI\\DISK")); Assert.Equal(AttachmentType.Internal, result.Attachment); Assert.Equal(StorageBusType.Nvme, result.BusType); }
    [Fact] public void Missing_physical_evidence_is_unknown() { var result = StorageClassifier.Classify(StorageKind.Fixed, null); Assert.Equal(AttachmentType.Unknown, result.Attachment); Assert.Equal(StorageBusType.Unknown, result.BusType); }
    [Fact] public void Removable_logical_drive_is_external_but_bus_remains_unknown_without_evidence() { var result = StorageClassifier.Classify(StorageKind.Removable, null); Assert.Equal(AttachmentType.External, result.Attachment); Assert.Equal(StorageBusType.Unknown, result.BusType); }
}
