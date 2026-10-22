using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;
using Marquee.Editor.Mvvm;

namespace Marquee.Editor.Properties;

/// A complete binding is committed together, even when one component box loses focus.
public sealed class UniformRowViewModel : ObservableObject
{
    readonly ShaderPanelViewModel _owner;
    IReadOnlyList<string> _saved;

    internal UniformRowViewModel(ShaderPanelViewModel owner, UniformRow row)
    {
        _owner = owner;
        Name = row.Name;
        Hint = row.Hint;
        IsUnused = !row.Declared;
        _saved = Values(row);
        Components = _saved.Select(value => new UniformComponentViewModel(this, value)).ToList();
        RemoveCommand = new RelayCommand(() => _owner.Remove(Name), () => IsUnused && IsEditable);
    }

    public string Name { get; }
    public string? Hint { get; private set; }
    internal ValueSources Sources => _owner.Values;
    public bool IsUnused { get; }
    public bool IsEditable => _owner.IsEditable;
    public IReadOnlyList<UniformComponentViewModel> Components { get; }
    public RelayCommand RemoveCommand { get; }

    public bool Commit()
    {
        if (!IsEditable || Components.Any(component => component.HasError)) return false;
        var values = Components.Select(component => component.Text.Trim()).ToArray();
        if (values.SequenceEqual(_saved)) return true;
        if (!_owner.Set(Name, values)) return false;
        _saved = values;
        return true;
    }

    internal void Update(UniformRow row)
    {
        Hint = row.Hint;
        Raise(nameof(Hint));
        _saved = Values(row);
        for (var i = 0; i < Components.Count; i++) Components[i].Picker.Update(_saved[i]);
        foreach (var component in Components) component.Refresh();
        RemoveCommand.Refresh();
    }

    static IReadOnlyList<string> Values(UniformRow row) => Enumerable.Range(0, row.Components)
        .Select(i => i < row.Values.Count ? row.Values[i] : "0").ToArray();
}

public sealed class UniformComponentViewModel : ObservableObject
{
    readonly UniformRowViewModel _owner;

    internal UniformComponentViewModel(UniformRowViewModel owner, string text)
    {
        _owner = owner;
        Picker = new ValuePickerViewModel(owner.Sources, text, showRange: true, owner.Hint);
        Picker.PropertyChanged += (_, _) => Raise(null);
        Picker.Committed += (_, _) => Commit();
    }

    public ValuePickerViewModel Picker { get; }
    public string Text
    {
        get => Picker.Text ?? Picker.AdvancedText;
        set => Picker.Read(value);
    }

    public string? Error => Picker.Problem;
    public bool HasError => Error is not null;
    public bool IsEditable => _owner.IsEditable;
    public bool Commit() => _owner.Commit();
    internal void Refresh() { Picker.Hint = _owner.Hint; Raise(null); }
}
