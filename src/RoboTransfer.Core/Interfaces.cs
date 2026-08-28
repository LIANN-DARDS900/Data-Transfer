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
public interface ITransferEngine
{
    string Name { get; }
    IAsyncEnumerable<TransferProgress> ExecuteAsync(TransferRequest request, CancellationToken cancellationToken = default);
}
public interface ICloudPlaceholderDetector { CloudContentState Detect(string path); }
public interface IManifestWriter : IAsyncDisposable
{
    Task WriteHeaderAsync(MigrationManifestHeader header, CancellationToken cancellationToken = default);
    Task WriteEntryAsync(MigrationManifestEntry entry, CancellationToken cancellationToken = default);
}
