using RoboTransfer.Core;
using RoboTransfer.Persistence;

namespace RoboTransfer.App.Services;

public sealed class MigrationWorkflowCoordinator(IManifestScanner scanner, IManifestReader manifestReader, IMigrationJournal journal, IMigrationRecovery recovery, IExecutionPlanStore plans, IDestinationValidator destinations, ITransferReconciler reconciler, KeepBothTransferEngineFactory keepBothFactory, IVerificationEngine verification, IVerificationStore verificationStore, IOperationalRecordStore operationalRecords, IReportGenerator reports, string dataRoot)
{
    public async Task<ManifestScanResult> ScanAsync(UserProfile profile, IReadOnlyList<KnownFolder> folders, PolicyProfile policy, IProgress<ManifestScanProgress> progress, CancellationToken cancellationToken)
    {
        var scanStarted = System.Diagnostics.Stopwatch.StartNew(); var sessionId = Guid.NewGuid(); var directory = Path.Combine(dataRoot, "manifests"); Directory.CreateDirectory(directory); var path = Path.Combine(directory, $"{sessionId:N}.jsonl");
        await using var writer = new JsonLinesManifestWriter(path);
        var result = await scanner.ScanAsync(new(sessionId, profile.Id, folders, policy.RequiredVerification, policy.DefaultConflictPolicy, path), writer, progress, cancellationToken);
        scanStarted.Stop(); await operationalRecords.SaveAsync(new(sessionId, scanStarted.Elapsed, TimeSpan.Zero, 0, 0, 0, 0, 0, [], TimeSpan.Zero, null), CancellationToken.None);
        await journal.SaveAsync(new(sessionId, MigrationRole.OldPc, MigrationStatus.Ready, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, new(Environment.MachineName, profile.Id), ManifestReference: path), cancellationToken);
        return result;
    }

    public async Task<MigrationExecutionPlan> LockPlanAsync(ManifestScanResult scan, UserProfile profile, IReadOnlyList<KnownFolder> folders, MigrationPlan recommendation, PolicyProfile policy, ToolCapability robocopy, StorageVolume? volume, CancellationToken cancellationToken = default)
    {
        var destination = recommendation.Destination ?? throw new InvalidOperationException("No approved destination is available.");
        var plan = new MigrationExecutionPlan(scan.Header.SessionId, DateTimeOffset.UtcNow, Environment.MachineName, profile.Id, folders.Select(f => f.Kind).ToArray(), Path.GetFileNameWithoutExtension(scan.ManifestReference), scan.ManifestReference, scan.Header.EntryCount, scan.Header.TotalBytes, recommendation.Route, recommendation.Strategy, volume?.PhysicalDiskId ?? destination.Location, destination.Location, destination.AvailableBytes ?? 0, recommendation.ConflictPolicy, "Unavailable cloud content skipped", recommendation.Verification, policy.SchemaVersion, PolicyFingerprint.Create(policy), robocopy.ExecutablePath ?? string.Empty, robocopy.Version, typeof(MigrationWorkflowCoordinator).Assembly.GetName().Version?.ToString()); await plans.SaveAsync(plan, cancellationToken); var session = await journal.LoadAsync(plan.SessionId, cancellationToken); if (session is not null) await journal.SaveAsync(session with { Status = MigrationStatus.Prepared, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken); return plan;
    }

    public async Task<TransferReconciliationResult> TransferAsync(MigrationExecutionPlan plan, UserProfile profile, PolicyProfile policy, StorageVolume? volume, IProgress<TransferProgress> progress, CancellationToken cancellationToken)
    {
        var transferStarted = System.Diagnostics.Stopwatch.StartNew();
        var session = await journal.LoadAsync(plan.SessionId, cancellationToken) ?? throw new InvalidOperationException("Session journal is missing.");
        var sourceRoots = profile.KnownFolders.Where(f => plan.SelectedKnownFolders.Contains(f.Kind)).Select(f => f.Path!).ToArray(); var validation = await destinations.ValidateAsync(new(plan, sourceRoots, policy, volume), cancellationToken); if (!validation.IsValid) return new(TransferCompletionState.Failed, 0, 0, 0, 0, 0, validation.Errors);
        await journal.SaveAsync(session with { Status = MigrationStatus.Transferring, UpdatedAt = DateTimeOffset.UtcNow }, cancellationToken);
        long files = 0, bytes = 0; var errors = new List<OperationError>(); var cancelled = false;
        foreach (var kind in plan.SelectedKnownFolders)
        {
            var source = profile.KnownFolders.Single(f => f.Kind == kind).Path!; var destination = Path.Combine(plan.DestinationPath, kind.ToString());
            var completedFiles = files; var completedBytes = bytes; var aggregateProgress = new Progress<TransferProgress>(value => progress.Report(value with { FilesProcessed = completedFiles + value.FilesProcessed, BytesProcessed = completedBytes + value.BytesProcessed }));
            var result = await keepBothFactory.Create().ExecuteAsync(new(plan, source, destination, kind), aggregateProgress, cancellationToken);
            files += result.FilesTransferred; bytes += result.BytesTransferred; errors.AddRange(result.Errors); cancelled |= result.Cancelled;
            if (cancelled) break;
        }
        var aggregate = new TransferResult(!cancelled && errors.Count == 0, cancelled, files, bytes, errors);
        var reconciliation = await reconciler.ReconcileAsync(plan, aggregate, CancellationToken.None); transferStarted.Stop(); var existingRecord = await operationalRecords.LoadAsync(plan.SessionId, CancellationToken.None) ?? new(plan.SessionId, TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, 0, 0, [], TimeSpan.Zero, null); await operationalRecords.SaveAsync(existingRecord with { TransferElapsed = transferStarted.Elapsed, TransferredFiles = files, TransferredBytes = bytes, LockedFiles = errors.LongCount(error => error.Category == ErrorCategory.FileLocked), Conflicts = errors.LongCount(error => error.Category == ErrorCategory.DestinationConflict), TransferFailures = errors.Count, TransferErrors = errors }, CancellationToken.None);
        var status = reconciliation.State switch { TransferCompletionState.Interrupted => MigrationStatus.Interrupted, TransferCompletionState.TransferCompletedVerificationPending => MigrationStatus.Verifying, _ => MigrationStatus.Failed };
        await journal.SaveAsync(session with { Status = status, UpdatedAt = DateTimeOffset.UtcNow }, CancellationToken.None);
        return reconciliation;
    }

    public IAsyncEnumerable<MigrationSession> DiscoverAsync(CancellationToken cancellationToken = default) => recovery.DiscoverAsync(cancellationToken);
    public Task AbandonAsync(MigrationSession session, CancellationToken cancellationToken = default) => recovery.AbandonAsync(session, cancellationToken);
    public async Task<(MigrationExecutionPlan? Plan, DestinationValidationResult Validation)> ValidateResumeAsync(MigrationSession session, UserProfile profile, PolicyProfile policy, StorageVolume? volume, ToolCapability robocopy, CancellationToken cancellationToken) { var plan = await plans.LoadAsync(session.Id, cancellationToken); if (plan is null) return (null, new(false, [new(ErrorCategory.ConfigurationInvalid, "Execution plan is missing. Resume is blocked.")])); var sources = profile.KnownFolders.Where(f => plan.SelectedKnownFolders.Contains(f.Kind)).Select(f => f.Path!).ToArray(); var context = new DestinationValidationContext(plan, sources, policy, volume, true, robocopy); return (plan, await recovery.ValidateResumeAsync(session, plan, context, cancellationToken)); }
    public async Task<VerificationResult> VerifyAsync(MigrationExecutionPlan plan, UserProfile profile, PolicyProfile policy, IReadOnlySet<string>? retry, IProgress<VerificationProgress> progress, CancellationToken cancellationToken)
    {
        var roots = profile.KnownFolders.Where(folder => plan.SelectedKnownFolders.Contains(folder.Kind) && folder.Path is not null).ToDictionary(folder => folder.Kind, folder => folder.Path!); var result = await verification.VerifyAsync(new(plan, plan.Fingerprint, PolicyFingerprint.Create(policy), roots, plan.DestinationPath, plan.VerificationRequirement, retry), progress, cancellationToken); await verificationStore.SaveAsync(result, CancellationToken.None); var operations = await operationalRecords.LoadAsync(plan.SessionId, CancellationToken.None) ?? new(plan.SessionId, TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, 0, 0, [], TimeSpan.Zero, null); await operationalRecords.SaveAsync(operations with { VerificationElapsed = result.CompletedAt - result.StartedAt, VerificationResultIdentity = result.Id }, CancellationToken.None); var session = await journal.LoadAsync(plan.SessionId, CancellationToken.None); if (session is not null) await journal.SaveAsync(session with { Status = result.State is VerificationRunState.Completed or VerificationRunState.CompletedWithWarnings ? MigrationStatus.Completed : result.State == VerificationRunState.Cancelled ? MigrationStatus.Interrupted : MigrationStatus.Failed, UpdatedAt = DateTimeOffset.UtcNow, LastError = result.State == VerificationRunState.Failed ? ErrorCategory.VerificationFailed : null }, CancellationToken.None); return result;
    }
    public async Task<(string JsonPath, string HtmlPath)> GenerateReportAsync(MigrationExecutionPlan plan, CancellationToken cancellationToken)
    {
        var verificationResult = await verificationStore.LoadAsync(plan.SessionId, cancellationToken) ?? throw new InvalidOperationException("Durable verification result is unavailable."); var operations = await operationalRecords.LoadAsync(plan.SessionId, cancellationToken) ?? throw new InvalidOperationException("Durable operational record is unavailable."); var inspection = await manifestReader.InspectAsync(plan.ManifestPath, cancellationToken); var footer = inspection.Footer ?? throw new InvalidDataException("Completed manifest is unavailable."); var final = verificationResult.State switch { VerificationRunState.Completed when footer.WarningCount == 0 => FinalMigrationStatus.Success, VerificationRunState.Completed or VerificationRunState.CompletedWithWarnings => FinalMigrationStatus.SuccessWithWarnings, VerificationRunState.Cancelled => FinalMigrationStatus.Cancelled, VerificationRunState.Failed => FinalMigrationStatus.VerificationFailed, _ => FinalMigrationStatus.Incomplete }; var report = new MigrationReport(plan.SessionId, plan.ApplicationVersion ?? "unknown", DateTimeOffset.UtcNow, plan.SourceMachineIdentity, plan.SourceProfileIdentity, plan.DestinationPath, plan.Route, plan.Strategy, plan.SelectedKnownFolders, footer.EligibleEntryCount, footer.EligibleBytes, operations.TransferredFiles, operations.TransferredBytes, footer.SkippedCount, operations.LockedFiles, operations.Conflicts, operations.TransferFailures + verificationResult.Entries.LongCount(entry => entry.Status is not (VerificationEntryStatus.Verified or VerificationEntryStatus.Skipped)), verificationResult.State, plan.VerificationRequirement == VerificationLevel.Strong ? verificationResult.State : null, operations.ScanElapsed, operations.TransferElapsed, operations.VerificationElapsed, operations.TransferErrors.Select(error => error.TechnicianMessage).Concat(verificationResult.Entries.Where(entry => entry.Status != VerificationEntryStatus.Verified).Select(entry => entry.TechnicianMessage)).Distinct().Take(100).ToArray(), final, plan.PolicyFingerprint, plan.Fingerprint, plan.ManifestIdentity, verificationResult.Id); return await reports.GenerateAsync(report, Path.Combine(dataRoot, "reports", plan.SessionId.ToString("N")), cancellationToken);
    }
}

public sealed class KeepBothTransferEngineFactory(IManifestReader reader) { public RoboTransfer.Robocopy.KeepBothTransferEngine Create() => new(reader); }
