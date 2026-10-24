namespace Marquee.Editor.Mvvm;

/// In-memory UI state only: each new editor session starts with closed sections.
public sealed class SectionExpansion : ObservableObject
{
    bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
}
