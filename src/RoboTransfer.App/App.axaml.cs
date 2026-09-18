using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoboTransfer.App.ViewModels;
using RoboTransfer.App.Views;
using RoboTransfer.Core;
using RoboTransfer.Persistence;
using RoboTransfer.Robocopy;
using RoboTransfer.Usmt;
using RoboTransfer.Windows;
using RoboTransfer.Verification;
namespace RoboTransfer.App;
public sealed partial class App : Application
{
    private ServiceProvider? services;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection(); collection.AddLogging(builder => builder.AddSimpleConsole(options => { options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ "; options.UseUtcTimestamp = true; options.SingleLine = true; }).SetMinimumLevel(LogLevel.Information));
        collection.AddSingleton<IMigrationPlanner, MigrationPlanner>(); collection.AddSingleton<IStorageDetector, WindowsStorageDetector>(); collection.AddSingleton<IUserProfileDetector, WindowsUserProfileDetector>(); collection.AddSingleton<IExecutableTrustValidator, WindowsExecutableTrustValidator>(); collection.AddSingleton<IToolDetector, RobocopyDetector>(); collection.AddSingleton<UsmtToolDetector>(); collection.AddSingleton<ApprovedNetworkShareDetector>(); collection.AddSingleton<ICapabilityDetector, WindowsCapabilityDetector>(); collection.AddSingleton<ICloudPlaceholderDetector, WindowsCloudPlaceholderDetector>();
        var layout = ApplicationDataLayout.CreateDefault(); var data = layout.Root; collection.AddSingleton(layout); collection.AddSingleton<IPolicyProvider>(_ => new JsonPolicyProvider(Path.Combine(layout.Policies, "policy.json"))); collection.AddSingleton<IMigrationJournal>(provider => new JsonMigrationJournal(layout.Sessions, provider.GetRequiredService<ILogger<JsonMigrationJournal>>()));
        collection.AddSingleton<IManifestReader, JsonLinesManifestReader>(); collection.AddSingleton<IExecutionPlanStore>(_ => new JsonExecutionPlanStore(Path.Combine(data, "plans"))); collection.AddSingleton<IVerificationStore>(_ => new JsonVerificationStore(Path.Combine(data, "verification"))); collection.AddSingleton<IOperationalRecordStore>(_ => new JsonOperationalRecordStore(Path.Combine(data, "operations"))); collection.AddSingleton<IReportGenerator, MigrationReportGenerator>(); collection.AddSingleton<IManifestScanner, ManifestScanner>(); collection.AddSingleton<IDestinationWriteProbe, DestinationWriteProbe>(); collection.AddSingleton<IDestinationValidator, DestinationValidator>(); collection.AddSingleton<IMigrationRecovery, MigrationRecovery>(); collection.AddSingleton<ITransferReconciler, ManifestTransferReconciler>(); collection.AddSingleton<IVerificationEngine, FileVerificationEngine>(); collection.AddSingleton<RoboTransfer.App.Services.KeepBothTransferEngineFactory>(); collection.AddSingleton(provider => new RoboTransfer.App.Services.MigrationWorkflowCoordinator(provider.GetRequiredService<IManifestScanner>(), provider.GetRequiredService<IManifestReader>(), provider.GetRequiredService<IMigrationJournal>(), provider.GetRequiredService<IMigrationRecovery>(), provider.GetRequiredService<IExecutionPlanStore>(), provider.GetRequiredService<IDestinationValidator>(), provider.GetRequiredService<ITransferReconciler>(), provider.GetRequiredService<RoboTransfer.App.Services.KeepBothTransferEngineFactory>(), provider.GetRequiredService<IVerificationEngine>(), provider.GetRequiredService<IVerificationStore>(), provider.GetRequiredService<IOperationalRecordStore>(), provider.GetRequiredService<IReportGenerator>(), data)); collection.AddSingleton<OperationalWorkflowViewModel>(); collection.AddSingleton<MainWindowViewModel>();
        services = collection.BuildServiceProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) { var viewModel = services.GetRequiredService<MainWindowViewModel>(); desktop.MainWindow = new MainWindow { DataContext = viewModel }; desktop.Exit += (_, _) => services.Dispose(); _ = viewModel.InitializeAsync(); }
        base.OnFrameworkInitializationCompleted();
    }
}
