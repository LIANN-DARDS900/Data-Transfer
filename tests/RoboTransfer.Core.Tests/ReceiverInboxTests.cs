using System.Text.Json;
using RoboTransfer.App.Services;
using RoboTransfer.App.ViewModels;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
using Xunit;
namespace RoboTransfer.Core.Tests;

public sealed class ReceiverInboxTests : IDisposable
{
    private const string Share = @"\\NEWPC\Migration";
    private readonly string root = Path.Combine(Path.GetTempPath(), "RoboTransferReceiver", Guid.NewGuid().ToString("N"));
    private readonly TestClock clock = new();
    private readonly PolicyProfile policy = PolicyProfile.Conservative with { AllowConfiguredNetworkShare = true, AllowRobocopy = true, ApprovedNetworkSharePaths = [Share] };
    private readonly UserProfile profile = new("profile-1", "Employee", @"C:\Users\Employee", false, ProfileClassification.InteractiveUser, true, []);
    private readonly TestEnvironment receiverEnvironment, senderEnvironment;
    private readonly ReceiverInboxService receiver, sender;
    public ReceiverInboxTests()
    {
        Directory.CreateDirectory(root);
        receiverEnvironment = new(root, "NEWPC"); senderEnvironment = new(root, "OLDPC");
        receiver = new(receiverEnvironment, clock); sender = new(senderEnvironment, clock);
    }
    private CancellationToken Token => TestContext.Current.CancellationToken;
    private Task<ReceiverOffer> Prepare() => receiver.PrepareAsync(policy, Share, profile, Token);
    private string Access(ReceiverOffer offer) => receiverEnvironment.GetAccessPath(offer.InboxPath);

    [Fact]
    public async Task Receiver_prepares_unique_inboxes_and_sender_reads_identity()
    {
        var first = await Prepare(); var second = await Prepare();
        Assert.NotEqual(first.OfferId, second.OfferId);
        Assert.True(Directory.Exists(Path.Combine(Access(first), "Files")));
        var read = await sender.ReadOfferAsync(policy, first.InboxPath, Token);
        Assert.Equal(first, read);
        Assert.Equal("Employee", read.ReceiverName);
        Assert.Equal(8, first.Code.Length);
    }
    [Fact]
    public async Task Policy_rejection_occurs_before_network_access()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.PrepareAsync(PolicyProfile.Conservative, Share, profile, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.ReadOfferAsync(PolicyProfile.Conservative, Share + "\\RoboTransferInbox\\" + Guid.NewGuid().ToString("N"), Token));
        Assert.Equal(0, receiverEnvironment.AccessCount); Assert.Equal(0, senderEnvironment.AccessCount);
    }
    [Fact]
    public async Task Remote_share_cannot_be_presented_as_this_receiving_pc()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.PrepareAsync(policy, Share, profile, Token));
        Assert.Equal(0, senderEnvironment.AccessCount);
    }
    [Theory]
    [InlineData(@"C:\Migration")]
    [InlineData(@"\\NEWPC\Migration\..\Other")]
    [InlineData(@"\\?\UNC\NEWPC\Migration")]
    [InlineData(@"\\NEWPC\Migration\file:stream")]
    [InlineData(@"\\NEWPC\Migration\trailing.")]
    [InlineData(@"\\NEWPC\Migration\bad ")]
    [InlineData("//NEWPC/Migration")]
    public void Unsafe_paths_are_rejected(string path) => Assert.Throws<InvalidDataException>(() => ReceiverInboxPaths.Normalize(path));

    [Fact]
    public async Task Similar_share_names_do_not_match_policy()
    {
        var path = @"\\NEWPC\Migration-other\RoboTransferInbox\" + Guid.NewGuid().ToString("N");
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.ReadOfferAsync(policy, path, Token));
        Assert.Equal(0, senderEnvironment.AccessCount);
    }
    [Fact]
    public async Task Changed_receiver_identity_invalidates_confirmation()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer);
        await File.WriteAllTextAsync(Path.Combine(Access(offer), "receiver.json"), JsonSerializer.Serialize(offer with { ReceiverName = "Someone else" }), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => sender.ClaimAsync(policy, binding, Guid.NewGuid(), 0, Token));
    }
    [Fact]
    public async Task Only_one_sender_session_can_claim_the_inbox()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer); var first = Guid.NewGuid();
        await sender.ClaimAsync(policy, binding, first, 100, Token);
        await sender.ClaimAsync(policy, binding, first, 100, Token); // Validated same-session retry.
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.ClaimAsync(policy, binding, Guid.NewGuid(), 100, Token));
    }
    [Fact]
    public async Task Simultaneous_senders_cannot_both_claim_one_inbox()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer);
        async Task<bool> TryClaim()
        {
            try { await sender.ClaimAsync(policy, binding, Guid.NewGuid(), 100, Token); return true; }
            catch (InvalidOperationException) { return false; }
        }
        var results = await Task.WhenAll(TryClaim(), TryClaim());
        Assert.Single(results, success => success);
    }
    [Fact]
    public async Task A_claimed_session_cannot_be_resumed_from_another_source_machine()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer); var id = Guid.NewGuid();
        await sender.ClaimAsync(policy, binding, id, 0, Token);
        var otherPc = new ReceiverInboxService(new TestEnvironment(root, "OTHERPC"), clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => otherPc.ValidateAsync(policy, binding, id, 0, Token));
    }
    [Fact]
    public async Task Revoking_share_policy_blocks_existing_migration_before_access()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer); var id = Guid.NewGuid();
        await sender.ClaimAsync(policy, binding, id, 0, Token);
        var count = senderEnvironment.AccessCount;
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.ValidateAsync(PolicyProfile.Conservative, binding, id, 0, Token));
        Assert.Equal(count, senderEnvironment.AccessCount);
    }
    [Fact]
    public async Task Capacity_must_be_known_and_sufficient_before_claiming()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer);
        senderEnvironment.FreeBytes = null;
        await Assert.ThrowsAsync<IOException>(() => sender.ClaimAsync(policy, binding, Guid.NewGuid(), 100, Token));
        senderEnvironment.FreeBytes = 99;
        await Assert.ThrowsAsync<IOException>(() => sender.ClaimAsync(policy, binding, Guid.NewGuid(), 100, Token));
        Assert.False(File.Exists(Path.Combine(Access(offer), "sender.json")));
    }
    [Fact]
    public async Task Expired_offers_reject_new_senders_but_preserve_existing_resume()
    {
        var first = await Prepare(); var second = await Prepare(); var id = Guid.NewGuid();
        await sender.ClaimAsync(policy, ReceiverInboxService.Bind(first), id, 0, Token);
        clock.Advance(TimeSpan.FromHours(13));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.ReadOfferAsync(policy, second.InboxPath, Token));
        await sender.ValidateAsync(policy, ReceiverInboxService.Bind(first), id, 0, Token);
    }
    [Fact]
    public async Task Closing_an_unused_inbox_preserves_files_and_blocks_senders()
    {
        var offer = await Prepare();
        await receiver.CloseAsync(policy, offer, Token);
        Assert.True(Directory.Exists(Access(offer)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.ReadOfferAsync(policy, offer.InboxPath, Token));
    }
    [Fact]
    public async Task Claimed_inboxes_remain_available_for_completion_and_recovery()
    {
        var offer = await Prepare(); await sender.ClaimAsync(policy, ReceiverInboxService.Bind(offer), Guid.NewGuid(), 0, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.CloseAsync(policy, offer, Token));
    }
    [Fact]
    public async Task Receiver_reads_measured_progress_and_rejects_foreign_status()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer); var id = Guid.NewGuid();
        await sender.ClaimAsync(policy, binding, id, 0, Token);
        await sender.PublishAsync(policy, binding, id, ReceiverPhase.Transferring, 42, 12345, Token);
        var status = await receiver.ReadStatusAsync(policy, offer, Token);
        Assert.NotNull(status); Assert.Equal(42, status.Files); Assert.Equal(12345, status.Bytes);
        await File.WriteAllTextAsync(Path.Combine(Access(offer), "status.json"), JsonSerializer.Serialize(status with { SessionId = Guid.NewGuid() }), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.ReadStatusAsync(policy, offer, Token));
    }
    [Fact]
    public async Task Oversized_and_unknown_metadata_fail_closed()
    {
        var offer = await Prepare();
        await File.WriteAllTextAsync(Path.Combine(Access(offer), "receiver.json"), new string('x', 65537), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => sender.ReadOfferAsync(policy, offer.InboxPath, Token));
    }
    [Fact]
    public async Task Reparse_inboxes_are_rejected_when_links_are_available()
    {
        var offer = await Prepare(); var path = Access(offer); var alternate = Path.Combine(root, "alternate"); Directory.CreateDirectory(alternate);
        Directory.Delete(path, true);
        try { Directory.CreateSymbolicLink(path, alternate); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or PlatformNotSupportedException) { return; }
        await Assert.ThrowsAsync<InvalidDataException>(() => sender.ReadOfferAsync(policy, offer.InboxPath, Token));
    }
    [Fact]
    public async Task Both_reports_are_published_only_for_the_claimed_migration()
    {
        var offer = await Prepare(); var binding = ReceiverInboxService.Bind(offer); var id = Guid.NewGuid();
        await sender.ClaimAsync(policy, binding, id, 0, Token);
        var json = Path.Combine(root, "local-report.json"); var html = Path.Combine(root, "local-report.html");
        await File.WriteAllTextAsync(json, "{}", Token); await File.WriteAllTextAsync(html, "<p>Verified report</p>", Token);
        await sender.PublishReportsAsync(policy, binding, id, json, html, Token);
        Assert.Equal("{}", await File.ReadAllTextAsync(Path.Combine(Access(offer), "report.json"), Token));
        Assert.True(File.Exists(Path.Combine(Access(offer), "report.html")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sender.PublishReportsAsync(policy, binding, Guid.NewGuid(), json, html, Token));
    }
    [Fact]
    public async Task Sender_must_compare_the_code_and_confirm_the_recipient()
    {
        var offer = await Prepare(); var vm = new ReceiverWorkflowViewModel(sender);
        vm.Configure(policy, [profile], [Share]); vm.InboxInput = offer.InboxPath;
        await ((AsyncCommand)vm.CheckCommand).ExecuteAsync(Token);
        Assert.True(vm.HasCheckedReceiver);
        var connected = false; vm.SenderConnected += _ => connected = true;
        vm.RecipientConfirmed = true; vm.ComparisonCode = "WRONG";
        await ((AsyncCommand)vm.UseReceiverCommand).ExecuteAsync(Token);
        Assert.False(connected);
        vm.ComparisonCode = offer.Code;
        await ((AsyncCommand)vm.UseReceiverCommand).ExecuteAsync(Token);
        Assert.True(connected);
        vm.InboxInput = "different";
        Assert.False(vm.HasCheckedReceiver); Assert.False(vm.CanUseReceiver);
    }
    [Fact]
    public async Task Receiver_ui_prepares_reopens_and_reads_the_same_inbox()
    {
        var vm = new ReceiverWorkflowViewModel(receiver);
        vm.Configure(policy, [profile], [Share]);
        await ((AsyncCommand)vm.PrepareCommand).ExecuteAsync(Token);
        Assert.True(vm.HasInbox);
        var reopened = new ReceiverWorkflowViewModel(receiver);
        reopened.Configure(policy, [profile], [Share]); reopened.InboxInput = vm.InboxPath;
        await ((AsyncCommand)reopened.ReopenCommand).ExecuteAsync(Token);
        Assert.True(reopened.HasInbox);
        Assert.Equal(vm.InboxCode, reopened.InboxCode);
        await reopened.RefreshStatusAsync(Token);
        Assert.Equal("Waiting for the old PC", reopened.SenderStatusText);
    }
    [Fact]
    public async Task Confirmed_receiver_runs_real_copy_strong_verification_and_both_reports()
    {
        var offer = await Prepare();
        var sourceEnvironment = new TestEnvironment(root, Environment.MachineName);
        var transport = new ReceiverInboxService(sourceEnvironment, clock);
        var source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
        await File.WriteAllTextAsync(Path.Combine(source, "work.txt"), "Employee work", Token);
        var selected = profile with { KnownFolders = [new(KnownFolderKind.Documents, source, true, KnownFolderResolution.Resolved, "Test fixture")] };
        var reader = new JsonLinesManifestReader();
        var journal = new JsonMigrationJournal(Path.Combine(root, "sessions"), Microsoft.Extensions.Logging.Abstractions.NullLogger<JsonMigrationJournal>.Instance);
        var validator = new AllowDestination();
        var coordinator = new MigrationWorkflowCoordinator(new TestScanner(), reader, journal, new MigrationRecovery(journal, validator, reader),
            new JsonExecutionPlanStore(Path.Combine(root, "plans")), validator, new RoboTransfer.Robocopy.ManifestTransferReconciler(reader), new KeepBothTransferEngineFactory(reader),
            new RoboTransfer.Verification.FileVerificationEngine(reader), new JsonVerificationStore(Path.Combine(root, "verification")), new JsonOperationalRecordStore(Path.Combine(root, "operations")), new MigrationReportGenerator(), root, transport);
        var vm = new OperationalWorkflowViewModel(coordinator);
        var destination = new DestinationOption("Approved network", new(MigrationRoute.ConfiguredNetworkShare, Share, null), null);
        vm.Configure(selected, policy, new(MigrationRoute.ConfiguredNetworkShare, MigrationStrategy.RobocopyKnownFolders, MigrationStatus.Ready, VerificationLevel.Strong, ConflictPolicy.KeepBoth, [], destination.Destination), new("Robocopy", CapabilityState.Available, "robocopy.exe", "test"), [destination]);
        vm.AttachReceiver(offer);
        async Task Run(System.Windows.Input.ICommand command) => await ((AsyncCommand)command).ExecuteAsync(Token);
        await Run(vm.StartScanCommand); vm.ReviewConfirmed = true;
        await Run(vm.LockPlanCommand);
        Assert.True(vm.HasLockedPlan);
        var savedPlanPath = Directory.GetFiles(Path.Combine(root, "plans"), "*.plan.json").Single();
        var savedPlan = JsonSerializer.Deserialize<MigrationExecutionPlan>(await File.ReadAllTextAsync(savedPlanPath, Token))!;
        Assert.Equal(ReceiverInboxService.Bind(offer), savedPlan.Receiver);
        var beforeWrongProfile = sourceEnvironment.AccessCount;
        var wrongProfile = selected with { Id = "different-employee" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.TransferAsync(savedPlan, wrongProfile, policy, null, new Progress<TransferProgress>(), Token));
        Assert.Equal(beforeWrongProfile, sourceEnvironment.AccessCount);
        var savedSession = await journal.LoadAsync(savedPlan.SessionId, Token);
        var wrongResume = await coordinator.ValidateResumeAsync(savedSession!, wrongProfile, policy, null, new("Robocopy", CapabilityState.Available), Token);
        Assert.False(wrongResume.Validation.IsValid);
        Assert.Equal(beforeWrongProfile, sourceEnvironment.AccessCount);
        await Run(vm.StartTransferCommand);
        Assert.True(vm.HasCompletedTransfer, vm.Status);
        Assert.Equal("Employee work", await File.ReadAllTextAsync(Path.Combine(Access(offer), "Files", "Documents", "work.txt"), Token));
        Assert.Equal(ReceiverPhase.CopyComplete, (await receiver.ReadStatusAsync(policy, offer, Token))!.Phase);
        await Run(vm.StartVerificationCommand);
        Assert.True(vm.HasCleanVerification, vm.Status);
        Assert.Equal(ReceiverPhase.Verified, (await receiver.ReadStatusAsync(policy, offer, Token))!.Phase);
        await Run(vm.GenerateReportCommand);
        Assert.True(File.Exists(Path.Combine(Access(offer), "report.html")));
        Assert.True(File.Exists(Path.Combine(Access(offer), "report.json")));
    }
    private sealed class AllowDestination : IDestinationValidator
    {
        public Task<DestinationValidationResult> ValidateAsync(DestinationValidationContext context, CancellationToken cancellationToken = default) => Task.FromResult(new DestinationValidationResult(true, []));
    }
    private sealed class TestScanner : IManifestScanner
    {
        public async Task<ManifestScanResult> ScanAsync(ManifestScanRequest request, IManifestWriter writer, IProgress<ManifestScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var file = new FileInfo(Path.Combine(request.KnownFolders[0].Path!, "work.txt"));
            var header = new MigrationManifestHeader(request.SessionId, DateTimeOffset.UtcNow, 1, file.Length, request.Verification, request.ConflictPolicy);
            await writer.WriteHeaderAsync(header, cancellationToken);
            await writer.WriteEntryAsync(new(file.Name, file.Length, file.LastWriteTimeUtc, file.Attributes, KnownFolderKind.Documents, CloudContentState.LocallyAvailable, TransferState.Pending, VerificationState.NotVerified, null, null), cancellationToken);
            await writer.CompleteAsync(new(request.SessionId, DateTimeOffset.UtcNow, 1, file.Length, 1, file.Length, 0, 0, ManifestCompletionState.Complete), cancellationToken);
            return new(header, request.ManifestReference, 0, 0, false);
        }
    }
    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }
    private sealed class TestEnvironment(string root, string machineName) : IReceiverEnvironment
    {
        public int AccessCount { get; private set; }
        public long? FreeBytes { get; set; } = 1_000_000;
        public string MachineName => machineName;
        public bool IsLocalHost(string host) => string.Equals(host, machineName, StringComparison.OrdinalIgnoreCase);
        public string GetAccessPath(string unc)
        {
            AccessCount++;
            var normalized = ReceiverInboxPaths.Normalize(unc);
            if (!normalized.StartsWith(Share, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unmapped test path.");
            return Path.Combine(root, normalized[Share.Length..].TrimStart('\\').Replace('\\', Path.DirectorySeparatorChar));
        }
        public long? GetAvailableBytes(string _) => FreeBytes;
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
