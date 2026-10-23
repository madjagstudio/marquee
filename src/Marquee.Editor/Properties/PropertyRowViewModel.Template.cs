using Marquee.Core.Expressions;
using Marquee.Editor.Model;
using Marquee.Editor.Model.Values;

namespace Marquee.Editor.Properties;

public sealed partial class PropertyRowViewModel
{
    public ValueSources? TemplateSources => _owner.Workspace?.Values;
    public TimeLocale? TemplateLocale => _owner.Locale;
    public string? TemplateValueSource => _owner.Model?.Widgets is [{ Type: "value" } widget]
        && _owner.Session?.Document.TryGetScalar(widget.Path.Child("source"), out var source) == true ? source : null;
    public bool IsTextWidget => _owner.Model?.Type == "text" && Name == "text";
    public ValuePickerViewModel? TextPicker { get; private set; }
    public bool ShowTextPicker { get; private set; }

    void LoadTextPicker()
    {
        if (!IsTextWidget || TemplateSources is not { } sources) return;
        try
        {
            var tokens = TextTemplate.Parse(Text).OfType<TemplatePart.Token>().Where(t => t.Kind == TokenKind.Reading).ToList();
            ShowTextPicker = !IsMixed && tokens.Count <= 1 && !TextTemplate.IsClock(Text);
            var key = tokens.Count == 1 && ValuePicker.Read(tokens[0].Key) is PickedValue.Sensor sensor
                ? sensor.Key : null;
            if (TextPicker is null)
            {
                TextPicker = new ValuePickerViewModel(sources, key, false) { SensorsOnly = true };
                TextPicker.Committed += (_, _) =>
                {
                    if (!IsEditable || !ShowTextPicker || TextPicker.SelectedKey is not { } picked) return;
                    Text = TextTemplate.PickReading(Text, picked);
                    Commit();
                };
            }
            else TextPicker.Update(key ?? "");
        }
        catch (ExpressionException) { ShowTextPicker = false; }
        Raise(nameof(TextPicker));
        Raise(nameof(ShowTextPicker));
    }
}
