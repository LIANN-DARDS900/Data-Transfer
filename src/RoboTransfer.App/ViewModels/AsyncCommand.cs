using System.Windows.Input;
namespace RoboTransfer.App.ViewModels;
public sealed class AsyncCommand(Func<Task> action) : ICommand
{
    private bool busy; public bool CanExecute(object? parameter) => !busy; public event EventHandler? CanExecuteChanged;
    public async void Execute(object? parameter) { if (busy) return; busy = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty); try { await action(); } finally { busy = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); } }
}
