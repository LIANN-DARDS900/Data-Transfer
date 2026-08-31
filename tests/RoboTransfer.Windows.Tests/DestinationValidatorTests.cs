using RoboTransfer.Core;
using RoboTransfer.Windows;
using Xunit;

namespace RoboTransfer.Windows.Tests;

public sealed class DestinationValidatorTests
{
    [Theory]
    [InlineData("same")]
    [InlineData("destination-child")]
    [InlineData("source-child")]
    public async Task Rejects_overlapping_paths(string scenario)
    {
        using var root = new Temp(); var source = Path.Combine(root.Path, "source"); var destination = Path.Combine(root.Path, "destination"); Directory.CreateDirectory(source); Directory.CreateDirectory(destination);
        (source, destination) = scenario switch { "same" => (source, source), "destination-child" => (source, Path.Combine(source, "child")), _ => (Path.Combine(destination, "child"), destination) };
        var result = await Validator().ValidateAsync(Context(destination, [source])); Assert.False(result.IsValid); Assert.Contains(result.Errors, e => e.Category == ErrorCategory.InvalidPath);
    }
    [Fact]
    public async Task Rejects_application_install_path() { var result = await Validator().ValidateAsync(Context(AppContext.BaseDirectory, [Path.GetTempPath()])); Assert.Contains(result.Errors, e => e.Category == ErrorCategory.PolicyForbidden); }
    [Theory]
    [MemberData(nameof(ProtectedPaths))]
    public async Task Rejects_windows_and_program_files_paths(string protectedPath) { var result = await Validator().ValidateAsync(Context(protectedPath, [Path.Combine(Path.GetTempPath(), "source-not-protected")])); Assert.Contains(result.Errors, e => e.Category == ErrorCategory.PolicyForbidden); }
    public static IEnumerable<object[]> ProtectedPaths() { foreach (var path in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Windows), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Where(p => !string.IsNullOrWhiteSpace(p))) yield return [path]; }
    [Fact]
    public async Task Rejects_disconnected_external_media() { using var temp = new Temp(); var result = await Validator().ValidateAsync(Context(temp.Path, [], volume: null)); Assert.Contains(result.Errors, e => e.Category == ErrorCategory.StorageDisconnected); }
    [Theory]
    [InlineData(false, "disk", "NTFS", 100, ErrorCategory.PolicyForbidden)]
    [InlineData(true, "changed", "NTFS", 100, ErrorCategory.DestinationChanged)]
    [InlineData(true, "disk", null, 100, ErrorCategory.ConfigurationInvalid)]
    [InlineData(true, "disk", "NTFS", 1, ErrorCategory.InsufficientSpace)]
    public async Task Rejects_invalid_media(bool external, string disk, string? fileSystem, long free, ErrorCategory category) { using var temp = new Temp(); var volume = Volume(temp.Path, external, disk, fileSystem, free); var result = await Validator().ValidateAsync(Context(temp.Path, [], volume)); Assert.Contains(result.Errors, e => e.Category == category); }
    [Fact]
    public async Task Rejects_changed_policy() { using var temp = new Temp(); var context = Context(temp.Path, [], Volume(temp.Path, true, "disk", "NTFS", 100)); context = context with { Policy = context.Policy with { SchemaVersion = 2 } }; var result = await Validator().ValidateAsync(context); Assert.Contains(result.Errors, e => e.Category == ErrorCategory.PolicyForbidden); }
    [Fact]
    public async Task Approved_unc_is_allowed_and_unapproved_unc_is_rejected_without_network_access()
    {
        var policy = Policy() with { AllowConfiguredNetworkShare = true, ApprovedNetworkSharePaths = [@"\\server\approved"] }; var approvedPlan = Plan(@"\\server\approved\employee", MigrationRoute.ConfiguredNetworkShare, policy);
        Assert.True((await Validator().ValidateAsync(new(approvedPlan, [@"C:\source"], policy, null))).IsValid);
        var denied = await Validator().ValidateAsync(new(approvedPlan with { DestinationPath = @"\\server\other" }, [@"C:\source"], policy, null)); Assert.Contains(denied.Errors, e => e.Category == ErrorCategory.PolicyForbidden);
    }
    [Fact]
    public async Task Write_probe_cleanup_is_delegated_and_no_artifact_remains() { using var temp = new Temp(); var probe = new RecordingProbe(); var result = await new DestinationValidator(probe).ValidateAsync(Context(temp.Path, [], Volume(temp.Path, true, "disk", "NTFS", 100))); Assert.True(result.IsValid); Assert.Equal(temp.Path, probe.Path); Assert.Empty(Directory.EnumerateFiles(temp.Path, ".robotransfer-write-*")); }
    private static DestinationValidator Validator() => new(new RecordingProbe());
    private static DestinationValidationContext Context(string path, IReadOnlyList<string> sources, StorageVolume? volume = null) { var policy = Policy(); return new(Plan(path, MigrationRoute.ExternalStorage, policy), sources, policy, volume); }
    private static PolicyProfile Policy() => new(1, false, true, false, true, VerificationLevel.Standard, ConflictPolicy.KeepBoth, []);
    private static MigrationExecutionPlan Plan(string path, MigrationRoute route, PolicyProfile policy) => new(Guid.NewGuid(), DateTimeOffset.UtcNow, "machine", "profile", [KnownFolderKind.Documents], "id", "manifest", 1, 10, route, MigrationStrategy.RobocopyKnownFolders, "disk", path, 100, ConflictPolicy.KeepBoth, "skip", VerificationLevel.Standard, policy.SchemaVersion, PolicyFingerprint.Create(policy), "robocopy", "1", "1");
    private static StorageVolume Volume(string path, bool external, string disk, string? fs, long free) => new(path, null, fs, 100, free, StorageKind.Removable, true, external ? AttachmentType.External : AttachmentType.Internal, StorageBusType.Usb, disk);
    private sealed class RecordingProbe : IDestinationWriteProbe { public string? Path { get; private set; } public Task<bool> IsWritableAsync(string path, bool requireExisting, CancellationToken cancellationToken = default) { Path = path; return Task.FromResult(true); } }
    private sealed class Temp : IDisposable { public Temp() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RoboTransferTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); } public string Path { get; } public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } } }
}
