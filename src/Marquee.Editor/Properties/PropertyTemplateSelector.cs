using System.Windows;
using System.Windows.Controls;

namespace Marquee.Editor.Properties;

/// Picks a properties row's template by its control (PropertyEditor): the template keyed `<Editor>Row` in the
/// resources around the row, so templates can hold rows of their own (a block's, a list item's) through the same
/// selector without referring to each other.
public sealed class PropertyTemplateSelector : DataTemplateSelector
{
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is PropertyRowViewModel row && container is FrameworkElement element
            ? element.TryFindResource(!PropertiesView.GetRawLabels(element) && VisualChoices.Supports(row) ? "VisualChoiceRow" : row.Editor == PropertyEditor.Template && (PropertiesView.GetRawLabels(element)
                || row.Row.Kind == Core.Themes.Schema.SchemaKind.Format)
                ? "RawTemplateRow" : $"{row.Editor}Row") as DataTemplate
            : base.SelectTemplate(item, container);
}
