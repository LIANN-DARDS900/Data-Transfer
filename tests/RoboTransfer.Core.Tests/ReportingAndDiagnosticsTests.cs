using System.Text.Json;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
using Xunit;

namespace RoboTransfer.Core.Tests;

public sealed class ReportingAndDiagnosticsTests
{
    [Theory]
    [InlineData(FinalMigrationStatus.Success)]
    [InlineData(FinalMigrationStatus.SuccessWithWarnings)]
    [InlineData(FinalMigrationStatus.Incomplete)]
    [InlineData(FinalMigrationStatus.VerificationFailed)]
    [InlineData(FinalMigrationStatus.Failed)]
    [InlineData(FinalMigrationStatus.Cancelled)]
    public async Task Json_and_html_preserve_final_status(FinalMigrationStatus status) { using var temp = new Temp(); var paths = await new MigrationReportGenerator().GenerateAsync(Report(status), temp.Path, TestContext.Current.CancellationToken); using var json = JsonDocument.Parse(await File.ReadAllTextAsync(paths.JsonPath, TestContext.Current.CancellationToken)); Assert.Equal(status.ToString(), json.RootElement.GetProperty("FinalStatus").GetString()); Assert.Equal(MigrationRoute.ExternalStorage.ToString(), json.RootElement.GetProperty("Route").GetString()); Assert.Equal(MigrationStrategy.RobocopyKnownFolders.ToString(), json.RootElement.GetProperty("Strategy").GetString()); Assert.Equal(VerificationRunState.Completed.ToString(), json.RootElement.GetProperty("StandardVerification").GetString()); Assert.Contains(status.ToString(), await File.ReadAllTextAsync(paths.HtmlPath, TestContext.Current.CancellationToken)); }
    [Fact]
    public async Task Reports_are_schema_versioned_and_redact_profile_and_destination() { using var temp = new Temp(); var paths = await new MigrationReportGenerator().GenerateAsync(Report(FinalMigrationStatus.Success), temp.Path, TestContext.Current.CancellationToken); using var json = JsonDocument.Parse(await File.ReadAllTextAsync(paths.JsonPath, TestContext.Current.CancellationToken)); Assert.Equal(1, json.RootElement.GetProperty("SchemaVersion").GetInt32()); Assert.DoesNotContain("secret-user", await File.ReadAllTextAsync(paths.JsonPath, TestContext.Current.CancellationToken)); Assert.DoesNotContain("private-folder", await File.ReadAllTextAsync(paths.HtmlPath, TestContext.Current.CancellationToken)); }
    [Fact]
    public async Task Report_preserves_cloud_locked_conflict_and_failure_counts() { using var temp = new Temp(); var paths = await new MigrationReportGenerator().GenerateAsync(Report(FinalMigrationStatus.SuccessWithWarnings), temp.Path, TestContext.Current.CancellationToken); using var json = JsonDocument.Parse(await File.ReadAllTextAsync(paths.JsonPath, TestContext.Current.CancellationToken)); Assert.Equal(1, json.RootElement.GetProperty("SkippedCloudContent").GetInt64()); Assert.Equal(1, json.RootElement.GetProperty("LockedFiles").GetInt64()); Assert.Equal(1, json.RootElement.GetProperty("Conflicts").GetInt64()); }
    [Fact]
    public async Task Verification_store_detects_corruption() { using var temp = new Temp(); var store = new JsonVerificationStore(temp.Path); var result = Verification(); await store.SaveAsync(result, TestContext.Current.CancellationToken); var file = Assert.Single(Directory.EnumerateFiles(temp.Path)); await File.AppendAllTextAsync(file, "altered", TestContext.Current.CancellationToken); await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(result.SessionId, TestContext.Current.CancellationToken)); }
    [Fact]
    public async Task Structured_log_redacts_paths_and_rotates() { using var temp = new Temp(); var log = new RotatingStructuredLog(temp.Path, 100, 3); for (var i = 0; i < 10; i++) await log.WriteAsync(new(DateTimeOffset.UtcNow, Guid.Empty, "Verification", "Warning", "Mismatch", @"C:\Users\secret\file.txt failed"), TestContext.Current.CancellationToken); var content = string.Join("", Directory.EnumerateFiles(temp.Path).Select(File.ReadAllText)); Assert.DoesNotContain("Users\\secret", content); Assert.Contains("[redacted path]", content); Assert.True(Directory.EnumerateFiles(temp.Path).Count() <= 3); }
    private static MigrationReport Report(FinalMigrationStatus status) => new(Guid.NewGuid(), "1.0", DateTimeOffset.UtcNow, "machine", "secret-user", @"C:\private-folder", MigrationRoute.ExternalStorage, MigrationStrategy.RobocopyKnownFolders, [KnownFolderKind.Documents], 2, 10, 2, 10, 1, 1, 1, status is FinalMigrationStatus.Failed or FinalMigrationStatus.VerificationFailed ? 1 : 0, VerificationRunState.Completed, VerificationRunState.Completed, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromSeconds(1), ["warning"], status, "policy", "plan", "manifest", Guid.NewGuid());
    private static VerificationResult Verification() => new(Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, VerificationLevel.Standard, VerificationRunState.Completed, 1, 1, 1, 1, 0, 0, [], "plan", "manifest");
    private sealed class Temp : IDisposable { public Temp() { Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RoboTransferTests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path); } public string Path { get; } public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } } }
}
