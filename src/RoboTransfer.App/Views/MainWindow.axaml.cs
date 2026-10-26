using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Threading;
using RoboTransfer.App.ViewModels;

namespace RoboTransfer.App.Views;

public sealed partial class MainWindow : Window
{
    private MainWindowViewModel? observed;
    private int lastPage = -1;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (observed is not null) observed.PropertyChanged -= OnWizardChanged;
            observed = DataContext as MainWindowViewModel;
            lastPage = observed?.CurrentPage ?? -1;
            if (observed is not null) observed.PropertyChanged += OnWizardChanged;
        };
        Closed += (_, _) => { if (observed is not null) observed.PropertyChanged -= OnWizardChanged; };
    }

    private void OnWizardChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MainWindowViewModel.CurrentPage) || observed is null || lastPage == observed.CurrentPage) return;
        lastPage = observed.CurrentPage;
        // Begin each step at its heading, even if the previous page was scrolled.
        Dispatcher.UIThread.Post(() => PageScroll.Offset = default);
    }
}
