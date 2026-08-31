using RoboTransfer.Core;
using RoboTransfer.Windows;

namespace RoboTransfer.Windows.Tests;

public sealed class ManifestScannerTests
{
    [Fact]
    public async Task Scans_nested_files_and_preserves_relative_paths()
    {
        using var temp = new TemporaryDirectory(); Directory.CreateDirectory(Path.Combine(temp.Path, "nested"));
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "a.txt"), "abc");
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "nested", "b.txt"), "12345");
        var writer = new RecordingWriter();
        var result = await Scanner().ScanAsync(Request(temp.Path), writer);
        Assert.Equal(2, result.Header.EntryCount); Assert.Equal(8, result.Header.TotalBytes);
        Assert.Contains(writer.Entries, e => e.RelativePath == Path.Combine("nested", "b.txt"));
    }

    [Fact]
    public async Task Unknown_cloud_bytes_are_never_claimed_eligible()
    {
        using var temp = new TemporaryDirectory(); await File.WriteAllTextAsync(Path.Combine(temp.Path, "cloud.txt"), "bytes");
        var writer = new RecordingWriter();
        var scanner = new ManifestScanner(new FakeCloud(CloudContentState.Unknown));
        var result = await scanner.ScanAsync(Request(temp.Path), writer);
        Assert.Equal(1, result.Skipped); Assert.Equal(TransferState.Skipped, Assert.Single(writer.Entries).TransferState);
    }

    [Fact]
    public async Task Online_only_cloud_bytes_are_skipped_and_reported()
    {
        using var temp = new TemporaryDirectory(); await File.WriteAllTextAsync(Path.Combine(temp.Path, "cloud.txt"), "bytes");
        var writer = new RecordingWriter();
        await new ManifestScanner(new FakeCloud(CloudContentState.OnlineOnly)).ScanAsync(Request(temp.Path), writer);
        Assert.Equal(ErrorCategory.CloudContentUnavailable, Assert.Single(writer.Entries).Error);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_enumeration()
    {
        using var temp = new TemporaryDirectory(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Scanner().ScanAsync(Request(temp.Path), new RecordingWriter(), cancellationToken: cancellation.Token));
    }

    [Fact]
    public async Task Reparse_directories_are_not_followed()
    {
        using var root = new TemporaryDirectory(); using var outside = new TemporaryDirectory();
        await File.WriteAllTextAsync(Path.Combine(outside.Path, "outside.txt"), "secret");
        try { Directory.CreateSymbolicLink(Path.Combine(root.Path, "link"), outside.Path); }
        catch (UnauthorizedAccessException) { return; }
        var writer = new RecordingWriter(); await Scanner().ScanAsync(Request(root.Path), writer);
        Assert.Empty(writer.Entries);
    }

    [Fact]
    public async Task Ost_cache_is_excluded()
    {
        using var temp = new TemporaryDirectory(); await File.WriteAllTextAsync(Path.Combine(temp.Path, "outlook.ost"), "cache");
        var result = await Scanner().ScanAsync(Request(temp.Path), new RecordingWriter());
        Assert.Equal(1, result.Skipped); Assert.Equal(0, result.Header.EntryCount);
    }

    private static ManifestScanner Scanner() => new(new FakeCloud(CloudContentState.LocallyAvailable));
    private static ManifestScanRequest Request(string path) => new(Guid.NewGuid(), "profile", [new(KnownFolderKind.Documents, path, true, KnownFolderResolution.Resolved, "test")], VerificationLevel.Standard, ConflictPolicy.KeepBoth, "manifest");
    private sealed class FakeCloud(CloudContentState state) : ICloudPlaceholderDetector { public CloudContentState Detect(string path) => state; }
    private sealed class RecordingWriter : IManifestWriter
    {
        public List<MigrationManifestEntry> Entries { get; } = [];
        public Task WriteHeaderAsync(MigrationManifestHeader header, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task WriteEntryAsync(MigrationManifestEntry entry, CancellationToken cancellationToken = default) { Entries.Add(entry); return Task.CompletedTask; }
        public Task CompleteAsync(MigrationManifestFooter footer, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RoboTransferTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); }
        public string Path { get; }
        public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
