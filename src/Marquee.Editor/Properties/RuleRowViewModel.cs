using System.Globalization;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;
using Marquee.Editor.Resources;

namespace Marquee.Editor.Properties;

public sealed class RuleRowViewModel : ObservableObject
{
    readonly RulesViewModel _list;
    string _condition = "above";
    string _value = "0";
    string? _styleChoice;
    bool _hasStyle;
    string? _error;
    bool _matching;
    bool _editingCondition;
    IReadOnlyList<PropertyRowViewModel> _ringRows = [];
    YamlMap _draft = YamlMap.Empty.With("above", "0");

    internal RuleRowViewModel(RulesViewModel list, int index, bool draft)
    {
        _list = list;
        Index = index;
        IsDraft = draft;
        DeleteCommand = new RelayCommand(() => list.Delete(this), () => list.CanEdit);
        Properties = list.Panel.CreateTarget(PropertyModel.ForRule(Document(), Path), WriteProperties);
        Refresh();
    }

    public int Index { get; private set; }
    public bool IsDraft { get; private set; }
    public YamlPath Path => _list.Owner.Child("when").Item(Index);
    public PropertiesViewModel Properties { get; }
    public RelayCommand DeleteCommand { get; }
    public bool IsRing => _list.IsRing;
    public bool IsWidget => !IsRing;
    public bool IsCustom => IsWidget && _hasStyle && _styleChoice is null;
    public bool NeedsValue => Condition != "stale";
    public IReadOnlyList<string> Conditions { get; } = ["above", "below", "equals", "stale"];
    public IReadOnlyList<RuleStyleOption> StyleOptions =>
        [.. Properties.Model!.StyleNames.Select(name => new RuleStyleOption(name, name)), new(null, Strings.RuleCustom)];
    public RuleStyleOption? SelectedStyle
    {
        get => _styleChoice is { } name ? new(name, name) : _hasStyle ? new(null, Strings.RuleCustom) : null;
        set { if (value is not null && value != SelectedStyle) ChooseStyle(value.Name); }
    }
    public IReadOnlyList<PropertyRowViewModel> RingRows => _ringRows;
    public string? Error { get => _error; private set => Set(ref _error, value); }
    public bool IsMatching { get => _matching; internal set => Set(ref _matching, value); }

    public string Condition
    {
        get => _condition;
        set
        {
            if (!Conditions.Contains(value) || !Set(ref _condition, value)) return;
            _editingCondition = true;
            if (value is "above" or "below" && !Number(Value)) Value = "0";
            Raise(nameof(NeedsValue));
            CommitCondition();
        }
    }

    public string Value
    {
        get => _value;
        set { if (Set(ref _value, value)) { _editingCondition = true; ValidateCondition(); } }
    }

    public string? StyleChoice
    {
        get => _styleChoice;
        set { if (value is not null && value != _styleChoice) ChooseStyle(value); }
    }

    void ChooseStyle(string? name)
    {
        if (!_list.CanEdit) return;
        var model = Properties.Model!;
        if (name is not null && !model.StyleNames.Contains(name)) return;
        var style = name is null ? (YamlValue)YamlMap.Empty : new YamlText(name);
        if (!Properties.Write(() => model.Set(model.Row("style")!, style))) Raise(nameof(SelectedStyle));
    }

    bool ValidateCondition()
    {
        Error = Condition is "above" or "below" && !Number(Value) ? Strings.NotANumber(Value) : null;
        return Error is null;
    }

    static bool Number(string text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n);

    public bool CommitCondition()
    {
        if (!_list.CanEdit || !ValidateCondition()) return false;
        if (!_editingCondition) return true;
        var value = Condition == "stale" ? "true" : Value;
        if (IsDraft)
        {
            foreach (var key in Conditions) _draft = _draft.Without(key);
            _draft = _draft.With(Condition, value);
            _editingCondition = false;
            Refresh();
            return true;
        }
        _editingCondition = false;
        var written = _list.Panel.Write(() => new SetCondition(Path, Condition, value));
        if (!written) Refresh();
        return written;
    }

    LayoutDocument Document()
    {
        var document = _list.Panel.Session!.Document;
        if (!IsDraft) return document;
        var owner = _list.Owner;
        if (IsRing && !document.Exists(owner)) document = document.AddKey(YamlPath.Root, "ring", "{}");
        var list = owner.Child("when");
        if (!document.Exists(list)) document = document.AddKey(owner, "when", "[]");
        Index = document.GetNode(list)!.Count;
        return document.InsertItem(list, Index, YamlWriter.Flow(_draft));
    }

    bool WriteProperties(EditorCommand command)
    {
        if (!_list.CanEdit) return false;
        if (!IsDraft) return _list.Panel.Write(() => command);
        // All property writers are EditDocument commands; apply to the draft's private document first.
        if (command is not EditDocument edit) return false;
        var document = edit.Edit(Document());
        _draft = (YamlMap)document.ValueAt(Path)!;
        var complete = IsRing ? new[] { "color", "effect", "speed", "brightness" }.Any(key => _draft[key] is not null)
            : _draft["style"] is not null;
        if (!complete) { Refresh(); return true; }
        IsDraft = false;
        var written = _list.Panel.Write(() => new InsertDraft(_list.Owner, Condition, Value, _draft));
        if (!written) IsDraft = true;
        Refresh();
        return written;
    }

    internal void Refresh()
    {
        var document = Document();
        var model = PropertyModel.ForRule(document, Path);
        Properties.RefreshTarget(model);
        if (!_editingCondition)
        {
            _condition = Conditions.First(key => document.Exists(Path.Child(key)));
            _value = document.TryGetScalar(Path.Child(_condition), out var text) ? text : "";
            Raise(nameof(Condition));
            Raise(nameof(Value));
            Raise(nameof(NeedsValue));
        }
        _styleChoice = document.TryGetScalar(Path.Child("style"), out var name) ? name : null;
        _hasStyle = document.Exists(Path.Child("style"));
        Raise(nameof(StyleChoice));
        Raise(nameof(StyleOptions));
        Raise(nameof(SelectedStyle));
        Raise(nameof(IsCustom));
        var ringRows = IsRing ? Properties.Rows.Where(row => !Conditions.Contains(row.Name)).ToList() : [];
        if (!_ringRows.SequenceEqual(ringRows)) { _ringRows = ringRows; Raise(nameof(RingRows)); }
        DeleteCommand.Refresh();
    }

    sealed record InsertDraft(YamlPath Owner, string Condition, string Value, YamlMap Rule) : EditorCommand
    {
        public override string Label => "Add rule";
        public override CommandResult Run(CommandContext context)
        {
            var document = new AddRule(Owner, Condition, Value).Run(context).Document;
            var list = Owner.Child("when");
            var index = document.GetNode(list)!.Count - 1;
            return document.RemoveItem(list.Item(index)).InsertItem(list, index, YamlWriter.Flow(Rule));
        }
    }
}

/// Null names the inline option, so a named style literally called "Custom" remains selectable.
public sealed record RuleStyleOption(string? Name, string Label);
