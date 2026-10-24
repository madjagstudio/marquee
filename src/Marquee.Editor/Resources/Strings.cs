using System.Globalization;
using System.Resources;

namespace Marquee.Editor.Resources;

/// The theme editor's text (its window, toolbar, banners and dialogs), from Strings.resx in English and from
/// Strings.<culture>.resx where a translation exists. Each property (plain text) or method (text with placeholders)
/// reads the resource of the same name; the tests check that the two lists match. Public, so XAML can bind to the
/// properties ({x:Static}).
public static class Strings
{
    public static string ShaderConstantRequired => Get(nameof(ShaderConstantRequired));
    public static string ModuleUnavailable => Get(nameof(ModuleUnavailable));
    public static string PercentUnit => Get(nameof(PercentUnit));
    public static string BlendMultiplyTip => Get(nameof(BlendMultiplyTip));
    public static string GaugeArcTip => Get(nameof(GaugeArcTip));
    public static string StrikeTip => Get(nameof(StrikeTip));
    public static string CaseLowerIcon => Get(nameof(CaseLowerIcon));
    public static string BoldTip => Get(nameof(BoldTip));
    public static string GraphColumnsTip => Get(nameof(GraphColumnsTip));
    public static string BlendScreenTip => Get(nameof(BlendScreenTip));
    public static string BlendAddTip => Get(nameof(BlendAddTip));
    public static string CaseAsWrittenIcon => Get(nameof(CaseAsWrittenIcon));
    public static string TextAlignRightTip => Get(nameof(TextAlignRightTip));
    public static string UnderlineTip => Get(nameof(UnderlineTip));
    public static string BlendNormalTip => Get(nameof(BlendNormalTip));
    public static string GaugeDialTip => Get(nameof(GaugeDialTip));
    public static string GraphLineTip => Get(nameof(GraphLineTip));
    public static string TextAlignLeftTip => Get(nameof(TextAlignLeftTip));
    public static string ItalicIcon => Get(nameof(ItalicIcon));
    public static string StrikeIcon => Get(nameof(StrikeIcon));
    public static string GaugeFormChange => Get(nameof(GaugeFormChange));
    public static string BlendOverlayTip => Get(nameof(BlendOverlayTip));
    public static string ItalicTip => Get(nameof(ItalicTip));
    public static string BoldIcon => Get(nameof(BoldIcon));
    public static string CaseUpperIcon => Get(nameof(CaseUpperIcon));
    public static string GraphAreaTip => Get(nameof(GraphAreaTip));
    public static string BlendLightenTip => Get(nameof(BlendLightenTip));
    public static string CaseLowerTip => Get(nameof(CaseLowerTip));
    public static string TextAlignCenterTip => Get(nameof(TextAlignCenterTip));
    public static string GaugeRingTip => Get(nameof(GaugeRingTip));
    public static string CaseAsWrittenTip => Get(nameof(CaseAsWrittenTip));
    public static string BlendDarkenTip => Get(nameof(BlendDarkenTip));
    public static string UnderlineIcon => Get(nameof(UnderlineIcon));
    public static string CaseUpperTip => Get(nameof(CaseUpperTip));
    public static string MyModuleNamePrompt => Get(nameof(MyModuleNamePrompt));
    public static string DeleteModule => Get(nameof(DeleteModule));
    public static string RenameModule => Get(nameof(RenameModule));
    public static string MyModuleSuggestion => Get(nameof(MyModuleSuggestion));
    public static string SaveAsModule => Get(nameof(SaveAsModule));
    public static string SaveAsModuleTip => Get(nameof(SaveAsModuleTip));
    public static string MyModuleName => Get(nameof(MyModuleName));
    public static string NewTheme => Get(nameof(NewTheme));
    public static string StartBlank => Get(nameof(StartBlank));
    public static string StartGaming => Get(nameof(StartGaming));
    public static string StartMonitor => Get(nameof(StartMonitor));
    public static string StartLandscape => Get(nameof(StartLandscape));
    public static string StartPortrait => Get(nameof(StartPortrait));
    public static string StartThemeTip => Get(nameof(StartThemeTip));
    public static string OpenYourTheme => Get(nameof(OpenYourTheme));
    public static string OpenYourThemeTip => Get(nameof(OpenYourThemeTip));
    public static string ModuleGallery => Get(nameof(ModuleGallery));
    public static string AddModule => Get(nameof(AddModule));
    public static string AddModuleTip => Get(nameof(AddModuleTip));
    public static string BackgroundAurora => Get(nameof(BackgroundAurora));
    public static string BackgroundWaves => Get(nameof(BackgroundWaves));
    public static string BackgroundPlasma => Get(nameof(BackgroundPlasma));
    public static string BackgroundStarfield => Get(nameof(BackgroundStarfield));
    public static string BackgroundDrift => Get(nameof(BackgroundDrift));
    public static string BackgroundNebula => Get(nameof(BackgroundNebula));
    public static string ShaderReactionSentence => Get(nameof(ShaderReactionSentence));
    public static string ShaderCpuLoad => Get(nameof(ShaderCpuLoad));
    public static string ShaderSpeedUnit => Get(nameof(ShaderSpeedUnit));
    public static string ShaderMissingValue => Get(nameof(ShaderMissingValue));
    public static string BackgroundGallery => Get(nameof(BackgroundGallery));
    public static string MovingBackgrounds => Get(nameof(MovingBackgrounds));
    public static string StillBackgrounds => Get(nameof(StillBackgrounds));
    public static string MoreBackgrounds => Get(nameof(MoreBackgrounds));
    public static string UseBackgroundTip => Get(nameof(UseBackgroundTip));
    public static string StillColour => Get(nameof(StillColour));
    public static string StillGradient => Get(nameof(StillGradient));
    public static string StillPicture => Get(nameof(StillPicture));
    public static string StillVideo => Get(nameof(StillVideo));
    public static string ImportBackgroundShader => Get(nameof(ImportBackgroundShader));
    public static string ShaderColourTip => Get(nameof(ShaderColourTip));
    public static string ShaderSpeedTip => Get(nameof(ShaderSpeedTip));
    public static string ShaderStrengthTip => Get(nameof(ShaderStrengthTip));
    public static string ShaderQualityTip => Get(nameof(ShaderQualityTip));
    public static string ShaderReacts => Get(nameof(ShaderReacts));
    public static string ShaderReactTip => Get(nameof(ShaderReactTip));
    public static string ShaderNothing => Get(nameof(ShaderNothing));
    public static string ShaderNothingTip => Get(nameof(ShaderNothingTip));
    public static string ShaderCalm => Get(nameof(ShaderCalm));
    public static string ShaderBusy => Get(nameof(ShaderBusy));
    public static string ShaderQuality => Get(nameof(ShaderQuality));
    public static string ShaderLight => Get(nameof(ShaderLight));
    public static string ShaderHeavy => Get(nameof(ShaderHeavy));
    public static string ShaderTooHeavy => Get(nameof(ShaderTooHeavy));
    public static string ShaderSlowIcon => Get(nameof(ShaderSlowIcon));
    public static string ShaderFastIcon => Get(nameof(ShaderFastIcon));
    public static string ShaderFaintIcon => Get(nameof(ShaderFaintIcon));
    public static string ShaderFullIcon => Get(nameof(ShaderFullIcon));
    public static string TryValue => Get(nameof(TryValue));
    public static string TryValueTip => Get(nameof(TryValueTip));
    public static string AddLayerTip => Get(nameof(AddLayerTip));
    public static string AddFillLayerTip => Get(nameof(AddFillLayerTip));
    public static string AddImageTip => Get(nameof(AddImageTip));
    public static string AddVideoTip => Get(nameof(AddVideoTip));
    public static string AddShaderLayerTip => Get(nameof(AddShaderLayerTip));
    public static string GroupTip => Get(nameof(GroupTip));
    public static string GroupPickHint => Get(nameof(GroupPickHint));
    public static string UngroupTip => Get(nameof(UngroupTip));
    public static string CopyTip => Get(nameof(CopyTip));
    public static string PasteTip => Get(nameof(PasteTip));
    public static string DuplicateTip => Get(nameof(DuplicateTip));
    public static string DeleteItemTip => Get(nameof(DeleteItemTip));
    public static string DeleteStyleTip => Get(nameof(DeleteStyleTip));
    public static string NewFromExampleTip => Get(nameof(NewFromExampleTip));
    public static string NoExamplesTip => Get(nameof(NoExamplesTip));
    public static string OpenThemeTip(string name) => Format(nameof(OpenThemeTip), name);
    public static string CopyExampleTip(string name) => Format(nameof(CopyExampleTip), name);
    public static string AlignLeftTip => Get(nameof(AlignLeftTip));
    public static string AlignCenterTip => Get(nameof(AlignCenterTip));
    public static string AlignRightTip => Get(nameof(AlignRightTip));
    public static string AlignTopTip => Get(nameof(AlignTopTip));
    public static string AlignMiddleTip => Get(nameof(AlignMiddleTip));
    public static string AlignBottomTip => Get(nameof(AlignBottomTip));
    public static string DistributeHorizontallyTip => Get(nameof(DistributeHorizontallyTip));
    public static string DistributeVerticallyTip => Get(nameof(DistributeVerticallyTip));
    public static string AddTextWidgetTip => Get(nameof(AddTextWidgetTip));
    public static string AddValueWidgetTip => Get(nameof(AddValueWidgetTip));
    public static string AddBarWidgetTip => Get(nameof(AddBarWidgetTip));
    public static string AddGaugeWidgetTip => Get(nameof(AddGaugeWidgetTip));
    public static string AddGraphWidgetTip => Get(nameof(AddGraphWidgetTip));
    public static string AddImageWidgetTip => Get(nameof(AddImageWidgetTip));
    public static string AddVideoWidgetTip => Get(nameof(AddVideoWidgetTip));
    public static string AddShapeWidgetTip => Get(nameof(AddShapeWidgetTip));
    public static string AddDonutWidgetTip => Get(nameof(AddDonutWidgetTip));
    public static string AddGroupWidgetTip => Get(nameof(AddGroupWidgetTip));
    public static string CleanUpTip => Get(nameof(CleanUpTip));
    public static string SaveToFolderTip => Get(nameof(SaveToFolderTip));
    public static string SaveDetailsTip => Get(nameof(SaveDetailsTip));
    public static string SelectProblemTip => Get(nameof(SelectProblemTip));
    public static string TestDateTip => Get(nameof(TestDateTip));
    public static string OpenCalendarTip => Get(nameof(OpenCalendarTip));
    public static string ChooseDateTip => Get(nameof(ChooseDateTip));
    public static string ChoosePeriodTip => Get(nameof(ChoosePeriodTip));
    public static string PreviousPeriodTip => Get(nameof(PreviousPeriodTip));
    public static string NextPeriodTip => Get(nameof(NextPeriodTip));
    public static string CalendarViewTip => Get(nameof(CalendarViewTip));
    public static string GradientAngleTip => Get(nameof(GradientAngleTip));
    public static string ZoomFitTip => Get(nameof(ZoomFitTip));
    public static string ZoomActualTip => Get(nameof(ZoomActualTip));
    public static string ZoomDoubleTip => Get(nameof(ZoomDoubleTip));
    public static string KeepEditingTip => Get(nameof(KeepEditingTip));
    public static string RevertTip => Get(nameof(RevertTip));
    public static string MakeCopyTip => Get(nameof(MakeCopyTip));
    public static string AddWidgetTip => Get(nameof(AddWidgetTip));
    public static string ArrangeTip => Get(nameof(ArrangeTip));
    public static string ProblemsTip => Get(nameof(ProblemsTip));
    public static string UndoTip => Get(nameof(UndoTip));
    public static string RedoTip => Get(nameof(RedoTip));
    public static string NameTip => Get(nameof(NameTip));
    public static string CreateTip => Get(nameof(CreateTip));
    public static string CancelTip => Get(nameof(CancelTip));
    public static string RearrangeYesTip => Get(nameof(RearrangeYesTip));
    public static string RearrangeNoTip => Get(nameof(RearrangeNoTip));
    public static string SelectAllFilesTip => Get(nameof(SelectAllFilesTip));
    public static string SelectNoFilesTip => Get(nameof(SelectNoFilesTip));
    public static string SelectFileTip => Get(nameof(SelectFileTip));
    public static string DeleteFilesTip => Get(nameof(DeleteFilesTip));
    public static string FoldLayerTip => Get(nameof(FoldLayerTip));
    public static string RenameLayerTip => Get(nameof(RenameLayerTip));
    public static string NewStyleTip => Get(nameof(NewStyleTip));
    public static string RenameStyleTip => Get(nameof(RenameStyleTip));
    public static string EditGradientTip => Get(nameof(EditGradientTip));
    public static string LinearGradientTip => Get(nameof(LinearGradientTip));
    public static string RadialGradientTip => Get(nameof(RadialGradientTip));
    public static string RecentColorTip => Get(nameof(RecentColorTip));
    public static string AddItemTip => Get(nameof(AddItemTip));
    public static string ItemSectionTip => Get(nameof(ItemSectionTip));
    public static string StylingSectionTip => Get(nameof(StylingSectionTip));
    public static string RawSectionTip => Get(nameof(RawSectionTip));
    public static string RingSectionTip => Get(nameof(RingSectionTip));
    public static string LayerKindTip => Get(nameof(LayerKindTip));
    public static string RemoveUniformTip => Get(nameof(RemoveUniformTip));
    public static string SaveAsStyleTip => Get(nameof(SaveAsStyleTip));
    public static string CopyStyleTip => Get(nameof(CopyStyleTip));
    public static string PasteStyleTip => Get(nameof(PasteStyleTip));
    public static string RuleConditionTip => Get(nameof(RuleConditionTip));
    public static string RuleValueTip => Get(nameof(RuleValueTip));
    public static string RuleStyleTip => Get(nameof(RuleStyleTip));
    public static string DeleteRuleTip => Get(nameof(DeleteRuleTip));
    public static string RuleSectionTip => Get(nameof(RuleSectionTip));
    public static string AddRuleTip => Get(nameof(AddRuleTip));
    public static string PickerModeTip => Get(nameof(PickerModeTip));
    public static string PickerSearchTip => Get(nameof(PickerSearchTip));
    public static string PickerFromLowTip => Get(nameof(PickerFromLowTip));
    public static string PickerFromHighTip => Get(nameof(PickerFromHighTip));
    public static string PickerToLowTip => Get(nameof(PickerToLowTip));
    public static string PickerToHighTip => Get(nameof(PickerToHighTip));
    public static string PickerNumberTip => Get(nameof(PickerNumberTip));
    public static string PickerAdvancedTip => Get(nameof(PickerAdvancedTip));
    public static string UnpinAllTip => Get(nameof(UnpinAllTip));
    public static string TestNumberTip => Get(nameof(TestNumberTip));
    public static string PinStaleTip => Get(nameof(PinStaleTip));
    public static string PinMissingTip => Get(nameof(PinMissingTip));
    public static string UnpinTip => Get(nameof(UnpinTip));
    public static string PinTimeTip => Get(nameof(PinTimeTip));
    public static string DecreaseValueTip => Get(nameof(DecreaseValueTip));
    public static string IncreaseValueTip => Get(nameof(IncreaseValueTip));
    public static string ScrollBackTip => Get(nameof(ScrollBackTip));
    public static string ScrollForwardTip => Get(nameof(ScrollForwardTip));
    public static string ExpandTreeTip => Get(nameof(ExpandTreeTip));
    public static string BasicsSection => Get(nameof(BasicsSection));
    public static string MoreStylingSection => Get(nameof(MoreStylingSection));
    public static string RawSection => Get(nameof(RawSection));
    public static string SensorGroupTip => Get(nameof(SensorGroupTip));
    public static string LayersToggleTip => Get(nameof(LayersToggleTip));
    public static string ScreenTag(string orientation, int width, int height) => Format(nameof(ScreenTag), orientation, width, height);
    public static string ScreenMismatchTip(string theme, string screen) => Format(nameof(ScreenMismatchTip), theme, screen);
    public static string ScreenOrientationTip => Get(nameof(ScreenOrientationTip));
    public static string StylesSection => Get(nameof(StylesSection));
    public static string NewStyle => Get(nameof(NewStyle));
    public static string SaveAsStyle => Get(nameof(SaveAsStyle));
    public static string StyleNamePrompt => Get(nameof(StyleNamePrompt));
    public static string StyleUsedBy(int count) => Format(nameof(StyleUsedBy), count);
    public static string StyleCopiedInto(string name, int count) => Format(nameof(StyleCopiedInto), name, count);
    public static string PickerSensor => Get(nameof(PickerSensor));
    public static string PickerNumber => Get(nameof(PickerNumber));
    public static string PickerAdvanced => Get(nameof(PickerAdvanced));
    public static string PickerMode => Get(nameof(PickerMode));
    public static string PickerSearch => Get(nameof(PickerSearch));
    public static string PickerFromLow => Get(nameof(PickerFromLow));
    public static string PickerFromHigh => Get(nameof(PickerFromHigh));
    public static string PickerToLow => Get(nameof(PickerToLow));
    public static string PickerToHigh => Get(nameof(PickerToHigh));
    public static string PickerNoReading => Get(nameof(PickerNoReading));
    public static string PickerChooseSensor => Get(nameof(PickerChooseSensor));
    public static string PickerInvalidNumber => Get(nameof(PickerInvalidNumber));
    public static string PickerInvalidRange => Get(nameof(PickerInvalidRange));
    public static string ThemeSettings => Get(nameof(ThemeSettings));
    public static string LayerKind => Get(nameof(LayerKind));
    public static string LayerFill => Get(nameof(LayerFill));
    public static string LayerImage => Get(nameof(LayerImage));
    public static string LayerVideo => Get(nameof(LayerVideo));
    public static string LayerShader => Get(nameof(LayerShader));
    public static string AddLayerMenu => Get(nameof(AddLayerMenu));
    public static string AddLayerImage => Get(nameof(AddLayerImage));
    public static string AddLayerVideo => Get(nameof(AddLayerVideo));
    public static string AddLayerShader => Get(nameof(AddLayerShader));
    public static string UnusedUniform => Get(nameof(UnusedUniform));
    public static string RemoveUniform => Get(nameof(RemoveUniform));
    public static string ImportShaderTitle => Get(nameof(ImportShaderTitle));
    public static string ShaderFiles => Get(nameof(ShaderFiles));
    public static string RearrangeQuestion(string orientation) => Format(nameof(RearrangeQuestion), orientation);
    public static string PartlyOffCanvas(int count) => Format(nameof(PartlyOffCanvas), count);
    public static string ShaderCostOver(double cost, double budget, double fps) => Format(nameof(ShaderCostOver), cost, budget, fps);
    public static string ShaderCostWithin(double cost, double budget, double fps) => Format(nameof(ShaderCostWithin), cost, budget, fps);
    public static string Yes => Get(nameof(Yes));
    public static string No => Get(nameof(No));

    /// Settable so tests can put a pseudo-translation in front of the real resources.
    internal static ResourceManager ResourceManager { get; set; } = new("Marquee.Editor.Resources.Strings", typeof(Strings).Assembly);

    public static string AppName => Get(nameof(AppName));

    public static string WindowTitle(string theme) => Format(nameof(WindowTitle), theme);

    public static string Usage => Get(nameof(Usage));

    public static string RefuseElevated => Get(nameof(RefuseElevated));

    public static string ThemeNotFound(string error) => Format(nameof(ThemeNotFound), error);

    public static string CouldNotOpen(string theme, string reason) => Format(nameof(CouldNotOpen), theme, reason);

    public static string RecoveryWaiting(string path) => Format(nameof(RecoveryWaiting), path);

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

    public static string AddRule => Get(nameof(AddRule));
    public static string RuleCustom => Get(nameof(RuleCustom));
    public static string RuleWhen => Get(nameof(RuleWhen));
    public static string RuleUse => Get(nameof(RuleUse));
    public static string RuleSet => Get(nameof(RuleSet));
    public static string RuleOn => Get(nameof(RuleOn));
    public static string RuleDelete => Get(nameof(RuleDelete));
    public static string RuleDrag => Get(nameof(RuleDrag));
    public static string RingSection => Get(nameof(RingSection));
    public static string RingOverridden => Get(nameof(RingOverridden));

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
