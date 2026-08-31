using System.Collections.ObjectModel;
using System.Windows.Input;
using RoboTransfer.Core;
namespace RoboTransfer.App.ViewModels;

public sealed record WorkflowStep(string Number, string Name, bool IsCurrent, bool IsAvailable);
public sealed record EnvironmentRow(string Label, string Value, string Detail, string BadgeClass)
{
    public bool IsAvailable => BadgeClass == "available";
    public bool IsWarning => BadgeClass == "warning";
    public bool IsBlocked => BadgeClass == "blocked";
}

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ICapabilityDetector detector; private readonly IMigrationPlanner planner; private readonly IPolicyProvider policies;
    private PolicyProfile? activePolicy; private MigrationPlan? activePlan; private ToolCapability? activeRobocopy; private IReadOnlyList<DestinationOption> activeDestinations = [];
    private bool isAnalyzing; private string? errorMessage; private string recommendation = "Analysis required"; private string recommendationDetail = "Refresh the endpoint analysis to identify policy-approved options."; private string policySummary = "Policy not loaded"; private MigrationRole role = MigrationRole.OldPc; private UserProfile? selectedProfile;
    public MainWindowViewModel(ICapabilityDetector detector, IMigrationPlanner planner, IPolicyProvider policies, OperationalWorkflowViewModel operations)
    {
        this.detector = detector; this.planner = planner; this.policies = policies; Operations = operations; RefreshCommand = new AsyncCommand(RefreshAsync, ShowError);
        Workflow = [new("1", "Environment", true, true), new("2", "Source", false, true), new("3", "Scan", false, true), new("4", "Migration plan", false, true), new("5", "Transfer", false, false), new("6", "Verification", false, false), new("7", "Report", false, false)];
    }
    public OperationalWorkflowViewModel Operations { get; } public IReadOnlyList<WorkflowStep> Workflow { get; } public IReadOnlyList<MigrationRole> Roles { get; } = Enum.GetValues<MigrationRole>();
    public ObservableCollection<EnvironmentRow> EnvironmentRows { get; } = []; public ObservableCollection<UserProfile> Profiles { get; } = [];
    public ICommand RefreshCommand { get; }
    public bool IsAnalyzing { get => isAnalyzing; private set { if (Set(ref isAnalyzing, value)) { OnPropertyChanged(nameof(AnalysisButtonText)); OnPropertyChanged(nameof(HasAnalysis)); } } }
    public bool HasAnalysis => EnvironmentRows.Count > 0; public string AnalysisButtonText => IsAnalyzing ? "Analyzing…" : "Refresh analysis";
    public string? ErrorMessage { get => errorMessage; private set { if (Set(ref errorMessage, value)) OnPropertyChanged(nameof(HasError)); } } public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string Recommendation { get => recommendation; private set => Set(ref recommendation, value); } public string RecommendationDetail { get => recommendationDetail; private set => Set(ref recommendationDetail, value); } public string PolicySummary { get => policySummary; private set => Set(ref policySummary, value); }
    public MigrationRole Role { get => role; set => Set(ref role, value); } public UserProfile? SelectedProfile { get => selectedProfile; set { if (Set(ref selectedProfile, value)) { OnPropertyChanged(nameof(ProfileDetail)); ConfigureOperations(); } } }
    public string ProfileDetail => SelectedProfile is null ? "Choose the registered employee profile to assess. No files are read or transferred." : $"{SelectedProfile.KnownFolders.Count(folder => folder.Resolution == KnownFolderResolution.Resolved)} known folders resolved authoritatively";

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsAnalyzing = true; ErrorMessage = null;
        try
        {
            var loaded = await policies.LoadAsync(cancellationToken); var policy = loaded.IsValid ? loaded.Policy : PolicyProfile.Conservative;
            PolicySummary = loaded.IsValid ? $"Policy schema {policy.SchemaVersion} validated" : "Conservative mode — policy missing or invalid";
            var capabilities = await detector.DetectAsync(policy, cancellationToken); var plan = planner.CreatePlan(policy, capabilities); activePolicy = policy; activePlan = plan; activeRobocopy = capabilities.Robocopy; activeDestinations = [.. capabilities.ExternalCandidates.Where(_ => policy.AllowExternalStorage).Select(v => new DestinationOption($"{v.Label ?? v.RootPath} · {FormatBytes(v.AvailableBytes)} free", new(MigrationRoute.ExternalStorage, v.RootPath, v.AvailableBytes), v)), .. capabilities.ApprovedShares.Where(s => s.State == CapabilityState.Available && policy.AllowConfiguredNetworkShare).Select(s => new DestinationOption(s.Path, new(MigrationRoute.ConfiguredNetworkShare, s.Path, null), null))];
            EnvironmentRows.Clear();
            EnvironmentRows.Add(Row("Windows endpoint", capabilities.OperatingSystem.Description, capabilities.OperatingSystem.IsElevated switch { true => "Administrator context", false => "Standard-user context", _ => "Elevation unknown" }, OperatingSystem.IsWindows() ? CapabilityState.Available : CapabilityState.Unknown));
            EnvironmentRows.Add(Row("Robocopy", StateText(capabilities.Robocopy.State), capabilities.Robocopy.Detail ?? "No additional detail", capabilities.Robocopy.State));
            EnvironmentRows.Add(Row("USMT", StateText(capabilities.Usmt.State), capabilities.Usmt.Detail ?? "No additional detail", capabilities.Usmt.State));
            var media = capabilities.ExternalCandidates.OrderByDescending(volume => volume.AvailableBytes).FirstOrDefault();
            EnvironmentRows.Add(media is null ? Row("External migration media", "Not detected", "Fixed disks without external evidence are not treated as migration media.", CapabilityState.NotAvailable) : Row("External migration media", $"{media.BusType} · {FormatBytes(media.AvailableBytes)} free", media.Model ?? media.RootPath, policy.AllowExternalStorage ? CapabilityState.Available : CapabilityState.ForbiddenByPolicy));
            var network = plan.Reasons.First(reason => reason.Subject == "Approved network route"); EnvironmentRows.Add(Row("Approved network route", StateText(network.State), network.Explanation, network.State));
            Profiles.Clear(); foreach (var profile in capabilities.UserProfiles.Where(profile => profile.IsSelectable)) Profiles.Add(profile);
            Recommendation = plan.Recommendation; RecommendationDetail = string.Join("  ", plan.Reasons.Select(reason => reason.Explanation));
            if (!loaded.IsValid) ErrorMessage = string.Join(" ", loaded.Issues.Select(issue => issue.Message));
            OnPropertyChanged(nameof(HasAnalysis)); OnPropertyChanged(nameof(ProfileDetail));
            ConfigureOperations();
        }
        finally { IsAnalyzing = false; }
    }
    private void ShowError(Exception exception) { ErrorMessage = $"Analysis could not be completed. {exception.GetType().Name}. Review technician logs and retry."; }
    public Task InitializeAsync(CancellationToken cancellationToken = default) => Operations.DiscoverRecoveryAsync(cancellationToken);
    private void ConfigureOperations() { if (SelectedProfile is not null && activePolicy is not null && activePlan is not null && activeRobocopy is not null) Operations.Configure(SelectedProfile, activePolicy, activePlan, activeRobocopy, activeDestinations); }
    private static EnvironmentRow Row(string label, string value, string detail, CapabilityState state) => new(label, value, detail, state == CapabilityState.Available ? "available" : state is CapabilityState.ForbiddenByPolicy or CapabilityState.NotAvailable ? "blocked" : "warning");
    private static string StateText(CapabilityState state) => state switch { CapabilityState.Available => "Available", CapabilityState.NotAvailable => "Not available", CapabilityState.NotConfigured => "Not configured", CapabilityState.ForbiddenByPolicy => "Blocked by policy", CapabilityState.RequiresApproval => "Requires approval", _ => "Unknown" };
    private static string FormatBytes(long bytes) { string[] units = ["B", "KB", "MB", "GB", "TB"]; var value = (double)bytes; var unit = 0; while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; } return $"{value:0.#} {units[unit]}"; }
}
