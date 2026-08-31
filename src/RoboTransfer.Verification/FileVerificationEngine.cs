using System.Diagnostics;
using System.Security.Cryptography;
using RoboTransfer.Core;

namespace RoboTransfer.Verification;

public sealed class FileVerificationEngine(IManifestReader manifests) : IVerificationEngine
{
    public async Task<VerificationResult> VerifyAsync(VerificationRequest request, IProgress<VerificationProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.ExpectedExecutionPlanFingerprint, request.Plan.Fingerprint, StringComparison.Ordinal)) throw new InvalidDataException("Execution plan fingerprint validation failed.");
        if (!string.Equals(request.CurrentPolicyFingerprint, request.Plan.PolicyFingerprint, StringComparison.Ordinal)) throw new InvalidDataException("Policy fingerprint validation failed.");
        var inspection = await manifests.InspectAsync(request.Plan.ManifestPath, cancellationToken); if (inspection.State != ManifestReadState.Complete || inspection.Footer is null) throw new InvalidDataException("A completed, valid manifest is required.");
        var started = DateTimeOffset.UtcNow; var clock = Stopwatch.StartNew(); var entries = new List<VerificationEntryResult>(); long processed = 0, bytes = 0, verified = 0, verifiedBytes = 0, skipped = 0, mismatches = 0, failures = 0;
        try
        {
            await foreach (var entry in manifests.ReadEntriesAsync(request.Plan.ManifestPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identity = $"{entry.SourceKnownFolder}/{entry.RelativePath}"; if (request.RetryRelativePaths is not null && !request.RetryRelativePaths.Contains(identity)) continue;
                if (entry.TransferState == TransferState.Skipped) { skipped++; entries.Add(Result(entry, entry.RelativePath, VerificationEntryStatus.Skipped, entry.Error, "Item was intentionally excluded from transfer.")); Report(entry); continue; }
                if (!request.SourceRoots.TryGetValue(entry.SourceKnownFolder, out var sourceRoot)) { failures++; entries.Add(Result(entry, entry.RelativePath, VerificationEntryStatus.Unknown, ErrorCategory.ConfigurationInvalid, "Source Known Folder identity is unavailable.")); Report(entry); continue; }
                var source = ResolveInside(sourceRoot, entry.RelativePath); var destinationRelative = Path.Combine(entry.SourceKnownFolder.ToString(), entry.RelativePath); var desiredDestination = ResolveInside(request.DestinationRoot, destinationRelative); var destination = FindKeepBothDestination(desiredDestination, entry);
                var result = await VerifyEntryAsync(entry, source, destination, destinationRelative, request.Mode, cancellationToken); entries.Add(result); processed++; if (result.Status == VerificationEntryStatus.Verified) { verified++; verifiedBytes += entry.FileSize; } else { failures++; if (result.Status is VerificationEntryStatus.SizeMismatch or VerificationEntryStatus.MetadataMismatch or VerificationEntryStatus.ChangedSinceTransfer) mismatches++; } bytes += entry.FileSize; Report(entry);
            }
            var state = failures > 0 ? VerificationRunState.Failed : skipped > 0 ? VerificationRunState.CompletedWithWarnings : VerificationRunState.Completed;
            return new(Guid.NewGuid(), request.Plan.SessionId, started, DateTimeOffset.UtcNow, request.Mode, state, inspection.Footer.EligibleEntryCount, inspection.Footer.EligibleBytes, verified, verifiedBytes, skipped, mismatches, entries, request.Plan.Fingerprint, request.Plan.ManifestIdentity);
        }
        catch (OperationCanceledException) { return new(Guid.NewGuid(), request.Plan.SessionId, started, DateTimeOffset.UtcNow, request.Mode, VerificationRunState.Cancelled, inspection.Footer.EligibleEntryCount, inspection.Footer.EligibleBytes, verified, verifiedBytes, skipped, mismatches, entries, request.Plan.Fingerprint, request.Plan.ManifestIdentity); }
        void Report(MigrationManifestEntry entry) => progress?.Report(new(request.Mode, processed, inspection.Footer.EligibleEntryCount, bytes, inspection.Footer.EligibleBytes, entry.SourceKnownFolder, Redact(entry.RelativePath), clock.Elapsed, clock.Elapsed.TotalSeconds > 0 ? bytes / clock.Elapsed.TotalSeconds : null, failures, mismatches, skipped));
    }

    private static async Task<VerificationEntryResult> VerifyEntryAsync(MigrationManifestEntry entry, string source, string destination, string destinationRelative, VerificationLevel mode, CancellationToken token)
    {
        try
        {
            if (!File.Exists(destination)) return Result(entry, destinationRelative, VerificationEntryStatus.Missing, ErrorCategory.VerificationMismatch, "Destination file is missing. Retry this verification item after restoring the transfer.");
            var sourceInfo = new FileInfo(source); var destinationInfo = new FileInfo(destination);
            if (!sourceInfo.Exists || sourceInfo.Length != entry.FileSize || new DateTimeOffset(sourceInfo.LastWriteTimeUtc) != entry.LastWriteTime.ToUniversalTime()) return Result(entry, destinationRelative, VerificationEntryStatus.ChangedSinceTransfer, ErrorCategory.VerificationMismatch, "Source changed after scanning. Rescan and transfer this item again.");
            if (destinationInfo.Length != entry.FileSize) return Result(entry, destinationRelative, VerificationEntryStatus.SizeMismatch, ErrorCategory.VerificationMismatch, "Destination size does not match the approved manifest. Retry this item.");
            if (Math.Abs((destinationInfo.LastWriteTimeUtc - entry.LastWriteTime.UtcDateTime).TotalSeconds) > 2) return Result(entry, destinationRelative, VerificationEntryStatus.MetadataMismatch, ErrorCategory.VerificationMismatch, "Destination timestamp differs from the approved manifest.");
            if (mode == VerificationLevel.Standard) return Result(entry, destinationRelative, VerificationEntryStatus.Verified, null, "Standard metadata verification passed.");
            var sourceBefore = Snapshot(sourceInfo); var destinationBefore = Snapshot(destinationInfo); var sourceHash = await HashAsync(source, token); var destinationHash = await HashAsync(destination, token); sourceInfo.Refresh(); destinationInfo.Refresh();
            if (sourceBefore != Snapshot(sourceInfo) || destinationBefore != Snapshot(destinationInfo)) return Result(entry, destinationRelative, VerificationEntryStatus.ChangedSinceTransfer, ErrorCategory.VerificationMismatch, "Source or destination changed while hashing. Retry verification.", sourceHash, destinationHash);
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(sourceHash), Convert.FromHexString(destinationHash)) ? Result(entry, destinationRelative, VerificationEntryStatus.Verified, null, "SHA-256 verification passed.", sourceHash, destinationHash) : Result(entry, destinationRelative, VerificationEntryStatus.ContentMismatch, ErrorCategory.VerificationMismatch, "SHA-256 hashes do not match. Retry transfer for this item.", sourceHash, destinationHash);
        }
        catch (UnauthorizedAccessException) { return Result(entry, destinationRelative, VerificationEntryStatus.AccessDenied, ErrorCategory.AccessDenied, "Verification could not read the file. Verify access without bypassing endpoint policy."); }
        catch (IOException) { return Result(entry, destinationRelative, VerificationEntryStatus.Unknown, ErrorCategory.Unknown, "Verification could not safely read the file. Inspect and retry this item."); }
    }
    private static async Task<string> HashAsync(string path, CancellationToken token) { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer = new byte[128 * 1024]; int read; while ((read = await stream.ReadAsync(buffer, token)) > 0) hash.AppendData(buffer, 0, read); return Convert.ToHexString(hash.GetHashAndReset()); }
    private static (long Length, DateTime LastWrite) Snapshot(FileInfo info) => (info.Length, info.LastWriteTimeUtc);
    private static VerificationEntryResult Result(MigrationManifestEntry entry, string destination, VerificationEntryStatus status, ErrorCategory? error, string message, string? sourceHash = null, string? destinationHash = null) => new(entry.SourceKnownFolder, entry.RelativePath, destination, entry.FileSize, status, error, message, sourceHash, destinationHash);
    private static string ResolveInside(string root, string relative) { if (Path.IsPathRooted(relative)) throw new InvalidDataException("Verification path must be relative."); var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)); var path = Path.GetFullPath(Path.Combine(canonicalRoot, relative)); if (!path.StartsWith(canonicalRoot + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Verification path escaped its approved root."); if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Verification refuses reparse points."); return path; }
    private static string FindKeepBothDestination(string desired, MigrationManifestEntry entry) { for (var collision = 0; collision < 100_000; collision++) { var candidate = collision == 0 ? desired : Path.Combine(Path.GetDirectoryName(desired)!, Path.GetFileNameWithoutExtension(desired) + (collision == 1 ? " (RoboTransfer copy)" : $" (RoboTransfer copy {collision})") + Path.GetExtension(desired)); if (!File.Exists(candidate)) return desired; var info = new FileInfo(candidate); if (info.Length == entry.FileSize && Math.Abs((info.LastWriteTimeUtc - entry.LastWriteTime.UtcDateTime).TotalSeconds) <= 2) return candidate; } throw new InvalidDataException("KeepBoth collision search exceeded its safe bound."); }
    private static string Redact(string path) { var name = Path.GetFileName(path); var digest = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(name)))[..8]; return $"file-{digest}{Path.GetExtension(name)}"; }
}
