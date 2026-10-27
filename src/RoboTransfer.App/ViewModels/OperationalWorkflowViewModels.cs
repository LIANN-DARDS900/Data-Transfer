using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using RoboTransfer.App.Services;
using RoboTransfer.Core;

namespace RoboTransfer.App.ViewModels;

public sealed class KnownFolderSelection(KnownFolder folder, Func<bool>? canEdit = null) : ViewModelBase
{
    private bool selected = folder.Resolution == KnownFolderResolution.Resolved && folder.Exists;
    public KnownFolder Folder { get; } = folder;
    public bool IsEligible => Folder.Resolution == KnownFolderResolution.Resolved && Folder.Exists && Folder.Path is not null;
    public bool IsSelected { get => selected; set { if (IsEligible && (canEdit?.Invoke() ?? true)) Set(ref selected, value); } }
    public string Name => Folder.Kind.ToString();
    public string Description => Folder.Kind switch
    {
        KnownFolderKind.Desktop => "Your desktop files and folders",
        KnownFolderKind.Documents => "Documents, spreadsheets and work files",
        KnownFolderKind.Downloads => "Files saved from the web",
        KnownFolderKind.Pictures => "Photos, screenshots and images",
        KnownFolderKind.Videos => "Videos and recordings",
        KnownFolderKind.Music => "Music and audio files",
        _ => "Saved browser favorites"
    };
    public string Status => IsEligible ? "Available on this PC" : "Unavailable · excluded";
    public string Icon => Folder.Kind switch
    {
        KnownFolderKind.Desktop => "▣", KnownFolderKind.Documents => "≡", KnownFolderKind.Downloads => "↓",
        KnownFolderKind.Pictures => "▧", KnownFolderKind.Videos => "▷", KnownFolderKind.Music => "♪", _ => "☆"
    };
}

public sealed record DestinationOption(string Display, MigrationDestination Destination, StorageVolume? Volume)
{
    public string RouteLabel => Destination.Route == MigrationRoute.ExternalStorage ? "External drive" : "Approved network share";
}

public sealed class OperationalWorkflowViewModel : ViewModelBase
{
    private readonly MigrationWorkflowCoordinator coordinator;
    private UserProfile? profile;
    private PolicyProfile policy = PolicyProfile.Conservative;
    private MigrationPlan? recommendation;
    private ToolCapability? robocopy;
    private StorageVolume? destinationVolume;
    private CancellationTokenSource? operation;
    private ManifestScanResult? scan;
    private MigrationExecutionPlan? plan;
    private ReceiverBinding? receiver;
    private VerificationResult? verificationResult;
    private readonly Stopwatch timer = new();
    private bool transferCompleted;
    private long totalFiles, totalBytes;
    private string stage = "Source", currentFolder = "—", currentSafeFile = "—", reportLocation = "Not generated";
    private long files, bytes, skipped, warnings, failures, mismatches;
    private TimeSpan elapsed;
    private bool busy, reviewConfirmed;
    private string status = "Select an employee profile and the folders to move.";

    public OperationalWorkflowViewModel(MigrationWorkflowCoordinator coordinator)
    {
        this.coordinator = coordinator;
        StartScanCommand = new AsyncCommand(StartScanAsync, Fail);
        CancelCommand = new DelegateCommand(() => operation?.Cancel());
        LockPlanCommand = new AsyncCommand(LockPlanAsync, Fail);
        StartTransferCommand = new AsyncCommand(StartTransferAsync, Fail);
        StartVerificationCommand = new AsyncCommand(token => RunVerificationAsync(null, token), Fail);
        RetryVerificationCommand = new AsyncCommand(RetryVerificationAsync, Fail);
        GenerateReportCommand = new AsyncCommand(GenerateReportAsync, Fail);
        AbandonCommand = new AsyncCommand(AbandonAsync, Fail);
        InspectCommand = new DelegateCommand(() => Status = "Resume revalidates the selected session, profile, policy and destination before copying.");
        ResumeCommand = new AsyncCommand(ResumeAsync, Fail);
        SelectAllCommand = new DelegateCommand(() => SelectFolders(true));
        SelectNoneCommand = new DelegateCommand(() => SelectFolders(false));
    }

    public ObservableCollection<KnownFolderSelection> Folders { get; } = [];
    public ObservableCollection<DestinationOption> Destinations { get; } = [];
    public ObservableCollection<MigrationSession> RecoverableSessions { get; } = [];
    public bool HasRecovery => RecoverableSessions.Count > 0;
    public ICommand StartScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand LockPlanCommand { get; }
    public ICommand StartTransferCommand { get; }
    public ICommand StartVerificationCommand { get; }
    public ICommand RetryVerificationCommand { get; }
    public ICommand GenerateReportCommand { get; }
    public ICommand AbandonCommand { get; }
    public ICommand InspectCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public MigrationSession? SelectedRecovery { get; set; }
    public DestinationOption? SelectedDestination
    {
        get => Destinations.FirstOrDefault(option => option.Destination == recommendation?.Destination);
        set
        {
            if (!CanEditSelection || value is null || recommendation is null || !Destinations.Contains(value) || value == SelectedDestination) return;
            recommendation = recommendation with { Destination = value.Destination, Route = value.Destination.Route };
            destinationVolume = value.Volume;
            receiver = null;
            OnPropertyChanged(nameof(RecipientSummary)); OnPropertyChanged(nameof(HasReceiver));
            InvalidateScan();
            OnPropertyChanged();
            NotifySummaries();
        }
    }
    public string Stage { get => stage; private set => Set(ref stage, value); }
    public string CurrentFolder { get => currentFolder; private set => Set(ref currentFolder, value); }
    public string CurrentSafeFile { get => currentSafeFile; private set => Set(ref currentSafeFile, value); }
    public string ReportLocation { get => reportLocation; private set => Set(ref reportLocation, value); }
    public long Files { get => files; private set { if (Set(ref files, value)) NotifyProgress(); } }
    public long Bytes { get => bytes; private set { if (Set(ref bytes, value)) { OnPropertyChanged(nameof(BytesText)); NotifyProgress(); } } }
    public string BytesText => FormatBytes(Bytes);
    public long Skipped { get => skipped; private set => Set(ref skipped, value); }
    public long Warnings { get => warnings; private set => Set(ref warnings, value); }
    public long Failures { get => failures; private set => Set(ref failures, value); }
    public long Mismatches { get => mismatches; private set => Set(ref mismatches, value); }
    public TimeSpan Elapsed { get => elapsed; private set { if (Set(ref elapsed, value)) OnPropertyChanged(nameof(ElapsedText)); } }
    public string ElapsedText => Elapsed.ToString(@"hh\:mm\:ss");
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) NotifyState(); } }
    public string RecipientSummary => receiver is null ? "Standard approved destination" : $"{receiver.ReceiverMachine} · {receiver.ReceiverName}";
    public bool HasReceiver => receiver is not null;
    public void AttachReceiver(ReceiverOffer offer)
    {
        if (!CanEditSelection || recommendation is null || !policy.AllowConfiguredNetworkShare || !policy.AllowRobocopy || robocopy?.State != CapabilityState.Available)
            throw new InvalidOperationException("Analyze this PC, select the source profile and confirm a permitted copy tool first.");
        var option = new DestinationOption($"{offer.ReceiverMachine} · {offer.ReceiverName}", new(MigrationRoute.ConfiguredNetworkShare, offer.FilesPath, null), null);
        InvalidateScan();
        Destinations.Add(option);
        recommendation = recommendation with { Route = MigrationRoute.ConfiguredNetworkShare, Strategy = MigrationStrategy.RobocopyKnownFolders, Destination = option.Destination };
        destinationVolume = null;
        receiver = RoboTransfer.Persistence.ReceiverInboxService.Bind(offer);
        OnPropertyChanged(nameof(SelectedDestination));
        OnPropertyChanged(nameof(RecipientSummary)); OnPropertyChanged(nameof(HasReceiver));
        NotifySummaries();
    }
    public bool HasLockedPlan => plan is not null;
    public bool HasCompletedTransfer => transferCompleted;
    public bool CanEditSelection => !IsBusy && !HasLockedPlan;
    public bool ReviewConfirmed { get => reviewConfirmed; set { if (Set(ref reviewConfirmed, value)) OnPropertyChanged(nameof(CanLockPlan)); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public int SelectedFolderCount => Folders.Count(f => f.IsSelected && f.IsEligible);
    public string SelectionSummary => $"{SelectedFolderCount} of {Folders.Count(f => f.IsEligible)} available folders selected";
    public bool HasCompletedScan => scan is not null && !IsBusy;
    public bool CanScan => CanEditSelection && profile is not null && SelectedDestination is not null && SelectedFolderCount > 0;
    public bool CanLockPlan => HasCompletedScan && ReviewConfirmed && !HasLockedPlan && recommendation?.Destination is not null;
    public bool CanTransfer => HasLockedPlan && !IsBusy && !transferCompleted;
    public bool CanVerify => HasLockedPlan && transferCompleted && !IsBusy;
    public bool CanRetryVerification => CanVerify && verificationResult?.Entries.Any(entry => entry.Status is not (VerificationEntryStatus.Verified or VerificationEntryStatus.Skipped)) == true;
    public bool CanGenerateReport => verificationResult is not null && !IsBusy;
    public bool IsVerified => verificationResult?.State is VerificationRunState.Completed or VerificationRunState.CompletedWithWarnings;
    public bool HasCleanVerification => verificationResult?.State == VerificationRunState.Completed && (scan?.Warnings ?? 0) == 0;
    public string ResultSymbol => HasCleanVerification ? "✓" : "!";
    public string ResultTitle => !IsVerified ? "This migration needs attention" : HasCleanVerification ? "Your files are verified" : "Verified with warnings";
    public string ResultDetail => IsVerified ? "Review the report for transferred files, exclusions and any warnings." : "Verification is incomplete or failed. Review the evidence before using the migrated data.";
    public string TransferTitle => transferCompleted ? "Files copied" : IsBusy ? "Moving your files…" : "Ready when you are";
    // Progress counts completed files/bytes from the engine, never a simulated timer.
    public double ProgressPercent => totalBytes > 0 ? Math.Clamp(Bytes * 100d / totalBytes, 0, 100) : totalFiles > 0 ? Math.Clamp(Files * 100d / totalFiles, 0, 100) : 0;
    public string ProgressText => $"{ProgressPercent:0}%";
    public string ProgressDetail => totalFiles > 0 ? $"{Files:N0} / {totalFiles:N0} files · {BytesText} / {FormatBytes(totalBytes)}" : "Waiting for file progress";
    public bool IsProgressIndeterminate => IsBusy && totalFiles == 0 && totalBytes == 0;
    public string VerificationMode => plan?.VerificationRequirement.ToString() ?? policy.RequiredVerification.ToString();
    public string SourceSummary => profile is null ? "No source selected" : $"{Environment.MachineName} · {profile.DisplayName}";
    public string SelectedFoldersSummary => string.Join(", ", Folders.Where(f => f.IsSelected && f.IsEligible).Select(f => f.Name));
    public string DestinationSummary => recommendation?.Destination?.Location ?? "No approved destination";
    public string RouteSummary => SelectedDestination?.RouteLabel ?? "—";
    public string StrategySummary => recommendation?.Strategy.ToString() ?? "—";
    public string ConflictSummary => "Keep both copies";
    public string VerificationSummary => recommendation?.Verification == VerificationLevel.Strong ? "SHA-256 content check" : "Size and timestamp check";
    public string RobocopySummary => robocopy?.Version ?? "Unavailable";

    public void Configure(UserProfile selected, PolicyProfile currentPolicy, MigrationPlan currentPlan, ToolCapability tool, IReadOnlyList<DestinationOption> destinations)
    {
        if (!CanEditSelection) return;
        foreach (var folder in Folders) folder.PropertyChanged -= FolderChanged;
        receiver = null;
        OnPropertyChanged(nameof(RecipientSummary)); OnPropertyChanged(nameof(HasReceiver));
        profile = selected; policy = currentPolicy; recommendation = currentPlan; robocopy = tool;
        Folders.Clear();
        foreach (var folder in selected.KnownFolders)
        {
            var item = new KnownFolderSelection(folder, () => CanEditSelection);
            item.PropertyChanged += FolderChanged;
            Folders.Add(item);
        }
        Destinations.Clear();
        foreach (var destination in destinations) Destinations.Add(destination);
        destinationVolume = SelectedDestination?.Volume;
        InvalidateScan();
        OnPropertyChanged(nameof(SelectedDestination));
        NotifySummaries();
    }

    public void Reset()
    {
        if (IsBusy) return;
        foreach (var folder in Folders) folder.PropertyChanged -= FolderChanged;
        receiver = null;
        OnPropertyChanged(nameof(RecipientSummary)); OnPropertyChanged(nameof(HasReceiver));
        profile = null; recommendation = null; robocopy = null; destinationVolume = null;
        policy = PolicyProfile.Conservative;
        Folders.Clear(); Destinations.Clear();
        InvalidateScan();
        NotifySummaries();
        OnPropertyChanged(nameof(SelectedDestination));
    }

    private void SelectFolders(bool selected)
    {
        if (!CanEditSelection) return;
        foreach (var folder in Folders) folder.IsSelected = selected;
    }
    private void FolderChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(KnownFolderSelection.IsSelected)) return;
        InvalidateScan();
        NotifySummaries();
    }
    private void InvalidateScan()
    {
        scan = null; plan = null; verificationResult = null; transferCompleted = false;
        totalFiles = totalBytes = 0;
        ReviewConfirmed = false;
        Files = Bytes = Skipped = Warnings = Failures = Mismatches = 0;
        Elapsed = TimeSpan.Zero;
        CurrentFolder = CurrentSafeFile = "—";
        ReportLocation = "Not generated";
        Stage = "Source";
        Status = "Selection updated. Scan the selected folders before continuing.";
        NotifyState();
    }

    public async Task DiscoverRecoveryAsync(CancellationToken cancellationToken = default)
    {
        RecoverableSessions.Clear();
        await foreach (var session in coordinator.DiscoverAsync(cancellationToken)) RecoverableSessions.Add(session);
        OnPropertyChanged(nameof(HasRecovery));
    }
    private async Task StartScanAsync(CancellationToken token)
    {
        if (!CanScan || profile is null) return;
        InvalidateScan();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        operation = cancellation; IsBusy = true; Stage = "Scan"; timer.Restart();
        Status = "Looking through your selected folders…";
        var chosen = Folders.Where(f => f.IsEligible && f.IsSelected).Select(f => f.Folder).ToArray();
        var progress = new Progress<ManifestScanProgress>(p =>
        {
            CurrentFolder = p.CurrentFolder?.ToString() ?? "—";
            Files = p.FilesScanned; Bytes = p.BytesScanned; Skipped = p.Skipped; Warnings = p.Warnings; Elapsed = timer.Elapsed;
        });
        try
        {
            scan = await coordinator.ScanAsync(profile, chosen, policy, progress, cancellation.Token);
            Files = scan.Header.EntryCount; Bytes = scan.Header.TotalBytes;
            Skipped = scan.Skipped; Warnings = scan.Warnings;
            Status = $"Scan complete. {scan.Header.EntryCount:N0} files inventoried. Review before copying.";
            Stage = "Migration Plan";
        }
        catch (OperationCanceledException) { Status = "Scan cancelled. Scan again before starting a transfer."; }
        finally { timer.Stop(); Elapsed = timer.Elapsed; operation = null; IsBusy = false; NotifyState(); }
    }
    private async Task LockPlanAsync(CancellationToken token)
    {
        if (!CanLockPlan || scan is null || profile is null || recommendation is null || robocopy is null) return;
        IsBusy = true;
        try
        {
            plan = await coordinator.LockPlanAsync(scan, profile, Folders.Where(f => f.IsSelected && f.IsEligible).Select(f => f.Folder).ToArray(), recommendation, policy, robocopy, destinationVolume, token, receiver);
            (totalFiles, totalBytes) = await coordinator.GetEligibleTotalsAsync(plan, token);
            Files = Bytes = 0;
            Stage = "Transfer";
            Status = "Your plan is locked. Start when both the PC and destination are ready.";
        }
        finally { IsBusy = false; NotifyState(); }
    }
    private async Task StartTransferAsync(CancellationToken token)
    {
        if (!CanTransfer || plan is null || profile is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        operation = cancellation; IsBusy = true; timer.Restart();
        Files = Bytes = Failures = 0; Stage = "Transfer"; Status = "Keep your destination connected while files are copied.";
        var progress = new Progress<TransferProgress>(p =>
        {
            CurrentFolder = p.CurrentFolder?.ToString() ?? "—"; Files = p.FilesProcessed; Bytes = p.BytesProcessed;
            Failures = p.Failed; Elapsed = timer.Elapsed;
        });
        try
        {
            var result = await coordinator.TransferAsync(plan, profile, policy, destinationVolume, progress, cancellation.Token);
            transferCompleted = result.State == TransferCompletionState.TransferCompletedVerificationPending;
            Status = transferCompleted ? "Copy complete. Check file integrity next." : result.State == TransferCompletionState.Interrupted ? "Transfer interrupted. Revalidate and resume this session." : "Transfer could not be completed. Review the destination and retry.";
            Files = result.DestinationFiles; Bytes = result.DestinationBytes;
            Failures = result.Errors.Count;
            if (transferCompleted) Stage = "Verification";
        }
        finally { timer.Stop(); Elapsed = timer.Elapsed; operation = null; IsBusy = false; NotifyState(); }
    }
    private async Task ResumeAsync(CancellationToken token)
    {
        if (IsBusy || HasLockedPlan) return;
        if (SelectedRecovery is null || profile is null || robocopy is null) { Status = "Analyze the PC and select the matching employee and destination before resuming."; return; }
        IsBusy = true;
        try
        {
            var result = await coordinator.ValidateResumeAsync(SelectedRecovery, profile, policy, destinationVolume, robocopy, token);
            if (!result.Validation.IsValid || result.Plan is null) { Status = string.Join(" ", result.Validation.Errors.Select(e => e.TechnicianMessage)); return; }
            plan = result.Plan; receiver = plan.Receiver;
            OnPropertyChanged(nameof(RecipientSummary)); OnPropertyChanged(nameof(HasReceiver));
            transferCompleted = false;
            (totalFiles, totalBytes) = await coordinator.GetEligibleTotalsAsync(plan, token); Files = Bytes = 0;
            Stage = "Transfer"; Status = "Resume checks passed. Start the transfer to continue.";
        }
        finally { IsBusy = false; NotifyState(); }
    }
    private Task RetryVerificationAsync(CancellationToken token)
    {
        if (!CanRetryVerification) return Task.CompletedTask;
        IReadOnlySet<string>? retry = verificationResult?.Entries.Where(entry => entry.Status is not (VerificationEntryStatus.Verified or VerificationEntryStatus.Skipped)).Select(entry => $"{entry.KnownFolder}/{entry.RelativePath}").ToHashSet(StringComparer.OrdinalIgnoreCase);
        return RunVerificationAsync(retry, token);
    }
    private async Task RunVerificationAsync(IReadOnlySet<string>? retry, CancellationToken token)
    {
        if (!CanVerify || plan is null || profile is null) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        operation = cancellation; IsBusy = true; timer.Restart(); Stage = "Verification";
        Files = Bytes = Failures = Mismatches = 0;
        (totalFiles, totalBytes) = await coordinator.GetEligibleTotalsAsync(plan, token);
        // A retry verifies a subset; its denominator is not the whole migration.
        if (retry is not null) { totalFiles = retry.Count; totalBytes = 0; }
        Status = retry is null ? "Checking the copied files against the source…" : "Checking previously failed items…";
        var progress = new Progress<VerificationProgress>(p =>
        {
            Files = p.FilesProcessed; Bytes = p.BytesProcessed; CurrentFolder = p.CurrentFolder?.ToString() ?? "—";
            CurrentSafeFile = p.SafeCurrentFile ?? "—"; Elapsed = p.Elapsed; Failures = p.Failures; Mismatches = p.Mismatches;
        });
        try
        {
            verificationResult = await coordinator.VerifyAsync(plan, profile, policy, retry, progress, cancellation.Token);
            totalFiles = verificationResult.EligibleFiles; totalBytes = verificationResult.EligibleBytes;
            Files = verificationResult.Entries.LongCount(entry => entry.Status != VerificationEntryStatus.Skipped);
            Bytes = verificationResult.Entries.Where(entry => entry.Status != VerificationEntryStatus.Skipped).Sum(entry => entry.ExpectedBytes);
            Failures = verificationResult.Entries.LongCount(entry => entry.Status is not (VerificationEntryStatus.Verified or VerificationEntryStatus.Skipped));
            Mismatches = verificationResult.Mismatches;
            Status = verificationResult.State switch
            {
                VerificationRunState.Completed => "File integrity verified.",
                VerificationRunState.CompletedWithWarnings => "Verification completed with warnings. Review the report.",
                VerificationRunState.Cancelled => "Verification cancelled. Run it again to finish.",
                _ => "Some files could not be verified. Retry failed items or review the report."
            };
            if (IsVerified) Stage = "Report";
        }
        finally { timer.Stop(); Elapsed = timer.Elapsed; operation = null; IsBusy = false; NotifyState(); }
    }
    private async Task GenerateReportAsync(CancellationToken token)
    {
        if (!CanGenerateReport || plan is null) return;
        IsBusy = true;
        try { var result = await coordinator.GenerateReportAsync(plan, token, policy); ReportLocation = result.HtmlPath; Status = "Your JSON and HTML migration reports are saved."; Stage = "Report"; }
        finally { IsBusy = false; }
    }
    private async Task AbandonAsync(CancellationToken token)
    {
        if (SelectedRecovery is null || IsBusy || HasLockedPlan) return;
        await coordinator.AbandonAsync(SelectedRecovery, token);
        RecoverableSessions.Remove(SelectedRecovery); OnPropertyChanged(nameof(HasRecovery));
        Status = "Session abandoned. Source files are preserved.";
    }
    private void Fail(Exception exception) { Status = $"Operation stopped ({exception.GetType().Name}). Review technician logs and retry."; IsBusy = false; NotifyState(); }
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(CanScan), nameof(CanLockPlan), nameof(CanTransfer), nameof(CanVerify), nameof(CanRetryVerification), nameof(CanGenerateReport), nameof(CanEditSelection), nameof(HasLockedPlan), nameof(HasCompletedTransfer), nameof(HasCompletedScan), nameof(IsVerified), nameof(HasCleanVerification), nameof(ResultTitle), nameof(ResultDetail), nameof(ResultSymbol), nameof(TransferTitle) }) OnPropertyChanged(property);
        NotifyProgress();
    }
    private void NotifyProgress()
    {
        foreach (var property in new[] { nameof(ProgressPercent), nameof(ProgressText), nameof(ProgressDetail), nameof(IsProgressIndeterminate) }) OnPropertyChanged(property);
    }
    private void NotifySummaries()
    {
        foreach (var property in new[] { nameof(SourceSummary), nameof(SelectedFoldersSummary), nameof(SelectedFolderCount), nameof(SelectionSummary), nameof(DestinationSummary), nameof(RouteSummary), nameof(StrategySummary), nameof(ConflictSummary), nameof(VerificationSummary), nameof(VerificationMode), nameof(RobocopySummary) }) OnPropertyChanged(property);
        NotifyState();
    }
    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"]; double size = value; var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.#} {units[unit]}";
    }
}

public sealed class DelegateCommand(Action execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
    public event EventHandler? CanExecuteChanged { add { } remove { } }
}
