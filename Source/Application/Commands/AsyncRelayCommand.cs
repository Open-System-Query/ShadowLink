using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;

namespace ShadowLink.Application.Commands;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _executeAsync;
    private readonly Func<Boolean>? _canExecute;
    private Boolean _isRunning;

    public AsyncRelayCommand(Func<Task> executeAsync, Func<Boolean>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public Boolean CanExecute(Object? parameter)
    {
        return !_isRunning && (_canExecute?.Invoke() ?? true);
    }

    public async void Execute(Object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        _isRunning = true;
        RaiseCanExecuteChanged();

        try
        {
            await Task.Yield();
            await _executeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Async command execution failed: {0}", ex);
        }
        finally
        {
            _isRunning = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
    }
}
