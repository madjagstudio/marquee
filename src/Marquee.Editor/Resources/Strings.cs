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

    public static string AddImage => Get(nameof(AddImage));

    public static string AddVideo => Get(nameof(AddVideo));

    public static string CleanUpMenu => Get(nameof(CleanUpMenu));

    public static string RevertMenu => Get(nameof(RevertMenu));

    public static string RevertQuestion(string theme) => Format(nameof(RevertQuestion), theme);

    public static string CleanUpTitle => Get(nameof(CleanUpTitle));

    public static string CleanUpIntro(string theme) => Format(nameof(CleanUpIntro), theme);

    public static string CleanUpConfirm(string count) => Format(nameof(CleanUpConfirm), count);

    public static string CleanUpFailed(string reason) => Format(nameof(CleanUpFailed), reason);

    public static string NoUnusedFiles => Get(nameof(NoUnusedFiles));

    public static string DeleteFiles(string count, string size) => Format(nameof(DeleteFiles), count, size);

    public static string FilesDeleted(string count) => Format(nameof(FilesDeleted), count);

    public static string FilesKept(string files) => Format(nameof(FilesKept), files);

    public static string FileKept(string file, string reason) => Format(nameof(FileKept), file, reason);

    public static string SelectAll => Get(nameof(SelectAll));

    public static string SelectNone => Get(nameof(SelectNone));

    public static string SizeBytes(string bytes) => Format(nameof(SizeBytes), bytes);

    public static string SizeKilobytes(string kilobytes) => Format(nameof(SizeKilobytes), kilobytes);

    public static string SizeMegabytes(string megabytes) => Format(nameof(SizeMegabytes), megabytes);

    public static string SizeGigabytes(string gigabytes) => Format(nameof(SizeGigabytes), gigabytes);

    public static string Cancel => Get(nameof(Cancel));

    public static string Create => Get(nameof(Create));

    public static string CopyNameTitle => Get(nameof(CopyNameTitle));

    public static string CopyNamePrompt(string example) => Format(nameof(CopyNamePrompt), example);

    public static string ImportImageTitle => Get(nameof(ImportImageTitle));

    public static string ImportFontTitle => Get(nameof(ImportFontTitle));

    public static string ImportVideoTitle => Get(nameof(ImportVideoTitle));

    public static string ImageFiles => Get(nameof(ImageFiles));

    public static string FontFiles => Get(nameof(FontFiles));

    public static string VideoFiles => Get(nameof(VideoFiles));

    public static string AllFiles => Get(nameof(AllFiles));

    public static string ImportRefused(string file, string reason) => Format(nameof(ImportRefused), file, reason);

    public static string Import => Get(nameof(Import));

    public static string ImportFontTip => Get(nameof(ImportFontTip));

    public static string ImportFileTip => Get(nameof(ImportFileTip));

    public static string MenuGroup => Get(nameof(MenuGroup));

    public static string MenuUngroup => Get(nameof(MenuUngroup));

    public static string MenuCopy => Get(nameof(MenuCopy));

    public static string MenuPaste => Get(nameof(MenuPaste));

    public static string MenuDuplicate => Get(nameof(MenuDuplicate));

    public static string MenuDelete => Get(nameof(MenuDelete));

    public static string MenuRename => Get(nameof(MenuRename));

    public static string HideTip => Get(nameof(HideTip));

    public static string ShowTip => Get(nameof(ShowTip));

    public static string LockTip => Get(nameof(LockTip));

    public static string UnlockTip => Get(nameof(UnlockTip));

    public static string ReadOnlyLayer => Get(nameof(ReadOnlyLayer));

    public static string ReadOnlyLayerTip => Get(nameof(ReadOnlyLayerTip));

    public static string BackgroundSection => Get(nameof(BackgroundSection));

    public static string BackgroundSectionTip => Get(nameof(BackgroundSectionTip));

    public static string NothingSelected => Get(nameof(NothingSelected));

    public static string WidgetsSelected(int count) => Format(nameof(WidgetsSelected), count);

    public static string Mixed => Get(nameof(Mixed));

    public static string DefaultIs(string value) => Format(nameof(DefaultIs), value);

    public static string FromNamedStyle(string value) => Format(nameof(FromNamedStyle), value);

    public static string KeyReadOnly => Get(nameof(KeyReadOnly));

    public static string ResetTip => Get(nameof(ResetTip));

    public static string Width => Get(nameof(Width));

    public static string Height => Get(nameof(Height));

    public static string ListItem(string list, string number) => Format(nameof(ListItem), list, number);

    public static string AddItem => Get(nameof(AddItem));

    public static string RemoveItem => Get(nameof(RemoveItem));

    public static string MoveItemUp => Get(nameof(MoveItemUp));

    public static string MoveItemDown => Get(nameof(MoveItemDown));

    public static string PickColorTip => Get(nameof(PickColorTip));

    public static string Gradient => Get(nameof(Gradient));

    public static string GradientTip => Get(nameof(GradientTip));

    public static string EditGradient => Get(nameof(EditGradient));

    public static string PreviewTip => Get(nameof(PreviewTip));

    public static string SplitIntoSpans => Get(nameof(SplitIntoSpans));

    public static string SplitIntoSpansTip => Get(nameof(SplitIntoSpansTip));

    public static string StyleSection => Get(nameof(StyleSection));

    public static string NamedStyle => Get(nameof(NamedStyle));

    public static string NamedStyleTip => Get(nameof(NamedStyleTip));

    public static string NoNamedStyle => Get(nameof(NoNamedStyle));

    public static string StyleNone => Get(nameof(StyleNone));

    public static string StyleKeysSetHere(string count) => Format(nameof(StyleKeysSetHere), count);

    public static string StyleOverrides(string name, string count) => Format(nameof(StyleOverrides), name, count);

    public static string ClearOverrides => Get(nameof(ClearOverrides));

    public static string ClearOverridesTip => Get(nameof(ClearOverridesTip));

    public static string CopyStyle => Get(nameof(CopyStyle));

    public static string PasteStyle => Get(nameof(PasteStyle));

    public static string SectionText => Get(nameof(SectionText));

    public static string SectionFill => Get(nameof(SectionFill));

    public static string SectionOutline => Get(nameof(SectionOutline));

    public static string SectionEffects => Get(nameof(SectionEffects));

    public static string SectionMore => Get(nameof(SectionMore));

    public static string RulesSection => Get(nameof(RulesSection));

    public static string RulesTip => Get(nameof(RulesTip));

    public static string HueTip => Get(nameof(HueTip));

    public static string AlphaTip => Get(nameof(AlphaTip));

    public static string HexTip => Get(nameof(HexTip));

    public static string RecentColors => Get(nameof(RecentColors));

    public static string Linear => Get(nameof(Linear));

    public static string Radial => Get(nameof(Radial));

    public static string SpreadEvenly => Get(nameof(SpreadEvenly));

    public static string SpreadEvenlyTip => Get(nameof(SpreadEvenlyTip));

    public static string StopBarTip => Get(nameof(StopBarTip));

    public static string Angle => Get(nameof(Angle));

    public static string AngleTip => Get(nameof(AngleTip));

    public static string RadialTip => Get(nameof(RadialTip));

    public static string SourcePinned => Get(nameof(SourcePinned));

    public static string SourceTray => Get(nameof(SourceTray));

    public static string SourceLocal => Get(nameof(SourceLocal));

    public static string SourceSample => Get(nameof(SourceSample));

    public static string ValueMissing => Get(nameof(ValueMissing));

    public static string ValueStale(string value) => Format(nameof(ValueStale), value);

    public static string PinTip => Get(nameof(PinTip));

    public static string Pin => Get(nameof(Pin));

    public static string PinStale => Get(nameof(PinStale));

    public static string PinMissing => Get(nameof(PinMissing));

    public static string Unpin => Get(nameof(Unpin));

    public static string UnpinAll => Get(nameof(UnpinAll));

    public static string PatternTip => Get(nameof(PatternTip));

    public static string TimeTip => Get(nameof(TimeTip));

    public static string NotANumber(string text) => Format(nameof(NotANumber), text);

    public static string NotATime(string text) => Format(nameof(NotATime), text);

    public static string ProblemError => Get(nameof(ProblemError));

    public static string ProblemWarning => Get(nameof(ProblemWarning));

    // The user's UI language picks the text. A .resx is XML, which reads every line break as "\n"; multi-line
    // messages get the platform's line breaks back.
    static string Get(string name) =>
        (ResourceManager.GetString(name, CultureInfo.CurrentUICulture)
            ?? throw new InvalidOperationException($"There is no string resource named {name}."))
        .ReplaceLineEndings();

    // Numbers inside messages stay culture-invariant, like every other number Marquee shows.
    static string Format(string name, params object?[] args) => string.Format(CultureInfo.InvariantCulture, Get(name), args);
}
