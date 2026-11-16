using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace YgoMDStartingHandRecorder.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _run;
    private readonly Func<object?, bool>? _can;

    public RelayCommand(Action<object?> run, Func<object?, bool>? can = null)
    {
        _run = run;
        _can = can;
    }

    public RelayCommand(Action run, Func<bool>? can = null)
        : this(_ => run(), can == null ? null : _ => can()) { }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _can?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _run(parameter);

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}