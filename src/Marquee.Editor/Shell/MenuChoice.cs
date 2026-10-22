using System.Windows.Input;

namespace Marquee.Editor.Shell;

/// One item of a toolbar menu: what it says, and the command it runs with its parameter.
public sealed record MenuChoice(string Label, ICommand Command, string ToolTip, object? Parameter = null);
