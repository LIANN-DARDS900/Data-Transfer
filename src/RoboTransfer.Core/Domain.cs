namespace RoboTransfer.Core;

public enum MigrationRole { OldPc, NewPc }
public enum MigrationRoute { ConfiguredNetworkShare, ExternalStorage, NoAvailableRoute }
public enum MigrationStrategy { RobocopyKnownFolders, Usmt, ManualApprovalRequired }
public enum MigrationStatus { Draft, Analyzing, Ready, Blocked, Completed, Failed }
public enum CapabilityState { Available, NotAvailable, NotConfigured, ForbiddenByPolicy, RequiresApproval, Unknown }
public enum KnownFolderKind { Desktop, Documents, Downloads, Pictures, Videos, Music, Favorites }
public enum StorageKind { Fixed, Removable, Network, Optical, Ram, Unknown }
public enum TransferState { Pending, Transferred, Skipped, Failed }
public enum VerificationState { NotVerified, Verified, Failed, Unknown }
public enum CloudContentState { LocallyAvailable, OnlineOnly, Unknown }

public sealed record MigrationSource(string MachineName, string? ProfileId);
public sealed record MigrationDestination(MigrationRoute Route, string Location, long? AvailableBytes);
public sealed record MigrationSession(Guid Id, MigrationRole Role, MigrationStatus Status, DateTimeOffset CreatedAt, MigrationSource? Source = null, MigrationDestination? Destination = null);
public sealed record KnownFolder(KnownFolderKind Kind, string Path, bool Exists);
public sealed record UserProfile(string Id, string DisplayName, string ProfilePath, bool IsLoaded, bool IsSpecial, IReadOnlyList<KnownFolder> KnownFolders);
public sealed record StorageVolume(string RootPath, string? Label, string? FileSystem, long TotalBytes, long AvailableBytes, StorageKind Kind, bool IsReady, string? BusType = null);
public sealed record ToolCapability(string Name, CapabilityState State, string? ExecutablePath = null, string? Version = null, string? Detail = null);
public sealed record MigrationCapability(string Name, CapabilityState State, string Explanation);
public sealed record OperatingSystemInfo(string Description, string Architecture, string MachineName, string CurrentUser, bool? IsElevated);
public sealed record NetworkShareCapability(string Path, CapabilityState State, string Explanation);
public sealed record EnvironmentCapabilities(OperatingSystemInfo OperatingSystem, IReadOnlyList<StorageVolume> Volumes, IReadOnlyList<UserProfile> UserProfiles, ToolCapability Robocopy, ToolCapability Usmt, IReadOnlyList<NetworkShareCapability> ApprovedShares, IReadOnlyList<string> Warnings);

public sealed record PolicyProfile(bool AllowConfiguredNetworkShare, bool AllowExternalStorage, bool AllowUsmt, bool AllowRobocopy, bool StrongVerificationRequired, IReadOnlyList<string> ApprovedNetworkSharePaths)
{
    public static PolicyProfile Conservative { get; } = new(false, false, false, false, true, Array.Empty<string>());
}

public sealed record PlanReason(string Subject, CapabilityState State, string Explanation);
public sealed record MigrationPlan(MigrationRoute Route, MigrationStrategy Strategy, MigrationStatus Status, IReadOnlyList<PlanReason> Reasons, MigrationDestination? Destination)
{
    public string Recommendation => Route == MigrationRoute.NoAvailableRoute ? "No approved migration route is currently available." : $"{Route} + {Strategy}";
}

public sealed record MigrationManifestEntry(string RelativePath, long FileSize, DateTimeOffset LastWriteTime, KnownFolderKind SourceKnownFolder, TransferState TransferState, VerificationState VerificationState, CloudContentState CloudState);
public sealed record MigrationManifest(Guid SessionId, DateTimeOffset CreatedAt, IReadOnlyList<MigrationManifestEntry> Entries, IReadOnlyList<string> Warnings);
