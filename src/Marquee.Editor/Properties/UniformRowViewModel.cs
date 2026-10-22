using Marquee.Editor.Model;
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
        IsUnused = !row.Declared;
        _saved = Values(row);
        Components = _saved.Select(value => new UniformComponentViewModel(this, value)).ToList();
        RemoveCommand = new RelayCommand(() => _owner.Remove(Name), () => IsUnused && IsEditable);
    }

    public string Name { get; }
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
        var clean = Components.Select(component => component.Text).SequenceEqual(_saved);
        _saved = Values(row);
        if (clean)
            for (var i = 0; i < Components.Count; i++) Components[i].Text = _saved[i];
        foreach (var component in Components) component.Refresh();
        RemoveCommand.Refresh();
    }

    static IReadOnlyList<string> Values(UniformRow row) => Enumerable.Range(0, row.Components)
        .Select(i => i < row.Values.Count ? row.Values[i] : "0").ToArray();
}

public sealed class UniformComponentViewModel : ObservableObject
{
    readonly UniformRowViewModel _owner;
    string _text;

    internal UniformComponentViewModel(UniformRowViewModel owner, string text)
    {
        _owner = owner;
        _text = text;
    }

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            Raise(nameof(Error));
            Raise(nameof(HasError));
        }
    }

    public string? Error => ShaderLayerModel.Check(Text.Trim());
    public bool HasError => Error is not null;
    public bool IsEditable => _owner.IsEditable;
    public bool Commit() => _owner.Commit();
    internal void Refresh() => Raise(null);
}
