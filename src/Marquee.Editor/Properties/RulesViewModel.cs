using System.Collections.ObjectModel;
using Marquee.Core.Themes.Editing;
using Marquee.Editor.Model;
using Marquee.Editor.Mvvm;

namespace Marquee.Editor.Properties;

/// A single owner's ordered rules. Drafts live here until their first style or ring change is chosen.
public sealed class RulesViewModel : ObservableObject
{
    internal readonly PropertiesViewModel Panel;
    public RulesViewModel(PropertiesViewModel panel, YamlPath owner)
    {
        Panel = panel;
        Owner = owner;
        AddCommand = new RelayCommand(() => Rows.Add(new RuleRowViewModel(this, Rows.Count, draft: true)), () => CanAdd);
        Refresh();
    }

    public YamlPath Owner { get; }
    public bool IsRing => Owner == YamlPath.Root.Child("ring");
    public bool CanEdit => Panel.CanEdit;
    bool CanAdd
    {
        get
        {
            if (!CanEdit || Panel.Session is not { } session) return false;
            if (!IsRing && !session.Document.Exists(Owner)) return false;
            var path = Owner.Child("when");
            while (!path.IsRoot && !session.Document.Exists(path)) path = path.Parent!;
            return session.Document.IsEditable(path);
        }
    }
    public ObservableCollection<RuleRowViewModel> Rows { get; } = [];
    public RelayCommand AddCommand { get; }

    internal void Refresh()
    {
        AddCommand.Refresh();
        Raise(nameof(CanEdit));
        if (Panel.Session!.IsPaused) return;
        var count = Panel.Session!.Document.GetNode(Owner.Child("when"))?.Count ?? 0;
        var saved = Rows.Where(row => !row.IsDraft).ToList();
        if (saved.Count != count)
        {
            var drafts = Rows.Where(row => row.IsDraft).ToList();
            Rows.Clear();
            for (var i = 0; i < count; i++) Rows.Add(new RuleRowViewModel(this, i, draft: false));
            foreach (var draft in drafts) Rows.Add(draft);
        }
        else
        {
            // Completing a later draft must still show persisted rules in file order, ahead of remaining drafts.
            foreach (var row in saved.OrderBy(row => row.Index))
                if (Rows.IndexOf(row) != row.Index) Rows.Move(Rows.IndexOf(row), row.Index);
        }
        foreach (var row in Rows) row.Refresh();
        RefreshMatching();
    }

    public void RefreshMatching()
    {
        if (Panel.Session is not { IsPaused: false } session || Panel.Workspace is not { } workspace) return;
        var matching = RuleSummaries.Matching(session.Document, Owner, workspace.Values.Snapshot());
        foreach (var row in Rows) row.IsMatching = !row.IsDraft && row.Index < matching.Count && matching[row.Index];
    }

    internal void Delete(RuleRowViewModel row)
    {
        if (!CanEdit) return;
        if (row.IsDraft) Rows.Remove(row);
        else Panel.Write(() => new RemoveRule(row.Path));
    }

    public void Move(RuleRowViewModel row, int index)
    {
        if (!CanEdit || row.IsDraft || !Rows.Contains(row) || index == row.Index || index < 0 || index >= Rows.Count || Rows[index].IsDraft) return;
        Panel.Write(() => new MoveRule(row.Path, index));
    }
}
