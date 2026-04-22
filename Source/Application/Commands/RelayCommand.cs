using System;
using System.Windows.Input;
using Avalonia.Threading;

namespace ShadowLink.Application.Commands;

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<Boolean>? _canExecute;

    public RelayCommand(Action execute, Func<Boolean>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public Boolean CanExecute(Object? parameter)
    {
        return _canExecute?.Invoke() ?? true;
    }

    public void Execute(Object? parameter)
    {
        if (CanExecute(parameter))
        {
            _execute();
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
