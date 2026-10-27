using System.Collections.ObjectModel;
using System.Windows.Input;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
namespace RoboTransfer.App.ViewModels;

public sealed class ReceiverWorkflowViewModel : ViewModelBase
{
    private readonly ReceiverInboxService? service;
    private PolicyProfile policy = PolicyProfile.Conservative;
    private UserProfile? selectedProfile;
    private string? selectedShare;
    private ReceiverOffer? prepared, checkedOffer;
    private string inboxInput = "", comparisonCode = "", status = "Analyze this PC to see approved receiver options.";
    private bool busy, confirmed, fresh;
    private ReceiverStatus? senderStatus;

    public ReceiverWorkflowViewModel(ReceiverInboxService? service)
    {
        this.service = service;
        PrepareCommand = new AsyncCommand(PrepareAsync, Fail);
        ReopenCommand = new AsyncCommand(ReopenAsync, Fail);
        CheckCommand = new AsyncCommand(CheckAsync, Fail);
        UseReceiverCommand = new AsyncCommand(UseReceiverAsync, Fail);
        RefreshCommand = new AsyncCommand(RefreshStatusAsync, Fail);
        CloseCommand = new AsyncCommand(CloseAsync, Fail);
        DoneCommand = new DelegateCommand(() =>
        {
            if (!CanFinishWatching) return;
            prepared = null; senderStatus = null;
            Status = "Inbox files and reports are preserved. You can prepare or reopen another inbox.";
            NotifyState();
        });
    }
    public event Action<ReceiverOffer>? SenderConnected;
    public bool IsAvailable => service is not null;
    public ObservableCollection<UserProfile> Profiles { get; } = [];
    public ObservableCollection<string> Shares { get; } = [];
    public ICommand PrepareCommand { get; }
    public ICommand ReopenCommand { get; }
    public ICommand CheckCommand { get; }
    public ICommand UseReceiverCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CloseCommand { get; }
    public ICommand DoneCommand { get; }
    public bool CanFinishWatching => !IsBusy && senderStatus?.Phase is ReceiverPhase.Verified or ReceiverPhase.Warnings or ReceiverPhase.VerificationFailed or ReceiverPhase.Cancelled or ReceiverPhase.Failed;
    public UserProfile? SelectedProfile { get => selectedProfile; set { if (!HasInbox && !IsBusy && Set(ref selectedProfile, value)) NotifyState(); } }
    public string? SelectedShare { get => selectedShare; set { if (!HasInbox && !IsBusy && Set(ref selectedShare, value)) NotifyState(); } }
    public string InboxInput { get => inboxInput; set { if (!IsBusy && Set(ref inboxInput, value)) { checkedOffer = null; RecipientConfirmed = false; NotifyState(); } } }
    public string ComparisonCode { get => comparisonCode; set { if (!IsBusy && Set(ref comparisonCode, value)) NotifyState(); } }
    public bool RecipientConfirmed { get => confirmed; set { if (Set(ref confirmed, value)) NotifyState(); } }
    public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) NotifyState(); } }
    public bool HasInbox => prepared is not null;
    public bool HasCheckedReceiver => checkedOffer is not null;
    public bool CanEditReceiver => !IsBusy && !HasInbox;
    public bool CanPrepare => CanEditReceiver && service is not null && SelectedProfile is not null && SelectedShare is not null;
    public bool CanUseReceiver => service is not null && !IsBusy && checkedOffer is not null && RecipientConfirmed && string.Equals(ComparisonCode.Trim(), checkedOffer.Code, StringComparison.OrdinalIgnoreCase);
    public bool CanClose => HasInbox && !IsBusy && senderStatus is null;
    public string Status { get => status; private set => Set(ref status, value); }
    public string InboxPath => prepared?.InboxPath ?? "";
    public string InboxCode => prepared?.Code ?? "";
    public string DestinationText => prepared?.FilesPath ?? "";
    public string RecipientText => checkedOffer is null ? "No receiver checked" : $"{checkedOffer.ReceiverMachine} · {checkedOffer.ReceiverName}";
    public string ReceiverName => prepared is null ? "" : $"{prepared.ReceiverMachine} · {prepared.ReceiverName}";
    public string SenderStatusText => senderStatus is null ? "Waiting for the old PC" : "Old PC reports: " + (senderStatus.Phase switch
    {
        ReceiverPhase.Transferring => "copying files", ReceiverPhase.CopyComplete => "files copied; verification is next",
        ReceiverPhase.Verifying => "checking file integrity", ReceiverPhase.Verified => "verification passed",
        ReceiverPhase.Warnings => "verified with warnings", ReceiverPhase.VerificationFailed => "verification needs attention",
        ReceiverPhase.Interrupted => "transfer interrupted", ReceiverPhase.Cancelled => "verification cancelled",
        ReceiverPhase.Failed => "transfer failed", _ => "waiting"
    });
    public string SenderProgress => senderStatus is null ? "No transfer started" : $"{senderStatus.Files:N0} files · {senderStatus.Bytes / 1048576d:N1} MB";
    public string LastSeen => senderStatus is null ? "No sender update yet" : $"Last update: {senderStatus.UpdatedAt.ToLocalTime():g}";
    public bool IsStatusFresh { get => fresh; private set => Set(ref fresh, value); }
    public string ReceiverReportPath => prepared is null ? "" : prepared.InboxPath + "\\report.html";

    public void Configure(PolicyProfile currentPolicy, IReadOnlyList<UserProfile> profiles, IReadOnlyList<string> availableShares)
    {
        if (IsBusy || HasInbox) return;
        policy = currentPolicy;
        checkedOffer = null; confirmed = false;
        Profiles.Clear(); foreach (var profile in profiles.Where(p => p.IsSelectable)) Profiles.Add(profile);
        Shares.Clear(); foreach (var share in availableShares.Where(share => service?.IsHostedHere(share) == true)) Shares.Add(share);
        selectedProfile = Profiles.Count == 1 ? Profiles[0] : null;
        selectedShare = Shares.Count == 1 ? Shares[0] : null;
        OnPropertyChanged(nameof(SelectedProfile)); OnPropertyChanged(nameof(SelectedShare));
        Status = Shares.Count == 0 ? "To receive here, this PC needs an existing approved Windows share. To send, enter the inbox prepared on the new PC." : "Choose the employee and an approved share hosted by this PC.";
        NotifyState();
    }
    public void ClearSenderCheck()
    {
        checkedOffer = null; confirmed = false; comparisonCode = "";
        OnPropertyChanged(nameof(RecipientConfirmed)); OnPropertyChanged(nameof(ComparisonCode)); NotifyState();
    }
    public void Reset()
    {
        if (IsBusy || HasInbox) return;
        ClearSenderCheck(); Profiles.Clear(); Shares.Clear(); selectedProfile = null; selectedShare = null;
        policy = PolicyProfile.Conservative; inboxInput = ""; OnPropertyChanged(nameof(InboxInput)); NotifyState();
    }
    private async Task PrepareAsync(CancellationToken token)
    {
        if (!CanPrepare || service is null || SelectedProfile is null || SelectedShare is null) return;
        IsBusy = true;
        try
        {
            prepared = await Task.Run(() => service.PrepareAsync(policy, SelectedShare, SelectedProfile, token), token);
            Status = "Inbox ready. On the old PC, enter this path, compare the inbox code and confirm the recipient. Files stay inside this migration inbox.";
            NotifyState();
        }
        finally { IsBusy = false; }
    }
    private async Task ReopenAsync(CancellationToken token)
    {
        if (IsBusy || HasInbox || service is null) return;
        IsBusy = true;
        try
        {
            var offer = await Task.Run(() => service.ReadOfferAsync(policy, InboxInput, token, allowExpired: true), token);
            if (!string.Equals(offer.ReceiverMachine, service.MachineName, StringComparison.OrdinalIgnoreCase) || !Profiles.Any(p => p.Id == offer.ReceiverProfileId))
                throw new InvalidOperationException("Reopen this inbox on the original receiving PC and registered employee profile.");
            prepared = offer;
            Status = "Receiver inbox reopened. Check the latest sender status.";
            NotifyState();
        }
        finally { IsBusy = false; }
    }
    private async Task CheckAsync(CancellationToken token)
    {
        if (IsBusy || service is null) return;
        checkedOffer = null; confirmed = false; NotifyState();
        IsBusy = true;
        try
        {
            checkedOffer = await Task.Run(() => service.ReadOfferAsync(policy, InboxInput, token), token);
            if (string.Equals(checkedOffer.ReceiverMachine, service.MachineName, StringComparison.OrdinalIgnoreCase))
            { checkedOffer = null; throw new InvalidOperationException("Choose an inbox on the new PC, not this source PC."); }
            Status = "Check the employee and PC name. Enter the code shown on the new PC, then confirm this recipient. The code identifies the inbox; Windows controls access.";
            NotifyState();
        }
        finally { IsBusy = false; }
    }
    private async Task UseReceiverAsync(CancellationToken token)
    {
        if (!CanUseReceiver || checkedOffer is null || service is null) return;
        IsBusy = true;
        try
        {
            var latest = await Task.Run(() => service.ReadOfferAsync(policy, InboxInput, token), token);
            if (ReceiverInboxService.Bind(latest) != ReceiverInboxService.Bind(checkedOffer)) throw new InvalidDataException("Receiver details changed. Check the inbox again.");
            SenderConnected?.Invoke(latest);
            Status = "Receiver confirmed. Select folders, scan, then review the complete transfer plan.";
        }
        finally { IsBusy = false; }
    }
    public async Task RefreshStatusAsync(CancellationToken token = default)
    {
        if (IsBusy || prepared is null || service is null) return;
        IsBusy = true;
        try
        {
            senderStatus = await Task.Run(() => service.ReadStatusAsync(policy, prepared, token), token);
            IsStatusFresh = senderStatus is not null && DateTimeOffset.UtcNow - senderStatus.UpdatedAt < TimeSpan.FromSeconds(15);
            Status = senderStatus is null ? "Inbox ready. Waiting for the old PC to send files." : "Status comes from the sender. Use its verification report as evidence; a copied file count alone does not prove integrity.";
            NotifyState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            IsStatusFresh = false;
            Status = $"Cannot refresh the receiver inbox ({ex.GetType().Name}). The last status may be stale; check the cable and share access.";
        }
        finally { IsBusy = false; }
    }
    private async Task CloseAsync(CancellationToken token)
    {
        if (!CanClose || service is null || prepared is null) return;
        IsBusy = true;
        try
        {
            await Task.Run(() => service.CloseAsync(policy, prepared, token), token);
            prepared = null; senderStatus = null; Status = "Inbox closed to new senders. Existing files were preserved."; NotifyState();
        }
        finally { IsBusy = false; }
    }
    private void Fail(Exception ex)
    {
        IsBusy = false;
        Status = ex is IOException or InvalidOperationException or UnauthorizedAccessException ? ex.Message : $"Receiver setup stopped ({ex.GetType().Name}). Review the connection and retry.";
        NotifyState();
    }
    private void NotifyState()
    {
        foreach (var property in new[] { nameof(HasInbox), nameof(HasCheckedReceiver), nameof(CanEditReceiver), nameof(CanPrepare), nameof(CanUseReceiver), nameof(CanClose), nameof(CanFinishWatching), nameof(InboxPath), nameof(InboxCode), nameof(DestinationText), nameof(RecipientText), nameof(ReceiverName), nameof(SenderStatusText), nameof(SenderProgress), nameof(LastSeen), nameof(ReceiverReportPath) }) OnPropertyChanged(property);
    }
}
