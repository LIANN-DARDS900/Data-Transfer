using RoboTransfer.Core;
namespace RoboTransfer.Windows;

public sealed class WindowsCloudPlaceholderDetector : ICloudPlaceholderDetector
{
    internal const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    internal const FileAttributes Pinned = (FileAttributes)0x00080000;
    internal const FileAttributes Unpinned = (FileAttributes)0x00100000;
    internal const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    public CloudContentState Detect(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return CloudContentState.Unknown;
        try { return Classify(File.GetAttributes(path)); }
        catch (UnauthorizedAccessException) { return CloudContentState.Unknown; }
        catch (IOException) { return CloudContentState.Unavailable; }
    }

    public static CloudContentState Classify(FileAttributes attributes)
    {
        if (attributes.HasFlag(Unpinned) || attributes.HasFlag(RecallOnDataAccess)) return CloudContentState.OnlineOnly;
        if (attributes.HasFlag(Pinned) && !attributes.HasFlag(FileAttributes.Offline)) return CloudContentState.Pinned;
        if (attributes.HasFlag(FileAttributes.Offline) || attributes.HasFlag(RecallOnOpen)) return CloudContentState.PartiallyAvailable;
        return CloudContentState.LocallyAvailable;
    }
}
