using System.Windows.Input;
namespace RoboTransfer.App.ViewModels;
public sealed class AsyncCommand(Func<CancellationToken, Task> action, Action<Exception>? onError = null) : ICommand
{
    private bool executing;
    public bool CanExecute(object? parameter) => !executing;
    public event EventHandler? CanExecuteChanged;
    public async void Execute(object? parameter) => await ExecuteAsync();
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (executing) return;
        executing = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(cancellationToken).ConfigureAwait(true); }
        catch (Exception ex) { onError?.Invoke(ex); }
        finally { executing = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
