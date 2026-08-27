using System.Collections.ObjectModel;
using System.Windows.Input;
using RoboTransfer.Core;
namespace RoboTransfer.App.ViewModels;
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly ICapabilityDetector detector; private readonly IMigrationPlanner planner;
    private string windowsState = "Unknown", robocopyState = "Unknown", usmtState = "Unknown", networkState = "Not configured", externalState = "Not detected", recommendation = "Run analysis to determine approved options.", detail = "No migration is performed during Phase 1."; private MigrationRole role = MigrationRole.OldPc; private UserProfile? selectedProfile;
    public MainWindowViewModel(ICapabilityDetector detector, IMigrationPlanner planner) { this.detector = detector; this.planner = planner; RefreshCommand = new AsyncCommand(RefreshAsync); }
    public string WindowsState { get => windowsState; private set => Set(ref windowsState, value); } public string RobocopyState { get => robocopyState; private set => Set(ref robocopyState, value); } public string UsmtState { get => usmtState; private set => Set(ref usmtState, value); } public string NetworkState { get => networkState; private set => Set(ref networkState, value); } public string ExternalState { get => externalState; private set => Set(ref externalState, value); }
    public string Recommendation { get => recommendation; private set => Set(ref recommendation, value); } public string Detail { get => detail; private set => Set(ref detail, value); } public MigrationRole Role { get => role; set => Set(ref role, value); } public UserProfile? SelectedProfile { get => selectedProfile; set => Set(ref selectedProfile, value); }
    public IReadOnlyList<MigrationRole> Roles { get; } = Enum.GetValues<MigrationRole>(); public ObservableCollection<UserProfile> Profiles { get; } = []; public ICommand RefreshCommand { get; }
    private async Task RefreshAsync()
    {
        var policy = PolicyProfile.Conservative; var capabilities = await detector.DetectAsync(policy); var plan = planner.CreatePlan(policy, capabilities);
        WindowsState = OperatingSystem.IsWindows() ? "Ready" : "Not available"; RobocopyState = Label(capabilities.Robocopy.State); UsmtState = capabilities.Usmt.State == CapabilityState.Available ? "Available" : "Not detected"; NetworkState = policy.AllowConfiguredNetworkShare ? (capabilities.ApprovedShares.Any(x => x.State == CapabilityState.Available) ? "Available" : "Not configured") : "Forbidden by policy"; ExternalState = capabilities.Volumes.Any(x => x.Kind == StorageKind.Removable && x.IsReady) ? "Detected" : "Not detected";
        Profiles.Clear(); foreach (var profile in capabilities.UserProfiles) Profiles.Add(profile); Recommendation = plan.Recommendation; Detail = string.Join(Environment.NewLine, plan.Reasons.Select(x => $"{x.Subject}: {x.Explanation}"));
    }
    private static string Label(CapabilityState value) => value switch { CapabilityState.NotAvailable => "Not available", CapabilityState.NotConfigured => "Not configured", CapabilityState.ForbiddenByPolicy => "Forbidden by policy", CapabilityState.RequiresApproval => "Requires approval", _ => value.ToString() };
}
