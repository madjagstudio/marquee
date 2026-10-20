using System.Globalization;
using System.Resources;

namespace Marquee.Editor.Resources;

/// The theme editor's text (its window, toolbar, banners and dialogs), from Strings.resx in English and from
/// Strings.<culture>.resx where a translation exists. Each property (plain text) or method (text with placeholders)
/// reads the resource of the same name; the tests check that the two lists match. Public, so XAML can bind to the
/// properties ({x:Static}).
public static class Strings
{
    /// Settable so tests can put a pseudo-translation in front of the real resources.
    internal static ResourceManager ResourceManager { get; set; } = new("Marquee.Editor.Resources.Strings", typeof(Strings).Assembly);

    public static string AppName => Get(nameof(AppName));

    public static string WindowTitle(string theme) => Format(nameof(WindowTitle), theme);

    public static string Usage => Get(nameof(Usage));

    public static string RefuseElevated => Get(nameof(RefuseElevated));

    public static string ThemeNotFound(string error) => Format(nameof(ThemeNotFound), error);

    public static string CouldNotOpen(string theme, string reason) => Format(nameof(CouldNotOpen), theme, reason);

    public static string CouldNotCopy(string theme, string reason) => Format(nameof(CouldNotCopy), theme, reason);

    public static string CouldNotRestore(string reason) => Format(nameof(CouldNotRestore), reason);

    public static string MakeCopyQuestion(string theme, string reason) => Format(nameof(MakeCopyQuestion), theme, reason);

    public static string RestoreQuestion(string theme, string saved, string layoutSaved) =>
        Format(nameof(RestoreQuestion), theme, saved, layoutSaved);

    public static string AlreadyOpen(string theme) => Format(nameof(AlreadyOpen), theme);

    public static string CloseUnsavedQuestion(string reason) => Format(nameof(CloseUnsavedQuestion), reason);

    public static string ThemePickerTip => Get(nameof(ThemePickerTip));

    public static string NewFromExample => Get(nameof(NewFromExample));

    public static string NoExamples => Get(nameof(NoExamples));

    public static string AddWidget => Get(nameof(AddWidget));

    public static string Undo => Get(nameof(Undo));

    public static string Redo => Get(nameof(Redo));

    public static string UndoStep(string step) => Format(nameof(UndoStep), step);

    public static string RedoStep(string step) => Format(nameof(RedoStep), step);

    public static string Align => Get(nameof(Align));

    public static string AlignLeft => Get(nameof(AlignLeft));

    public static string AlignCenter => Get(nameof(AlignCenter));

    public static string AlignRight => Get(nameof(AlignRight));

    public static string AlignTop => Get(nameof(AlignTop));

    public static string AlignMiddle => Get(nameof(AlignMiddle));

    public static string AlignBottom => Get(nameof(AlignBottom));

    public static string DistributeHorizontally => Get(nameof(DistributeHorizontally));

    public static string DistributeVertically => Get(nameof(DistributeVertically));

    public static string ZoomFit => Get(nameof(ZoomFit));

    public static string Zoom100 => Get(nameof(Zoom100));

    public static string Zoom200 => Get(nameof(Zoom200));

    public static string ShowOnScreen => Get(nameof(ShowOnScreen));

    public static string ShowOnScreenTip => Get(nameof(ShowOnScreenTip));

    public static string ShownOnScreen(string theme) => Format(nameof(ShownOnScreen), theme);

    public static string ShowOnScreenFailed(string reason) => Format(nameof(ShowOnScreenFailed), reason);

    public static string Saved => Get(nameof(Saved));

    public static string Saving => Get(nameof(Saving));

    public static string NotSaved => Get(nameof(NotSaved));

    public static string SaveReadOnly => Get(nameof(SaveReadOnly));

    public static string SaveFailedDetail(string reason, string retryAt) => Format(nameof(SaveFailedDetail), reason, retryAt);

    public static string NoProblems => Get(nameof(NoProblems));

    public static string OneProblem => Get(nameof(OneProblem));

    public static string Problems(int count) => Format(nameof(Problems), count);

    public static string PausedBanner(string error) => Format(nameof(PausedBanner), error);

    public static string ReadOnlyBanner(string reason) => Format(nameof(ReadOnlyBanner), reason);

    public static string RevertToOpened => Get(nameof(RevertToOpened));

    public static string MakeCopyToEdit => Get(nameof(MakeCopyToEdit));

    public static string KeepEditing => Get(nameof(KeepEditing));

    public static string SaveToFolder => Get(nameof(SaveToFolder));

    public static string SaveToFolderTitle => Get(nameof(SaveToFolderTitle));

    public static string SaveToFolderFailed(string reason) => Format(nameof(SaveToFolderFailed), reason);

    public static string ReplaceLayoutQuestion(string folder) => Format(nameof(ReplaceLayoutQuestion), folder);

    public static string RecoveryOff(string problem) => Format(nameof(RecoveryOff), problem);

    public static string ReadOnlyRefused => Get(nameof(ReadOnlyRefused));

    public static string PausedRefused => Get(nameof(PausedRefused));

    public static string TrayNotRunning => Get(nameof(TrayNotRunning));

    public static string TrayNotRunningTip => Get(nameof(TrayNotRunningTip));

    public static string LayersPanel => Get(nameof(LayersPanel));

    public static string PropertiesPanel => Get(nameof(PropertiesPanel));

    public static string TestValuesPanel => Get(nameof(TestValuesPanel));

    public static string ActionFailed(string action, string reason) => Format(nameof(ActionFailed), action, reason);

    public static string ActionDrag => Get(nameof(ActionDrag));

    public static string ActionSelect => Get(nameof(ActionSelect));

    public static string ActionCopy => Get(nameof(ActionCopy));

    public static string ActionCopyStyle => Get(nameof(ActionCopyStyle));

    public static string ClipboardBusy => Get(nameof(ClipboardBusy));

    public static string CouldNotReadFramePixels => Get(nameof(CouldNotReadFramePixels));

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages get the platform's line breaks back.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee shows.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
