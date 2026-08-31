namespace RoboTransfer.Core;

public interface ICapabilityDetector { Task<EnvironmentCapabilities> DetectAsync(PolicyProfile policy, CancellationToken cancellationToken = default); }
public interface IUserProfileDetector { Task<IReadOnlyList<UserProfile>> DetectAsync(CancellationToken cancellationToken = default); }
public interface IStorageDetector { Task<IReadOnlyList<StorageVolume>> DetectAsync(CancellationToken cancellationToken = default); }
public interface IToolDetector { Task<ToolCapability> DetectAsync(CancellationToken cancellationToken = default); }
public interface IMigrationPlanner { MigrationPlan CreatePlan(PolicyProfile policy, EnvironmentCapabilities capabilities, long? requiredBytes = null, bool cloudStateUncertain = false); }
public interface IPolicyProvider { Task<PolicyLoadResult> LoadAsync(CancellationToken cancellationToken = default); }
public interface IMigrationJournal
{
    Task SaveAsync(MigrationSession session, CancellationToken cancellationToken = default);
    Task<MigrationSession?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<MigrationSession> FindIncompleteAsync(CancellationToken cancellationToken = default);
}
public interface IMigrationRecovery
{
    IAsyncEnumerable<MigrationSession> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<DestinationValidationResult> ValidateResumeAsync(MigrationSession session, MigrationExecutionPlan plan, DestinationValidationContext destination, CancellationToken cancellationToken = default);
    Task AbandonAsync(MigrationSession session, CancellationToken cancellationToken = default);
}
public interface IExecutionPlanStore
{
    Task SaveAsync(MigrationExecutionPlan plan, CancellationToken cancellationToken = default);
    Task<MigrationExecutionPlan?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default);
}
public interface ITransferEngine
{
    string Name { get; }
    IAsyncEnumerable<TransferProgress> ExecuteAsync(TransferRequest request, CancellationToken cancellationToken = default);
}
public interface IOperationalTransferEngine
{
    Task<TransferResult> ExecuteAsync(MigrationExecutionRequest request, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default);
}
public interface ITransferReconciler { Task<TransferReconciliationResult> ReconcileAsync(MigrationExecutionPlan plan, TransferResult transfer, CancellationToken cancellationToken = default); }
public interface ICloudPlaceholderDetector { CloudContentState Detect(string path); }
public interface IManifestWriter : IAsyncDisposable
{
    Task WriteHeaderAsync(MigrationManifestHeader header, CancellationToken cancellationToken = default);
    Task WriteEntryAsync(MigrationManifestEntry entry, CancellationToken cancellationToken = default);
    Task CompleteAsync(MigrationManifestFooter footer, CancellationToken cancellationToken = default);
}
public interface IManifestReader
{
    Task<ManifestReadResult> InspectAsync(string path, CancellationToken cancellationToken = default);
    IAsyncEnumerable<MigrationManifestEntry> ReadEntriesAsync(string path, CancellationToken cancellationToken = default);
}
public interface IManifestScanner
{
    Task<ManifestScanResult> ScanAsync(ManifestScanRequest request, IManifestWriter writer, IProgress<ManifestScanProgress>? progress = null, CancellationToken cancellationToken = default);
}
public interface IDestinationValidator { Task<DestinationValidationResult> ValidateAsync(DestinationValidationContext context, CancellationToken cancellationToken = default); }
public interface IDestinationWriteProbe { Task<bool> IsWritableAsync(string path, bool requireExisting, CancellationToken cancellationToken = default); }
public interface IVerificationEngine { Task<VerificationResult> VerifyAsync(VerificationRequest request, IProgress<VerificationProgress>? progress = null, CancellationToken cancellationToken = default); }
public interface IVerificationStore { Task SaveAsync(VerificationResult result, CancellationToken cancellationToken = default); Task<VerificationResult?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default); }
public interface IReportGenerator { Task<(string JsonPath, string HtmlPath)> GenerateAsync(MigrationReport report, string outputDirectory, CancellationToken cancellationToken = default); }
public interface IOperationalRecordStore { Task SaveAsync(MigrationOperationalRecord record, CancellationToken cancellationToken = default); Task<MigrationOperationalRecord?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default); }
