using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GtaAll.Core.Settings;

namespace GtaAll.App;

/// <summary>Construit la liste de cases à cocher des modules (assistant et paramètres).</summary>
internal static class ModuleUi
{
    public static Dictionary<ModuleId, CheckBox> Build(Panel host, AppSettings settings)
    {
        host.Children.Clear();
        var map = new Dictionary<ModuleId, CheckBox>();
        foreach (var m in ModuleCatalog.All)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = m.Name, FontWeight = FontWeights.SemiBold });
            head.Children.Add(Badge(m.Implemented ? "Disponible" : "À venir", m.Implemented ? "OkBrush" : "WarnBrush"));

            var body = new StackPanel();
            body.Children.Add(head);
            var desc = new TextBlock { Text = m.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0), MaxWidth = 460 };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            body.Children.Add(desc);

            var cb = new CheckBox
            {
                Content = body,
                IsEnabled = m.Implemented,
                IsChecked = m.Implemented && settings.EnabledModules.Contains(m.Id),
                Margin = new Thickness(0, 0, 0, 12),
                ToolTip = m.Implemented ? null : "Ce module n'est pas encore implémenté."
            };
            host.Children.Add(cb);
            map[m.Id] = cb;
        }
        return map;
    }

    public static List<ModuleId> Read(Dictionary<ModuleId, CheckBox> map) =>
        map.Where(kv => ModuleCatalog.Get(kv.Key).Implemented && kv.Value.IsChecked == true).Select(kv => kv.Key).ToList();

    private static Border Badge(string text, string brushKey)
    {
        var label = new TextBlock { Text = text, FontSize = 11 };
        label.SetResourceReference(TextBlock.ForegroundProperty, brushKey);
        var badge = new Border
        {
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 0, 8, 1), Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = label
        };
        badge.SetResourceReference(Border.BorderBrushProperty, brushKey);
        return badge;
    }
}
