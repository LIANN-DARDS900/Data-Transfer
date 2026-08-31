using RoboTransfer.Core;

namespace RoboTransfer.Windows;

public sealed class ManifestScanner(ICloudPlaceholderDetector cloudDetector) : IManifestScanner
{
    public async Task<ManifestScanResult> ScanAsync(ManifestScanRequest request, IManifestWriter writer, IProgress<ManifestScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selected = request.KnownFolders.Where(f => f.Resolution == KnownFolderResolution.Resolved && f.Exists && !string.IsNullOrWhiteSpace(f.Path)).ToArray();
        if (selected.Length != request.KnownFolders.Count) throw new ArgumentException("Every selected Known Folder must be authoritatively resolved and available.", nameof(request));

        long files = 0, bytes = 0, skipped = 0, warnings = 0, eligibleFiles = 0, eligibleBytes = 0;
        await writer.WriteHeaderAsync(new(request.SessionId, DateTimeOffset.UtcNow, 0, 0, request.Verification, request.ConflictPolicy), cancellationToken);
        foreach (var folder in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = CanonicalDirectory(folder.Path!);
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                IEnumerable<string> entries;
                try { entries = Directory.EnumerateFileSystemEntries(directory); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or IOException)
                {
                    skipped++; warnings++; Report(folder.Kind); continue;
                }

                using var enumerator = entries.GetEnumerator();
                while (true)
                {
                    string path;
                    try { if (!enumerator.MoveNext()) break; path = enumerator.Current; }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or DirectoryNotFoundException or FileNotFoundException or IOException)
                    { skipped++; warnings++; break; }
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var canonical = Path.GetFullPath(path);
                        if (!IsWithin(root, canonical)) { skipped++; warnings++; continue; }
                        var attributes = File.GetAttributes(canonical);
                        if ((attributes & FileAttributes.ReparsePoint) != 0) { skipped++; warnings++; continue; }
                        if ((attributes & FileAttributes.Directory) != 0) { pending.Push(canonical); continue; }
                        if (string.Equals(Path.GetExtension(canonical), ".ost", StringComparison.OrdinalIgnoreCase)) { skipped++; warnings++; continue; }
                        var info = new FileInfo(canonical);
                        var cloud = cloudDetector.Detect(canonical);
                        var unavailable = cloud is CloudContentState.OnlineOnly or CloudContentState.PartiallyAvailable or CloudContentState.Unavailable or CloudContentState.Unknown;
                        var entry = new MigrationManifestEntry(Path.GetRelativePath(root, canonical), info.Length, info.LastWriteTimeUtc, attributes, folder.Kind, cloud, unavailable ? TransferState.Skipped : TransferState.Pending, VerificationState.NotVerified, unavailable ? ErrorCategory.CloudContentUnavailable : null, unavailable ? "Cloud bytes are not confirmed locally available; item is excluded." : null);
                        await writer.WriteEntryAsync(entry, cancellationToken);
                        files++; bytes += info.Length; if (unavailable) { skipped++; warnings++; } else { eligibleFiles++; eligibleBytes += info.Length; }
                    }
                    catch (Exception ex) when (ex is UnauthorizedAccessException or FileNotFoundException or DirectoryNotFoundException or PathTooLongException or IOException)
                    { skipped++; warnings++; }
                    Report(folder.Kind);
                }
            }
        }
        var header = new MigrationManifestHeader(request.SessionId, DateTimeOffset.UtcNow, files, bytes, request.Verification, request.ConflictPolicy);
        await writer.CompleteAsync(new(request.SessionId, DateTimeOffset.UtcNow, files, bytes, eligibleFiles, eligibleBytes, skipped, warnings, ManifestCompletionState.Complete), cancellationToken);
        return new(header, request.ManifestReference, skipped, warnings, false);

        void Report(KnownFolderKind kind) => progress?.Report(new(kind, files, bytes, skipped, warnings));
    }

    private static string CanonicalDirectory(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static bool IsWithin(string root, string path) => path.StartsWith(root + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || string.Equals(path, root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
