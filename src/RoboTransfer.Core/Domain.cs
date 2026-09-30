namespace RoboTransfer.Core;

public enum MigrationRole { OldPc, NewPc }
public enum MigrationRoute { ConfiguredNetworkShare, ExternalStorage, NoAvailableRoute }
public enum MigrationStrategy { RobocopyKnownFolders, Usmt, ManualApprovalRequired }
public enum MigrationStatus { Draft, Analyzing, Ready, Blocked, Prepared, Transferring, Interrupted, Verifying, Completed, Failed, Abandoned }
public enum CapabilityState { Available, NotAvailable, NotConfigured, ForbiddenByPolicy, RequiresApproval, Unknown }
public enum KnownFolderKind { Desktop, Documents, Downloads, Pictures, Videos, Music, Favorites }
public enum KnownFolderResolution { Resolved, ConventionalPath, Unresolved }
public enum StorageKind { Fixed, Removable, Network, Optical, Ram, Unknown }
public enum AttachmentType { Internal, External, Unknown }
public enum StorageBusType { Usb, Nvme, Sata, Sas, Sd, Mmc, Virtual, FileBackedVirtual, Unknown }
public enum TransferState { Pending, InProgress, Transferred, Skipped, Failed }
public enum VerificationState { NotVerified, StandardVerified, StrongVerified, Failed, Unknown }
public enum VerificationLevel { Standard, Strong }
public enum CloudContentState { LocallyAvailable, Pinned, OnlineOnly, PartiallyAvailable, Unavailable, Unknown }
public enum ConflictPolicy { Skip, ReplaceIfSourceNewer, KeepBoth, Replace, ManualDecision }
public enum ErrorCategory { AccessDenied, FileLocked, InvalidPath, InsufficientSpace, StorageDisconnected, DestinationChanged, ToolUnavailable, PolicyForbidden, CloudContentUnavailable, PathTooLong, DestinationConflict, VerificationMismatch, VerificationFailed, ProcessFailure, ConfigurationInvalid, Cancelled, Unknown }
public enum ProfileClassification { InteractiveUser, Special, Service, Temporary, Stale, Unknown }
public enum ExecutableTrustStatus { Trusted, NotTrusted, Unavailable, InvalidLocation, InvalidIdentity }
public sealed record ExecutableTrustResult(ExecutableTrustStatus Status, string? CanonicalPath, string? Version, string? Publisher, string Explanation)
{
    public bool IsAuthorized => Status == ExecutableTrustStatus.Trusted;
}

public sealed record MigrationSource(string MachineName, string? ProfileId);
public sealed record MigrationDestination(MigrationRoute Route, string Location, long? AvailableBytes);
public sealed record MigrationSession(Guid Id, MigrationRole Role, MigrationStatus Status, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, MigrationSource? Source = null, MigrationDestination? Destination = null, string? ManifestReference = null, ErrorCategory? LastError = null);
public sealed record KnownFolder(KnownFolderKind Kind, string? Path, bool Exists, KnownFolderResolution Resolution, string Explanation);
public sealed record UserProfile(string Id, string DisplayName, string ProfilePath, bool IsLoaded, ProfileClassification Classification, bool IsSelectable, IReadOnlyList<KnownFolder> KnownFolders);
public sealed record StorageVolume(string RootPath, string? Label, string? FileSystem, long TotalBytes, long AvailableBytes, StorageKind Kind, bool IsReady, AttachmentType Attachment, StorageBusType BusType, string? PhysicalDiskId = null, string? Model = null)
{
    public bool IsExternalCandidate => IsReady && Attachment == AttachmentType.External;
}
public sealed record ToolCapability(string Name, CapabilityState State, string? ExecutablePath = null, string? Version = null, string? Detail = null);
public sealed record OperatingSystemInfo(string Description, string Architecture, string MachineName, string CurrentUser, bool? IsElevated);
public sealed record NetworkShareCapability(string Path, CapabilityState State, string Explanation);
public sealed record EnvironmentCapabilities(OperatingSystemInfo OperatingSystem, IReadOnlyList<StorageVolume> Volumes, IReadOnlyList<UserProfile> UserProfiles, ToolCapability Robocopy, ToolCapability Usmt, IReadOnlyList<NetworkShareCapability> ApprovedShares, IReadOnlyList<string> Warnings)
{
    public IReadOnlyList<StorageVolume> ExternalCandidates => Volumes.Where(volume => volume.IsExternalCandidate).ToArray();
}

public sealed record PolicyProfile(int SchemaVersion, bool AllowConfiguredNetworkShare, bool AllowExternalStorage, bool AllowUsmt, bool AllowRobocopy, VerificationLevel RequiredVerification, ConflictPolicy DefaultConflictPolicy, IReadOnlyList<string> ApprovedNetworkSharePaths)
{
    public const int CurrentSchemaVersion = 1;
    public static PolicyProfile Conservative { get; } = new(CurrentSchemaVersion, false, false, false, false, VerificationLevel.Strong, ConflictPolicy.KeepBoth, Array.Empty<string>());
}
public sealed record PolicyValidationIssue(string Field, string Message);
public sealed record PolicyLoadResult(bool IsValid, PolicyProfile Policy, IReadOnlyList<PolicyValidationIssue> Issues, string Source)
{
    public static PolicyLoadResult Invalid(string source, params PolicyValidationIssue[] issues) => new(false, PolicyProfile.Conservative, issues, source);
}

public sealed record PlanReason(string Subject, CapabilityState State, string Explanation);
public sealed record MigrationPlan(MigrationRoute Route, MigrationStrategy Strategy, MigrationStatus Status, VerificationLevel Verification, ConflictPolicy ConflictPolicy, IReadOnlyList<PlanReason> Reasons, MigrationDestination? Destination)
{
    public string Recommendation => Route == MigrationRoute.NoAvailableRoute ? "No approved migration route is currently available" : $"{Route} + {Strategy}";
}

public sealed record MigrationManifestEntry(string RelativePath, long FileSize, DateTimeOffset LastWriteTime, FileAttributes Attributes, KnownFolderKind SourceKnownFolder, CloudContentState CloudState, TransferState TransferState, VerificationState VerificationState, ErrorCategory? Error, string? Warning);
public enum ManifestCompletionState { Complete, Interrupted }
public enum ManifestReadState { Complete, Incomplete, Corrupt }
public sealed record MigrationManifestHeader(Guid SessionId, DateTimeOffset CreatedAt, long EntryCount, long TotalBytes, VerificationLevel Verification, ConflictPolicy ConflictPolicy, int FormatVersion = 2);
public sealed record MigrationManifestFooter(Guid SessionId, DateTimeOffset CompletedAt, long EntryCount, long TotalBytes, long EligibleEntryCount, long EligibleBytes, long SkippedCount, long WarningCount, ManifestCompletionState CompletionState);
public sealed record ManifestReadResult(ManifestReadState State, MigrationManifestHeader? Header, MigrationManifestFooter? Footer, OperationError? Error);
public sealed record ManifestScanRequest(Guid SessionId, string ProfileId, IReadOnlyList<KnownFolder> KnownFolders, VerificationLevel Verification, ConflictPolicy ConflictPolicy, string ManifestReference);
public sealed record ManifestScanProgress(KnownFolderKind? CurrentFolder, long FilesScanned, long BytesScanned, long Skipped, long Warnings);
public sealed record ManifestScanResult(MigrationManifestHeader Header, string ManifestReference, long Skipped, long Warnings, bool Cancelled);
public sealed record MigrationExecutionPlan(Guid SessionId, DateTimeOffset CreatedAt, string SourceMachineIdentity, string SourceProfileIdentity, IReadOnlyList<KnownFolderKind> SelectedKnownFolders, string ManifestIdentity, string ManifestPath, long ManifestEntryCount, long ExpectedBytes, MigrationRoute Route, MigrationStrategy Strategy, string DestinationIdentity, string DestinationPath, long DestinationAvailableBytes, ConflictPolicy ConflictPolicy, string CloudDisposition, VerificationLevel VerificationRequirement, int PolicySchemaVersion, string PolicyFingerprint, string RobocopyExecutablePath, string? RobocopyVersion, string? ApplicationVersion, bool ReplaceAuthorizedByPolicy = false, bool DestructiveReplaceConfirmed = false)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public string Fingerprint => ExecutionPlanFingerprint.Create(this);
}
public sealed record DestinationValidationContext(MigrationExecutionPlan Plan, IReadOnlyList<string> SourceRoots, PolicyProfile Policy, StorageVolume? CurrentVolume, bool RequireExistingDestination = false, ToolCapability? CurrentRobocopy = null);
public sealed record DestinationValidationResult(bool IsValid, IReadOnlyList<OperationError> Errors);
public sealed record TransferProgress(long FilesProcessed, long BytesProcessed, string Stage, KnownFolderKind? CurrentFolder = null, long? TotalFiles = null, long? TotalBytes = null, double? BytesPerSecond = null, TimeSpan? Elapsed = null, TimeSpan? EstimatedRemaining = null, long Skipped = 0, long Warnings = 0, long Failed = 0);
public sealed record TransferRequest(Guid SessionId, MigrationPlan Plan, string SourceRoot, string DestinationRoot, ConflictPolicy ConflictPolicy);
public sealed record MigrationExecutionRequest(MigrationExecutionPlan Plan, string SourceRoot, string DestinationRoot, KnownFolderKind SourceKnownFolder);
public sealed record TransferResult(bool Succeeded, bool Cancelled, long FilesTransferred, long BytesTransferred, IReadOnlyList<OperationError> Errors);
public enum TransferCompletionState { Failed, Interrupted, TransferCompletedVerificationPending }
public sealed record TransferReconciliationResult(TransferCompletionState State, long EligibleFiles, long EligibleBytes, long DestinationFiles, long DestinationBytes, long SkippedCloudFiles, IReadOnlyList<OperationError> Errors);
public enum VerificationEntryStatus { Verified, Skipped, Missing, SizeMismatch, MetadataMismatch, ContentMismatch, AccessDenied, ChangedSinceTransfer, Unknown }
public enum VerificationRunState { Completed, CompletedWithWarnings, Failed, Cancelled }
public enum FinalMigrationStatus { Success, SuccessWithWarnings, Incomplete, VerificationFailed, Failed, Cancelled }
public sealed record VerificationRequest(MigrationExecutionPlan Plan, string ExpectedExecutionPlanFingerprint, string CurrentPolicyFingerprint, IReadOnlyDictionary<KnownFolderKind, string> SourceRoots, string DestinationRoot, VerificationLevel Mode, IReadOnlySet<string>? RetryRelativePaths = null);
public sealed record VerificationProgress(VerificationLevel Mode, long FilesProcessed, long TotalFiles, long BytesProcessed, long TotalBytes, KnownFolderKind? CurrentFolder, string? SafeCurrentFile, TimeSpan Elapsed, double? BytesPerSecond, long Failures, long Mismatches, long Skipped);
public sealed record VerificationEntryResult(KnownFolderKind KnownFolder, string RelativePath, string DestinationRelativePath, long ExpectedBytes, VerificationEntryStatus Status, ErrorCategory? Error, string TechnicianMessage, string? SourceSha256 = null, string? DestinationSha256 = null);
public sealed record VerificationResult(Guid Id, Guid SessionId, DateTimeOffset StartedAt, DateTimeOffset CompletedAt, VerificationLevel Mode, VerificationRunState State, long EligibleFiles, long EligibleBytes, long VerifiedFiles, long VerifiedBytes, long SkippedFiles, long Mismatches, IReadOnlyList<VerificationEntryResult> Entries, string ExecutionPlanFingerprint, string ManifestIdentity, int SchemaVersion = 1);
public sealed record MigrationReport(Guid SessionId, string ApplicationVersion, DateTimeOffset CreatedAt, string SourceMachine, string SourceProfile, string Destination, MigrationRoute Route, MigrationStrategy Strategy, IReadOnlyList<KnownFolderKind> SelectedFolders, long ExpectedFiles, long ExpectedBytes, long TransferredFiles, long TransferredBytes, long SkippedCloudContent, long LockedFiles, long Conflicts, long Failures, VerificationRunState StandardVerification, VerificationRunState? StrongVerification, TimeSpan ScanElapsed, TimeSpan TransferElapsed, TimeSpan VerificationElapsed, IReadOnlyList<string> Warnings, FinalMigrationStatus FinalStatus, string PolicyFingerprint, string ExecutionPlanFingerprint, string ManifestIdentity, Guid VerificationResultIdentity, int SchemaVersion = 1);
public sealed record MigrationOperationalRecord(Guid SessionId, TimeSpan ScanElapsed, TimeSpan TransferElapsed, long TransferredFiles, long TransferredBytes, long LockedFiles, long Conflicts, long TransferFailures, IReadOnlyList<OperationError> TransferErrors, TimeSpan VerificationElapsed, Guid? VerificationResultIdentity, int SchemaVersion = 1);
public sealed record OperationError(ErrorCategory Category, string TechnicianMessage, string? SafeTechnicalDetail = null);
