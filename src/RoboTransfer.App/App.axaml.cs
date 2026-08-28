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
namespace RoboTransfer.App;
public sealed partial class App : Application
{
    private ServiceProvider? services;
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        var collection = new ServiceCollection(); collection.AddLogging(builder => builder.AddSimpleConsole(options => { options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ "; options.UseUtcTimestamp = true; options.SingleLine = true; }).SetMinimumLevel(LogLevel.Information));
        collection.AddSingleton<IMigrationPlanner, MigrationPlanner>(); collection.AddSingleton<IStorageDetector, WindowsStorageDetector>(); collection.AddSingleton<IUserProfileDetector, WindowsUserProfileDetector>(); collection.AddSingleton<IToolDetector, RobocopyDetector>(); collection.AddSingleton<UsmtToolDetector>(); collection.AddSingleton<ApprovedNetworkShareDetector>(); collection.AddSingleton<ICapabilityDetector, WindowsCapabilityDetector>(); collection.AddSingleton<ICloudPlaceholderDetector, WindowsCloudPlaceholderDetector>();
        var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoboTransfer"); collection.AddSingleton<IPolicyProvider>(_ => new JsonPolicyProvider(Path.Combine(data, "policy.json"))); collection.AddSingleton<IMigrationJournal>(provider => new JsonMigrationJournal(Path.Combine(data, "sessions"), provider.GetRequiredService<ILogger<JsonMigrationJournal>>())); collection.AddSingleton<MainWindowViewModel>();
        services = collection.BuildServiceProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) { desktop.MainWindow = new MainWindow { DataContext = services.GetRequiredService<MainWindowViewModel>() }; desktop.Exit += (_, _) => services.Dispose(); }
        base.OnFrameworkInitializationCompleted();
    }
}
