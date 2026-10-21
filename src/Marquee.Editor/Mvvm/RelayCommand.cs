using System.Windows.Input;

namespace Marquee.Editor.Mvvm;

/// A command that runs a delegate, for buttons and menu items. Whether it can run is asked again when WPF requeries
/// (after input), or at once with Refresh.
public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    EventHandler? _changed;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute())
    {
    }

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            CommandManager.RequerySuggested += value;
            _changed += value;
        }
        remove
        {
            CommandManager.RequerySuggested -= value;
            _changed -= value;
        }
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) execute(parameter);
    }

    /// Tells the controls bound to it to ask CanExecute again, for a change that came without input (an autosave, a
    /// file changed outside the editor).
    public void Refresh() => _changed?.Invoke(this, EventArgs.Empty);
}
