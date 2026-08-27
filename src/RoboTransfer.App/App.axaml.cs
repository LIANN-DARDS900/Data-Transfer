using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RoboTransfer.App.ViewModels;
using RoboTransfer.App.Views;
using RoboTransfer.Core;
using RoboTransfer.Robocopy;
using RoboTransfer.Usmt;
using RoboTransfer.Windows;
namespace RoboTransfer.App;
public sealed partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);
    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<IMigrationPlanner, MigrationPlanner>(); services.AddSingleton<IStorageDetector, WindowsStorageDetector>(); services.AddSingleton<IUserProfileDetector, WindowsUserProfileDetector>(); services.AddSingleton<IToolDetector, RobocopyDetector>(); services.AddSingleton<UsmtToolDetector>(); services.AddSingleton<ApprovedNetworkShareDetector>(); services.AddSingleton<ICapabilityDetector, WindowsCapabilityDetector>(); services.AddSingleton<MainWindowViewModel>();
        var provider = services.BuildServiceProvider();
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow { DataContext = provider.GetRequiredService<MainWindowViewModel>() };
        base.OnFrameworkInitializationCompleted();
    }
}
