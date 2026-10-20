using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Marquee.Editor.Mvvm;

/// A view-model's change notification, so WPF bindings follow its properties.
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// Sets `field` to `value` and raises PropertyChanged for the property, when the value changes. True when it did.
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    /// Raises PropertyChanged for `name`; null or empty means every property.
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
