using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using RoboTransfer.App;
using RoboTransfer.App.ViewModels;
using RoboTransfer.App.Views;
using RoboTransfer.Core;

// Offline layout preview only. This harness does not analyze endpoints or copy data.
var output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/ui-preview");
Directory.CreateDirectory(output);
AppBuilder.Configure<App>().UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();
var operations = new OperationalWorkflowViewModel(null!);
var wizard = new MainWindowViewModel(null!, null!, null!, operations);
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
window.Close();
Console.WriteLine($"Native UI previews saved to {output}");
void Capture(string name)
{
    Dispatcher.UIThread.RunJobs();
    using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No rendered frame.");
    frame.Save(Path.Combine(output, name + ".png"));
}
