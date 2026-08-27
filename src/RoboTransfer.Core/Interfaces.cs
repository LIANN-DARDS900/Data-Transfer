namespace RoboTransfer.Core;

public interface ICapabilityDetector { Task<EnvironmentCapabilities> DetectAsync(PolicyProfile policy, CancellationToken cancellationToken = default); }
public interface IUserProfileDetector { Task<IReadOnlyList<UserProfile>> DetectAsync(CancellationToken cancellationToken = default); }
public interface IStorageDetector { Task<IReadOnlyList<StorageVolume>> DetectAsync(CancellationToken cancellationToken = default); }
public interface IToolDetector { Task<ToolCapability> DetectAsync(CancellationToken cancellationToken = default); }
public interface IMigrationPlanner { MigrationPlan CreatePlan(PolicyProfile policy, EnvironmentCapabilities capabilities, long? requiredBytes = null); }
public interface IMigrationJournal { Task SaveAsync(MigrationSession session, CancellationToken cancellationToken = default); Task<MigrationSession?> LoadAsync(Guid sessionId, CancellationToken cancellationToken = default); }
public interface ITransferEngine { Task<TransferResult> ExecuteAsync(MigrationPlan plan, CancellationToken cancellationToken = default); }
public sealed record TransferResult(bool Succeeded, string Message);
public interface ICloudPlaceholderDetector { CloudContentState Detect(string path); }
