using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using RoboTransfer.App.Services;
using RoboTransfer.Core;

namespace RoboTransfer.App.ViewModels;

public sealed class KnownFolderSelection(KnownFolder folder) : ViewModelBase
{
    private bool selected = folder.Resolution == KnownFolderResolution.Resolved && folder.Exists;
    public KnownFolder Folder { get; } = folder;
    public bool IsEligible => Folder.Resolution == KnownFolderResolution.Resolved && Folder.Exists && Folder.Path is not null;
    public bool IsSelected { get => selected; set { if (IsEligible) Set(ref selected, value); } }
    public string Status => IsEligible ? "Resolved · eligible" : $"{Folder.Resolution} · unavailable";
}
public sealed record DestinationOption(string Display, MigrationDestination Destination, StorageVolume? Volume);

public sealed class OperationalWorkflowViewModel : ViewModelBase
{
    private readonly MigrationWorkflowCoordinator coordinator; private UserProfile? profile; private PolicyProfile policy = PolicyProfile.Conservative; private MigrationPlan? recommendation; private ToolCapability? robocopy; private StorageVolume? destinationVolume; private CancellationTokenSource? operation; private ManifestScanResult? scan; private MigrationExecutionPlan? plan; private VerificationResult? verificationResult; private readonly Stopwatch timer = new();
    private string stage = "Source"; private string currentFolder = "—"; private string currentSafeFile = "—"; private string reportLocation = "Not generated"; private long files; private long bytes; private long skipped; private long warnings; private long failures; private long mismatches; private TimeSpan elapsed; private bool busy; private bool reviewConfirmed; private string status = "Select an employee profile and eligible Known Folders.";

    public OperationalWorkflowViewModel(MigrationWorkflowCoordinator coordinator)
    {
        this.coordinator = coordinator; StartScanCommand = new AsyncCommand(StartScanAsync, Fail); CancelCommand = new DelegateCommand(Cancel); LockPlanCommand = new AsyncCommand(LockPlanAsync, Fail); StartTransferCommand = new AsyncCommand(StartTransferAsync, Fail); StartVerificationCommand = new AsyncCommand(StartVerificationAsync, Fail); RetryVerificationCommand = new AsyncCommand(RetryVerificationAsync, Fail); GenerateReportCommand = new AsyncCommand(GenerateReportAsync, Fail); AbandonCommand = new AsyncCommand(AbandonAsync, Fail); InspectCommand = new DelegateCommand(() => Status = "Recovery session selected. Resume will revalidate all identities before transfer."); ResumeCommand = new AsyncCommand(ResumeAsync, Fail);
    }
    public ObservableCollection<KnownFolderSelection> Folders { get; } = []; public ObservableCollection<DestinationOption> Destinations { get; } = []; public ObservableCollection<MigrationSession> RecoverableSessions { get; } = []; public bool HasRecovery => RecoverableSessions.Count > 0;
    public ICommand StartScanCommand { get; } public ICommand CancelCommand { get; } public ICommand LockPlanCommand { get; } public ICommand StartTransferCommand { get; } public ICommand StartVerificationCommand { get; } public ICommand RetryVerificationCommand { get; } public ICommand GenerateReportCommand { get; } public ICommand AbandonCommand { get; } public ICommand InspectCommand { get; } public ICommand ResumeCommand { get; }
    public MigrationSession? SelectedRecovery { get; set; }
    public DestinationOption? SelectedDestination { get => Destinations.FirstOrDefault(option => option.Destination == recommendation?.Destination); set { if (value is null || recommendation is null) return; recommendation = recommendation with { Destination = value.Destination, Route = value.Destination.Route }; destinationVolume = value.Volume; NotifySummaries(); OnPropertyChanged(nameof(CanLockPlan)); } }
    public string Stage { get => stage; private set => Set(ref stage, value); } public string CurrentFolder { get => currentFolder; private set => Set(ref currentFolder, value); }
    public string CurrentSafeFile { get => currentSafeFile; private set => Set(ref currentSafeFile, value); } public string ReportLocation { get => reportLocation; private set => Set(ref reportLocation, value); }
    public long Files { get => files; private set => Set(ref files, value); } public long Bytes { get => bytes; private set { if (Set(ref bytes, value)) OnPropertyChanged(nameof(BytesText)); } } public string BytesText => FormatBytes(Bytes);
    public long Skipped { get => skipped; private set => Set(ref skipped, value); } public long Warnings { get => warnings; private set => Set(ref warnings, value); } public long Failures { get => failures; private set => Set(ref failures, value); } public long Mismatches { get => mismatches; private set => Set(ref mismatches, value); }
    public TimeSpan Elapsed { get => elapsed; private set { if (Set(ref elapsed, value)) OnPropertyChanged(nameof(ElapsedText)); } } public string ElapsedText => Elapsed.ToString(@"hh\:mm\:ss");
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) { OnPropertyChanged(nameof(CanScan)); OnPropertyChanged(nameof(CanTransfer)); OnPropertyChanged(nameof(CanVerify)); OnPropertyChanged(nameof(CanRetryVerification)); OnPropertyChanged(nameof(CanGenerateReport)); } } }
    public bool ReviewConfirmed { get => reviewConfirmed; set { if (Set(ref reviewConfirmed, value)) OnPropertyChanged(nameof(CanLockPlan)); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public bool CanScan => !IsBusy && profile is not null && Folders.Any(f => f.IsEligible && f.IsSelected); public bool CanLockPlan => scan is not null && ReviewConfirmed && recommendation?.Destination is not null; public bool CanTransfer => plan is not null && !IsBusy; public bool CanVerify => plan is not null && !IsBusy; public bool CanRetryVerification => verificationResult?.Entries.Any(entry => entry.Status is not (VerificationEntryStatus.Verified or VerificationEntryStatus.Skipped)) == true && !IsBusy; public bool CanGenerateReport => verificationResult is not null && !IsBusy;
    public string VerificationMode => plan?.VerificationRequirement.ToString() ?? policy.RequiredVerification.ToString();
    public string SourceSummary => profile is null ? "No source selected" : $"{Environment.MachineName} · {profile.DisplayName}"; public string SelectedFoldersSummary => string.Join(", ", Folders.Where(f => f.IsSelected && f.IsEligible).Select(f => f.Folder.Kind));
    public string DestinationSummary => recommendation?.Destination?.Location ?? "No approved destination"; public string RouteSummary => recommendation?.Route.ToString() ?? "—"; public string StrategySummary => recommendation?.Strategy.ToString() ?? "—"; public string ConflictSummary => recommendation?.ConflictPolicy.ToString() ?? "KeepBoth"; public string VerificationSummary => recommendation?.Verification.ToString() ?? "—"; public string RobocopySummary => robocopy?.Version ?? "Unavailable";

    public void Configure(UserProfile selected, PolicyProfile currentPolicy, MigrationPlan currentPlan, ToolCapability tool, IReadOnlyList<DestinationOption> destinations)
    {
        profile = selected; policy = currentPolicy; recommendation = currentPlan; robocopy = tool; Folders.Clear(); foreach (var folder in selected.KnownFolders) Folders.Add(new(folder)); Destinations.Clear(); foreach (var destination in destinations) Destinations.Add(destination); destinationVolume = SelectedDestination?.Volume;
        OnPropertyChanged(nameof(CanScan)); NotifySummaries();
    }
    public async Task DiscoverRecoveryAsync(CancellationToken cancellationToken = default) { RecoverableSessions.Clear(); await foreach (var session in coordinator.DiscoverAsync(cancellationToken)) RecoverableSessions.Add(session); OnPropertyChanged(nameof(HasRecovery)); }
    private async Task StartScanAsync(CancellationToken ignored)
    {
        if (!CanScan || profile is null) return; operation = new(); IsBusy = true; Stage = "Scan"; timer.Restart(); Status = "Scanning selected Known Folders…";
        var chosen = Folders.Where(f => f.IsEligible && f.IsSelected).Select(f => f.Folder).ToArray(); var progress = new Progress<ManifestScanProgress>(p => { CurrentFolder = p.CurrentFolder?.ToString() ?? "—"; Files = p.FilesScanned; Bytes = p.BytesScanned; Skipped = p.Skipped; Warnings = p.Warnings; Elapsed = timer.Elapsed; });
        try { scan = await coordinator.ScanAsync(profile, chosen, policy, progress, operation.Token); Status = $"Scan complete. {scan.Header.EntryCount:N0} entries recorded; review the immutable plan."; Stage = "Migration Plan"; }
        catch (OperationCanceledException) { Status = "Scan cancelled. The incomplete manifest cannot be used for transfer."; Stage = "Scan"; }
        finally { timer.Stop(); Elapsed = timer.Elapsed; IsBusy = false; OnPropertyChanged(nameof(CanLockPlan)); NotifySummaries(); }
    }
    private async Task LockPlanAsync(CancellationToken token) { if (!CanLockPlan || scan is null || profile is null || recommendation is null || robocopy is null) return; plan = await coordinator.LockPlanAsync(scan, profile, Folders.Where(f => f.IsSelected && f.IsEligible).Select(f => f.Folder).ToArray(), recommendation, policy, robocopy, destinationVolume, token); Stage = "Transfer"; Status = "Execution plan locked. Mutable selections can no longer alter this transfer."; OnPropertyChanged(nameof(CanTransfer)); }
    private async Task StartTransferAsync(CancellationToken ignored)
    {
        if (!CanTransfer || plan is null || profile is null) return; operation = new(); IsBusy = true; timer.Restart(); Status = "Transfer in progress…";
        var progress = new Progress<TransferProgress>(p => { CurrentFolder = p.CurrentFolder?.ToString() ?? "—"; Files = p.FilesProcessed; Bytes = p.BytesProcessed; Skipped = p.Skipped; Warnings = p.Warnings; Failures = p.Failed; Elapsed = p.Elapsed ?? timer.Elapsed; });
        try { var result = await coordinator.TransferAsync(plan, profile, policy, destinationVolume, progress, operation.Token); Status = result.State == TransferCompletionState.TransferCompletedVerificationPending ? "Transfer completed. Verification remains pending." : result.State == TransferCompletionState.Interrupted ? "Transfer interrupted. Resume requires revalidation." : "Transfer failed reconciliation. Inspect failures."; Failures = result.Errors.Count; if (result.State == TransferCompletionState.TransferCompletedVerificationPending) Stage = "Verification"; OnPropertyChanged(nameof(CanVerify)); }
        finally { timer.Stop(); IsBusy = false; }
    }
    private async Task ResumeAsync(CancellationToken token) { if (SelectedRecovery is null || profile is null || robocopy is null) { Status = "Analyze the endpoint and select the reviewed source profile before resume."; return; } var result = await coordinator.ValidateResumeAsync(SelectedRecovery, profile, policy, destinationVolume, robocopy, token); if (!result.Validation.IsValid || result.Plan is null) { Status = string.Join(" ", result.Validation.Errors.Select(e => e.TechnicianMessage)); return; } plan = result.Plan; Stage = "Transfer"; Status = "Resume revalidation passed. Select Start transfer to continue safely."; OnPropertyChanged(nameof(CanTransfer)); }
    private Task StartVerificationAsync(CancellationToken token) => RunVerificationAsync(null, token);
    private Task RetryVerificationAsync(CancellationToken token) { IReadOnlySet<string>? retry = verificationResult?.Entries.Where(entry => entry.Status is not (VerificationEntryStatus.Verified or VerificationEntryStatus.Skipped)).Select(entry => $"{entry.KnownFolder}/{entry.RelativePath}").ToHashSet(StringComparer.OrdinalIgnoreCase); return RunVerificationAsync(retry, token); }
    private async Task RunVerificationAsync(IReadOnlySet<string>? retry, CancellationToken token) { if (plan is null || profile is null) return; operation = CancellationTokenSource.CreateLinkedTokenSource(token); IsBusy = true; timer.Restart(); Stage = "Verification"; Status = retry is null ? "Verification in progress…" : "Retrying failed verification items…"; var progress = new Progress<VerificationProgress>(value => { Files = value.FilesProcessed; Bytes = value.BytesProcessed; CurrentFolder = value.CurrentFolder?.ToString() ?? "—"; CurrentSafeFile = value.SafeCurrentFile ?? "—"; Elapsed = value.Elapsed; Failures = value.Failures; Mismatches = value.Mismatches; Skipped = value.Skipped; }); try { verificationResult = await coordinator.VerifyAsync(plan, profile, policy, retry, progress, operation.Token); Status = verificationResult.State switch { VerificationRunState.Completed => "Required verification completed successfully.", VerificationRunState.CompletedWithWarnings => "Verification completed with warnings.", VerificationRunState.Cancelled => "Verification cancelled. Retry is available.", _ => "Verification failed. Retry only the failed subset." }; if (verificationResult.State is VerificationRunState.Completed or VerificationRunState.CompletedWithWarnings) Stage = "Report"; } finally { timer.Stop(); IsBusy = false; OnPropertyChanged(nameof(CanRetryVerification)); OnPropertyChanged(nameof(CanGenerateReport)); } }
    private async Task GenerateReportAsync(CancellationToken token) { if (plan is null || verificationResult is null) return; var result = await coordinator.GenerateReportAsync(plan, token); ReportLocation = result.HtmlPath; Status = "Durable JSON and HTML technician reports generated."; Stage = "Report"; }
    private void Cancel() => operation?.Cancel();
    private async Task AbandonAsync(CancellationToken token) { if (SelectedRecovery is null) return; await coordinator.AbandonAsync(SelectedRecovery, token); RecoverableSessions.Remove(SelectedRecovery); OnPropertyChanged(nameof(HasRecovery)); Status = "Session abandoned. No source files were deleted."; }
    private void Fail(Exception exception) { Status = $"Operation failed safely ({exception.GetType().Name}). Review technician logs."; IsBusy = false; }
    private void NotifySummaries() { foreach (var property in new[] { nameof(SourceSummary), nameof(SelectedFoldersSummary), nameof(DestinationSummary), nameof(RouteSummary), nameof(StrategySummary), nameof(ConflictSummary), nameof(VerificationSummary), nameof(RobocopySummary) }) OnPropertyChanged(property); }
    private static string FormatBytes(long value) { string[] units = ["B", "KB", "MB", "GB", "TB"]; double size = value; var unit = 0; while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; } return $"{size:0.#} {units[unit]}"; }
}

public sealed class DelegateCommand(Action execute) : ICommand { public bool CanExecute(object? parameter) => true; public void Execute(object? parameter) => execute(); public event EventHandler? CanExecuteChanged { add { } remove { } } }
