using RoboTransfer.Core;

namespace RoboTransfer.Robocopy;

public sealed class KeepBothTransferEngine(IManifestReader manifests) : IOperationalTransferEngine
{
    public async Task<TransferResult> ExecuteAsync(MigrationExecutionRequest request, IProgress<TransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (request.Plan.ConflictPolicy != ConflictPolicy.KeepBoth) throw new InvalidOperationException("KeepBoth orchestration only accepts KeepBoth plans.");
        var sourceRoot = Canonical(request.SourceRoot); var destinationRoot = Canonical(request.DestinationRoot);
        long files = 0, bytes = 0, skipped = 0, warnings = 0, failed = 0; var errors = new List<OperationError>(); var started = DateTimeOffset.UtcNow;
        try
        {
            await foreach (var entry in manifests.ReadEntriesAsync(request.Plan.ManifestPath, cancellationToken))
            {
                if (entry.SourceKnownFolder != request.SourceKnownFolder) continue;
                if (entry.TransferState == TransferState.Skipped) { skipped++; continue; }
                cancellationToken.ThrowIfCancellationRequested();
                var source = Within(sourceRoot, entry.RelativePath); var desired = Within(destinationRoot, entry.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(desired)!);
                try
                {
                    await CopyKeepBothAsync(source, desired, entry, cancellationToken); files++; bytes += entry.FileSize;
                }
                catch (IOException) when (Path.GetExtension(source).Equals(".pst", StringComparison.OrdinalIgnoreCase))
                {
                    failed++; errors.Add(new(ErrorCategory.FileLocked, "Outlook data file is currently in use. Close Outlook and retry this item.", "PST copy returned a sharing or I/O failure."));
                }
                catch (UnauthorizedAccessException) { failed++; errors.Add(new(ErrorCategory.AccessDenied, "A file could not be copied. Verify access and retry; RoboTransfer will not bypass security policy.")); }
                catch (IOException) { failed++; errors.Add(new(ErrorCategory.Unknown, "A file could not be copied. Inspect the item and retry.")); }
                progress?.Report(new(files, bytes, "Transferring", request.SourceKnownFolder, request.Plan.ManifestEntryCount, request.Plan.ExpectedBytes, Elapsed: DateTimeOffset.UtcNow - started, Skipped: skipped, Warnings: warnings, Failed: failed));
            }
            return new(errors.Count == 0, false, files, bytes, errors);
        }
        catch (OperationCanceledException) { return new(false, true, files, bytes, [.. errors, new(ErrorCategory.Cancelled, "Transfer was deliberately interrupted and can be resumed after revalidation.")]); }
    }

    private static async Task CopyKeepBothAsync(string source, string desired, MigrationManifestEntry entry, CancellationToken cancellationToken)
    {
        for (var collision = 0; ; collision++)
        {
            var candidate = collision == 0 ? desired : Candidate(desired, collision);
            try
            {
                try { await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan)) await using (var output = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough)) { await input.CopyToAsync(output, 128 * 1024, cancellationToken); await output.FlushAsync(cancellationToken); } File.SetLastWriteTimeUtc(candidate, entry.LastWriteTime.UtcDateTime); File.SetAttributes(candidate, entry.Attributes & ~(FileAttributes.ReparsePoint | FileAttributes.Offline)); }
                catch { if (File.Exists(candidate)) File.Delete(candidate); throw; }
                return;
            }
            catch (IOException) when (File.Exists(candidate)) { }
        }
    }
    private static string Candidate(string path, int collision) { var suffix = collision == 1 ? " (RoboTransfer copy)" : $" (RoboTransfer copy {collision})"; return Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + suffix + Path.GetExtension(path)); }
    private static string Canonical(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static string Within(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new InvalidDataException("Manifest path must be relative.");
        var combined = Path.GetFullPath(Path.Combine(root, relative));
        if (!combined.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Manifest path escaped its approved root.");
        return combined;
    }
}
