using Microsoft.Extensions.Logging.Abstractions;
using RoboTransfer.App.Services;
using RoboTransfer.App.ViewModels;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
using RoboTransfer.Robocopy;
using RoboTransfer.Verification;
using Xunit;

namespace RoboTransfer.Core.Tests;

public sealed class WizardWorkflowTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "RoboTransferWizard", Guid.NewGuid().ToString("N"));
    private readonly Scanner scanner = new();
    private readonly OperationalWorkflowViewModel operations;
    private readonly MainWindowViewModel wizard;
    private readonly UserProfile profile;
    private readonly PolicyProfile policy = PolicyProfile.Conservative with { AllowExternalStorage = true, AllowRobocopy = true };
    private readonly MigrationPlan recommendation;
    private readonly DestinationOption destination;

    public WizardWorkflowTests()
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "work.txt"), "My work");
        File.WriteAllText(Path.Combine(source, "other.txt"), "Other work");
        profile = new("employee", "Employee", source, false, ProfileClassification.InteractiveUser, true,
            [new(KnownFolderKind.Documents, source, true, KnownFolderResolution.Resolved, "Test"),
             new(KnownFolderKind.Videos, null, false, KnownFolderResolution.Unresolved, "Unavailable")]);
        destination = new("Test drive", new(MigrationRoute.ExternalStorage, Path.Combine(root, "destination"), 1_000_000), null);
        recommendation = new(MigrationRoute.ExternalStorage, MigrationStrategy.RobocopyKnownFolders, MigrationStatus.Ready, VerificationLevel.Strong, ConflictPolicy.KeepBoth, [], destination.Destination);
        var reader = new JsonLinesManifestReader();
        var journal = new JsonMigrationJournal(Path.Combine(root, "sessions"), NullLogger<JsonMigrationJournal>.Instance);
        var validator = new AllowDestination();
        var coordinator = new MigrationWorkflowCoordinator(scanner, reader, journal, new MigrationRecovery(journal, validator, reader),
            new JsonExecutionPlanStore(Path.Combine(root, "plans")), validator, new ManifestTransferReconciler(reader),
            new KeepBothTransferEngineFactory(reader), new FileVerificationEngine(reader), new JsonVerificationStore(Path.Combine(root, "verification")),
            new JsonOperationalRecordStore(Path.Combine(root, "operations")), new MigrationReportGenerator(), root);
        operations = new(coordinator);
        wizard = new(null!, null!, null!, operations);
        operations.Configure(profile, policy, recommendation, new("Robocopy", CapabilityState.Available, "robocopy.exe", "test"), [destination]);
    }

    private Task Run(System.Windows.Input.ICommand command) => ((AsyncCommand)command).ExecuteAsync(TestContext.Current.CancellationToken);

    [Fact]
    public async Task Disabled_copy_policy_cannot_be_overridden_by_a_ready_recommendation()
    {
        operations.Configure(profile, policy with { AllowRobocopy = false }, recommendation, new("Robocopy", CapabilityState.Available, "robocopy.exe", "test"), [destination]);
        await Run(operations.StartScanCommand);
        operations.ReviewConfirmed = true;
        await Run(operations.LockPlanCommand);
        Assert.False(operations.HasLockedPlan);
        Assert.False(operations.CanTransfer);
    }

    [Fact]
    public async Task Unsupported_usmt_execution_is_not_silently_replaced_by_file_copy()
    {
        operations.Configure(profile, policy, recommendation with { Strategy = MigrationStrategy.Usmt }, new("Robocopy", CapabilityState.Available, "robocopy.exe", "test"), [destination]);
        await Run(operations.StartScanCommand);
        operations.ReviewConfirmed = true;
        await Run(operations.LockPlanCommand);
        Assert.False(operations.HasLockedPlan);
        Assert.False(operations.CanTransfer);
    }

    [Fact]
    public async Task Verification_cannot_start_before_a_completed_transfer()
    {
        await Run(operations.StartScanCommand);
        operations.ReviewConfirmed = true;
        await Run(operations.LockPlanCommand);
        Assert.True(operations.CanTransfer);
        Assert.False(operations.CanVerify);
        Assert.False(wizard.CanContinueToVerification);
        await Run(operations.StartVerificationCommand);
        Assert.Equal("Transfer", operations.Stage);
    }

    [Fact]
    public async Task Changing_folders_invalidates_the_scan_and_confirmation()
    {
        await Run(operations.StartScanCommand);
        operations.ReviewConfirmed = true;
        Assert.True(operations.CanLockPlan);
        operations.SelectNoneCommand.Execute(null);
        Assert.False(operations.HasCompletedScan);
        Assert.False(operations.ReviewConfirmed);
        Assert.False(operations.CanLockPlan);
        Assert.False(operations.CanScan);
        operations.SelectAllCommand.Execute(null);
        Assert.Equal(1, operations.SelectedFolderCount);
        Assert.False(operations.Folders[1].IsSelected);
        Assert.True(operations.CanScan);
    }

    [Fact]
    public async Task Destination_changes_require_a_new_scan()
    {
        await Run(operations.StartScanCommand);
        operations.ReviewConfirmed = true;
        var other = new DestinationOption("Other drive", destination.Destination with { Location = Path.Combine(root, "other-destination") }, null);
        operations.Destinations.Add(other);
        operations.SelectedDestination = other;
        Assert.False(operations.HasCompletedScan);
        Assert.False(operations.ReviewConfirmed);
        Assert.False(operations.CanLockPlan);
    }

    [Fact]
    public async Task Busy_scan_blocks_navigation_and_selection_until_cancelled()
    {
        wizard.SelectSenderCommand.Execute(null);
        scanner.Block = true;
        var running = Run(operations.StartScanCommand);
        Assert.True(operations.IsBusy);
        wizard.BackCommand.Execute(null);
        wizard.StartOverCommand.Execute(null);
        operations.SelectNoneCommand.Execute(null);
        operations.Folders[0].IsSelected = false;
        Assert.Equal(1, wizard.CurrentPage);
        Assert.True(operations.Folders[0].IsSelected);
        operations.CancelCommand.Execute(null);
        await running;
        Assert.False(operations.IsBusy);
        Assert.False(operations.HasCompletedScan);
        Assert.False(operations.CanLockPlan);
    }

    [Fact]
    public async Task Locked_plan_prevents_selection_and_back_navigation()
    {
        wizard.SelectSenderCommand.Execute(null);
        await Run(operations.StartScanCommand);
        operations.ReviewConfirmed = true;
        await Run(operations.LockPlanCommand);
        Assert.Equal(4, wizard.CurrentPage);
        operations.Folders[0].IsSelected = false;
        Assert.True(operations.Folders[0].IsSelected);
        Assert.True(operations.HasLockedPlan);
        wizard.BackCommand.Execute(null);
        Assert.Equal(4, wizard.CurrentPage);
        Assert.False(wizard.CanGoBack);
    }

    [Fact]
    public async Task Complete_workflow_copies_verifies_and_resets_without_stale_evidence()
    {
        wizard.SelectSenderCommand.Execute(null);
        await Run(operations.StartScanCommand);
        Assert.Equal(3, wizard.CurrentPage);
        operations.ReviewConfirmed = true;
        await Run(operations.LockPlanCommand);
        await Run(operations.StartTransferCommand);
        Assert.True(operations.HasCompletedTransfer);
        Assert.True(operations.CanVerify);
        Assert.Equal(5, wizard.CurrentPage);
        Assert.Equal("My work", File.ReadAllText(Path.Combine(root, "destination", "Documents", "work.txt")));
        await Run(operations.StartVerificationCommand);
        Assert.True(operations.IsVerified);
        Assert.True(operations.HasCleanVerification);
        Assert.Equal(6, wizard.CurrentPage);
        await Run(operations.GenerateReportCommand);
        Assert.True(File.Exists(operations.ReportLocation));
        wizard.StartOverCommand.Execute(null);
        Assert.Equal(0, wizard.CurrentPage);
        Assert.False(operations.HasLockedPlan);
        Assert.False(operations.CanGenerateReport);
        Assert.False(operations.CanVerify);
        Assert.Equal("Not generated", operations.ReportLocation);
        Assert.Empty(operations.Folders);
    }

    [Fact]
    public async Task Retry_keeps_previous_verified_files_in_the_final_evidence()
    {
        await Run(operations.StartScanCommand);
        operations.ReviewConfirmed = true;
        await Run(operations.LockPlanCommand);
        await Run(operations.StartTransferCommand);
        var target = Path.Combine(root, "destination", "Documents", "work.txt");
        var source = Path.Combine(root, "source", "work.txt");
        File.WriteAllText(target, "My fail");
        File.SetLastWriteTimeUtc(target, File.GetLastWriteTimeUtc(source));
        await Run(operations.StartVerificationCommand);
        Assert.False(operations.IsVerified);
        Assert.True(operations.CanRetryVerification);
        Assert.Equal("This migration needs attention", operations.ResultTitle);
        File.Copy(source, target, true);
        await Run(operations.RetryVerificationCommand);
        Assert.True(operations.HasCleanVerification);
        var record = Directory.GetFiles(Path.Combine(root, "verification"), "*.json").Single();
        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(record));
        Assert.Equal(2, json.RootElement.GetProperty("Result").GetProperty("Entries").GetArrayLength());
    }

    [Fact]
    public void Commands_cannot_skip_setup_or_enable_unimplemented_receiver()
    {
        wizard.SelectSenderCommand.Execute(null);
        wizard.ContinueCommand.Execute(null);
        Assert.Equal(1, wizard.CurrentPage);
        wizard.StartOverCommand.Execute(null);
        wizard.SelectReceiverCommand.Execute(null);
        Assert.Equal(0, wizard.CurrentPage);
    }

    private sealed class AllowDestination : IDestinationValidator
    {
        public Task<DestinationValidationResult> ValidateAsync(DestinationValidationContext context, CancellationToken cancellationToken = default) => Task.FromResult(new DestinationValidationResult(true, []));
    }
    private sealed class Scanner : IManifestScanner
    {
        public bool Block { get; set; }
        public async Task<ManifestScanResult> ScanAsync(ManifestScanRequest request, IManifestWriter writer, IProgress<ManifestScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (Block) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var files = new[] { "work.txt", "other.txt" }.Select(name => new FileInfo(Path.Combine(request.KnownFolders[0].Path!, name))).ToArray();
            var bytes = files.Sum(file => file.Length);
            var header = new MigrationManifestHeader(request.SessionId, DateTimeOffset.UtcNow, files.Length, bytes, request.Verification, request.ConflictPolicy);
            await writer.WriteHeaderAsync(header, cancellationToken);
            foreach (var file in files)
                await writer.WriteEntryAsync(new(file.Name, file.Length, file.LastWriteTimeUtc, file.Attributes, KnownFolderKind.Documents, CloudContentState.LocallyAvailable, TransferState.Pending, VerificationState.NotVerified, null, null), cancellationToken);
            await writer.CompleteAsync(new(request.SessionId, DateTimeOffset.UtcNow, files.Length, bytes, files.Length, bytes, 0, 0, ManifestCompletionState.Complete), cancellationToken);
            return new(header, request.ManifestReference, 0, 0, false);
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
