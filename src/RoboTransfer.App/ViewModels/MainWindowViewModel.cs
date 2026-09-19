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
    private readonly ICapabilityDetector detector;
    private readonly IMigrationPlanner planner;
    private readonly IPolicyProvider policies;
    private PolicyProfile? activePolicy;
    private MigrationPlan? activePlan;
    private ToolCapability? activeRobocopy;
    private IReadOnlyList<DestinationOption> activeDestinations = [];
    private bool isAnalyzing;
    private string? errorMessage;
    private string recommendation = "Analysis required";
    private string recommendationDetail = "Analyze this PC to identify policy-approved migration options.";
    private string policySummary = "Policy not loaded";
    private MigrationRole role = MigrationRole.OldPc;
    private UserProfile? selectedProfile;
    private int currentPage;

    public MainWindowViewModel(
        ICapabilityDetector detector,
        IMigrationPlanner planner,
        IPolicyProvider policies,
        OperationalWorkflowViewModel operations)
    {
        this.detector = detector;
        this.planner = planner;
        this.policies = policies;
        Operations = operations;

        RefreshCommand = new AsyncCommand(RefreshAsync, ShowError);
        SelectSenderCommand = new DelegateCommand(() =>
        {
            Role = MigrationRole.OldPc;
            SetPage(1);
        });
        SelectReceiverCommand = new DelegateCommand(() =>
        {
            Role = MigrationRole.NewPc;
            SetPage(1);
        });
        BackCommand = new DelegateCommand(() => SetPage(Math.Max(0, currentPage - 1)));
        ContinueCommand = new DelegateCommand(() => SetPage(Math.Min(6, currentPage + 1)));
        StartOverCommand = new DelegateCommand(() => SetPage(0));

        Operations.PropertyChanged += (_, _) => NotifyWizardState();
    }

    public OperationalWorkflowViewModel Operations { get; }
    public ObservableCollection<EnvironmentRow> EnvironmentRows { get; } = [];
    public ObservableCollection<UserProfile> Profiles { get; } = [];

    public ICommand RefreshCommand { get; }
    public ICommand SelectSenderCommand { get; }
    public ICommand SelectReceiverCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand StartOverCommand { get; }

    public string ApplicationVersion => ApplicationIdentity.Version;
    public int CurrentPage => currentPage;
    public bool IsWelcomePage => CurrentPage == 0;
    public bool IsSetupPage => CurrentPage == 1;
    public bool IsDataPage => CurrentPage == 2;
    public bool IsReviewPage => CurrentPage == 3;
    public bool IsTransferPage => CurrentPage == 4;
    public bool IsVerificationPage => CurrentPage == 5;
    public bool IsReportPage => CurrentPage == 6;
    public bool ShowBack => CurrentPage > 0;
    public string StepLabel => CurrentPage == 0 ? "START" : $"STEP {CurrentPage} OF 6";
    public string RoleTitle => Role == MigrationRole.OldPc ? "Send from this PC" : "Receive on this PC";
    public string RoleDetail => Role == MigrationRole.OldPc
        ? "Choose the employee profile and an approved destination. RoboTransfer will never delete the source."
        : "Receiver mode is reserved for the destination workflow. Direct PC-to-PC receive is not enabled in this qualification build.";
    public bool ReceiverAvailable => false;

    public bool IsAnalyzing
    {
        get => isAnalyzing;
        private set
        {
            if (Set(ref isAnalyzing, value))
            {
                OnPropertyChanged(nameof(AnalysisButtonText));
                OnPropertyChanged(nameof(HasAnalysis));
                NotifyWizardState();
            }
        }
    }

    public bool HasAnalysis => EnvironmentRows.Count > 0;
    public string AnalysisButtonText => IsAnalyzing ? "Analyzing…" : HasAnalysis ? "Refresh analysis" : "Analyze this PC";

    public string? ErrorMessage
    {
        get => errorMessage;
        private set
        {
            if (Set(ref errorMessage, value))
                OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string Recommendation { get => recommendation; private set => Set(ref recommendation, value); }
    public string RecommendationDetail { get => recommendationDetail; private set => Set(ref recommendationDetail, value); }
    public string PolicySummary { get => policySummary; private set => Set(ref policySummary, value); }

    public MigrationRole Role
    {
        get => role;
        set
        {
            if (Set(ref role, value))
            {
                OnPropertyChanged(nameof(RoleTitle));
                OnPropertyChanged(nameof(RoleDetail));
            }
        }
    }

    public UserProfile? SelectedProfile
    {
        get => selectedProfile;
        set
        {
            if (Set(ref selectedProfile, value))
            {
                OnPropertyChanged(nameof(ProfileDetail));
                ConfigureOperations();
                NotifyWizardState();
            }
        }
    }

    public string ProfileDetail => SelectedProfile is null
        ? "Choose the registered employee profile to migrate."
        : $"{SelectedProfile.KnownFolders.Count(folder => folder.Resolution == KnownFolderResolution.Resolved)} known folders resolved";

    public bool CanContinueFromSetup =>
        HasAnalysis &&
        Role == MigrationRole.OldPc &&
        SelectedProfile is not null &&
        Operations.SelectedDestination is not null;

    public bool CanContinueToReview => Operations.Stage == "Migration Plan";
    public bool CanContinueToTransfer => Operations.CanTransfer;
    public bool CanContinueToVerification => Operations.CanVerify || Operations.Stage == "Verification";
    public bool CanContinueToReport => Operations.CanGenerateReport;

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        IsAnalyzing = true;
        ErrorMessage = null;

        try
        {
            var loaded = await policies.LoadAsync(cancellationToken);
            var policy = loaded.IsValid ? loaded.Policy : PolicyProfile.Conservative;
            PolicySummary = loaded.IsValid
                ? $"Policy schema {policy.SchemaVersion} validated"
                : "Conservative mode — policy missing or invalid";

            var capabilities = await detector.DetectAsync(policy, cancellationToken);
            var plan = planner.CreatePlan(policy, capabilities);

            activePolicy = policy;
            activePlan = plan;
            activeRobocopy = capabilities.Robocopy;
            activeDestinations =
            [
                .. capabilities.ExternalCandidates
                    .Where(_ => policy.AllowExternalStorage)
                    .Select(v => new DestinationOption(
                        $"{v.Label ?? v.RootPath} · {FormatBytes(v.AvailableBytes)} free",
                        new(MigrationRoute.ExternalStorage, v.RootPath, v.AvailableBytes),
                        v)),
                .. capabilities.ApprovedShares
                    .Where(s => s.State == CapabilityState.Available && policy.AllowConfiguredNetworkShare)
                    .Select(s => new DestinationOption(
                        s.Path,
                        new(MigrationRoute.ConfiguredNetworkShare, s.Path, null),
                        null))
            ];

            EnvironmentRows.Clear();
            EnvironmentRows.Add(Row(
                "Windows",
                capabilities.OperatingSystem.Description,
                capabilities.OperatingSystem.IsElevated switch
                {
                    true => "Administrator context",
                    false => "Standard-user context",
                    _ => "Elevation unknown"
                },
                OperatingSystem.IsWindows() ? CapabilityState.Available : CapabilityState.Unknown));

            EnvironmentRows.Add(Row(
                "Robocopy",
                StateText(capabilities.Robocopy.State),
                capabilities.Robocopy.Detail ?? "No additional detail",
                capabilities.Robocopy.State));

            var media = capabilities.ExternalCandidates
                .OrderByDescending(volume => volume.AvailableBytes)
                .FirstOrDefault();

            EnvironmentRows.Add(media is null
                ? Row(
                    "External drive",
                    "Not detected",
                    "Connect an approved NTFS USB/SSD if external-media migration is allowed.",
                    CapabilityState.NotAvailable)
                : Row(
                    "External drive",
                    $"{media.BusType} · {FormatBytes(media.AvailableBytes)} free",
                    media.Model ?? media.RootPath,
                    policy.AllowExternalStorage ? CapabilityState.Available : CapabilityState.ForbiddenByPolicy));

            var network = plan.Reasons.First(reason => reason.Subject == "Approved network route");
            EnvironmentRows.Add(Row(
                "Company network",
                StateText(network.State),
                network.Explanation,
                network.State));

            Profiles.Clear();
            foreach (var profile in capabilities.UserProfiles.Where(profile => profile.IsSelectable))
                Profiles.Add(profile);

            Recommendation = plan.Recommendation;
            RecommendationDetail = string.Join("  ", plan.Reasons.Select(reason => reason.Explanation));

            if (!loaded.IsValid)
                ErrorMessage = string.Join(" ", loaded.Issues.Select(issue => issue.Message));

            OnPropertyChanged(nameof(HasAnalysis));
            OnPropertyChanged(nameof(ProfileDetail));
            ConfigureOperations();
            NotifyWizardState();
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private void ShowError(Exception exception)
    {
        ErrorMessage = $"Analysis could not be completed. {exception.GetType().Name}. Review technician logs and retry.";
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        Operations.DiscoverRecoveryAsync(cancellationToken);

    private void ConfigureOperations()
    {
        if (SelectedProfile is not null &&
            activePolicy is not null &&
            activePlan is not null &&
            activeRobocopy is not null)
        {
            Operations.Configure(
                SelectedProfile,
                activePolicy,
                activePlan,
                activeRobocopy,
                activeDestinations);
        }
    }

    private void SetPage(int page)
    {
        if (currentPage == page)
            return;

        currentPage = page;
        NotifyWizardState();
    }

    private void NotifyWizardState()
    {
        foreach (var property in new[]
        {
            nameof(CurrentPage),
            nameof(IsWelcomePage),
            nameof(IsSetupPage),
            nameof(IsDataPage),
            nameof(IsReviewPage),
            nameof(IsTransferPage),
            nameof(IsVerificationPage),
            nameof(IsReportPage),
            nameof(ShowBack),
            nameof(StepLabel),
            nameof(CanContinueFromSetup),
            nameof(CanContinueToReview),
            nameof(CanContinueToTransfer),
            nameof(CanContinueToVerification),
            nameof(CanContinueToReport)
        })
        {
            OnPropertyChanged(property);
        }
    }

    private static EnvironmentRow Row(
        string label,
        string value,
        string detail,
        CapabilityState state) =>
        new(
            label,
            value,
            detail,
            state == CapabilityState.Available
                ? "available"
                : state is CapabilityState.ForbiddenByPolicy or CapabilityState.NotAvailable
                    ? "blocked"
                    : "warning");

    private static string StateText(CapabilityState state) => state switch
    {
        CapabilityState.Available => "Available",
        CapabilityState.NotAvailable => "Not available",
        CapabilityState.NotConfigured => "Not configured",
        CapabilityState.ForbiddenByPolicy => "Blocked by policy",
        CapabilityState.RequiresApproval => "Requires approval",
        _ => "Unknown"
    };

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }
}
