using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RoboTransfer.App;
using RoboTransfer.App.ViewModels;
using RoboTransfer.App.Views;
using RoboTransfer.Core;
using RoboTransfer.Persistence;

// Offline layout preview only. This harness does not analyze endpoints or copy data.
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-preview");
Directory.CreateDirectory(output);
AppBuilder.Configure<App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
var sampleRoot = Path.Combine(Path.GetTempPath(), "RoboTransferUiPreview", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(sampleRoot);
var receiver = new ReceiverWorkflowViewModel(new ReceiverInboxService(new PreviewEnvironment(sampleRoot)));
var operations = new OperationalWorkflowViewModel(null!);
var wizard = new MainWindowViewModel(null!, null!, null!, operations, receiver);
var window = new MainWindow { DataContext = wizard };
window.Show();
Capture("welcome");
var folders = Enum.GetValues<KnownFolderKind>().Select(kind => new KnownFolder(kind, $@"C:\Users\Employee\{kind}", true, KnownFolderResolution.Resolved, "Preview only")).ToArray();
var profile = new UserProfile("preview", "Employee", @"C:\Users\Employee", false, ProfileClassification.InteractiveUser, true, folders);
var destination = new DestinationOption("Migration drive · 250 GB free", new(MigrationRoute.ExternalStorage, @"E:\Migration", 250_000_000_000), null);
wizard.SelectSenderCommand.Execute(null);
wizard.EnvironmentRows.Add(new("Windows", "Windows 11", "Preview only", "available"));
wizard.SelectedProfile = profile;
operations.Configure(profile, PolicyProfile.Conservative with { AllowExternalStorage = true, AllowRobocopy = true },
    new(MigrationRoute.ExternalStorage, MigrationStrategy.RobocopyKnownFolders, MigrationStatus.Ready, VerificationLevel.Strong, ConflictPolicy.KeepBoth, [], destination.Destination),
    new("Robocopy", CapabilityState.Available), [destination]);
wizard.ContinueCommand.Execute(null);
Capture("select-files");
window.Width = 900; window.Height = 650;
Capture("select-files-900x650");
window.Width = 1080; window.Height = 820;
wizard.BackCommand.Execute(null); wizard.BackCommand.Execute(null);
wizard.SelectReceiverCommand.Execute(null);
receiver.Configure(PolicyProfile.Conservative with { AllowConfiguredNetworkShare = true, ApprovedNetworkSharePaths = [@"\\NEWPC\Migration"] }, [profile], [@"\\NEWPC\Migration"]);
Capture("receiver-setup");
var preparation = ((AsyncCommand)receiver.PrepareCommand).ExecuteAsync();
var deadline = DateTime.UtcNow.AddSeconds(10);
while (!preparation.IsCompleted && DateTime.UtcNow < deadline) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(5); }
if (!preparation.IsCompleted) throw new TimeoutException("Offline preview did not finish preparing its isolated inbox.");
preparation.GetAwaiter().GetResult();
if (!receiver.HasInbox) throw new InvalidOperationException(receiver.Status);
Capture("receiver-ready");
window.Width = 900; window.Height = 650;
Capture("receiver-ready-900x650");
window.Close();
Directory.Delete(sampleRoot, true);
Console.WriteLine($"Native UI previews saved to {output}");
void Capture(string name)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
    frame.Save(Path.Combine(output, name + ".png"));
}

sealed class PreviewEnvironment(string root) : IReceiverEnvironment
{
    public string MachineName => "NEWPC";
    public bool IsLocalHost(string host) => host == "NEWPC";
    public long? GetAvailableBytes(string _) => 250_000_000_000;
    public string GetAccessPath(string path)
    {
        const string share = @"\\NEWPC\Migration";
        var normalized = ReceiverInboxPaths.Normalize(path);
        if (!normalized.StartsWith(share + "\\", StringComparison.OrdinalIgnoreCase) && normalized != share) throw new InvalidOperationException("Preview path is outside its isolated share.");
        return Path.Combine(root, normalized[share.Length..].TrimStart('\\').Replace('\\', Path.DirectorySeparatorChar));
    }
}
