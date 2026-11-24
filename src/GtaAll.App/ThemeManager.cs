using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GtaAll.Core.Appearance;

namespace GtaAll.App;

/// <summary>
/// Applique l'apparence (thème + style + accent) en remplaçant les brosses de Application.Resources.
/// Les gabarits (Themes/Controls.xaml) utilisent DynamicResource : le changement est immédiat, sans redémarrage.
/// </summary>
internal static class ThemeManager
{
    public static event Action? Changed;
    public static AppearanceSettings? Current { get; private set; }
    public static ResolvedAppearance? Resolved { get; private set; }

    public static void Apply(AppearanceSettings settings)
    {
        var snap = SystemInfo.Detect();
        var r = AppearanceResolver.Resolve(settings, snap.WindowsDark, snap.Glass);
        var res = Application.Current.Resources;

        foreach (var kv in r.Brushes) res[kv.Key] = ToBrush(kv.Value);

        // Couleurs système utilisées par les gabarits par défaut (sélection, coin de défilement).
        res[SystemColors.HighlightBrushKey] = ToBrush(r.Brushes["AccentDimBrush"]);
        res[SystemColors.InactiveSelectionHighlightBrushKey] = ToBrush(r.Brushes["AccentDimBrush"]);
        res[SystemColors.HighlightTextBrushKey] = ToBrush(r.Brushes["OnSelectionBrush"]);
        res[SystemColors.InactiveSelectionHighlightTextBrushKey] = ToBrush(r.Brushes["OnSelectionBrush"]);
        res[SystemColors.ControlBrushKey] = ToBrush(r.Brushes["PanelSolidBrush"]);

        Current = settings.Clone();
        Resolved = r;
        Changed?.Invoke();
    }

    /// <summary>Réapplique l'apparence courante (ex. Windows vient de changer de thème).</summary>
    public static void Refresh(AppearanceSettings fallback) => Apply(Current ?? fallback);

    /// <summary>Lie une fenêtre au thème : barre de titre, fond Mica, mises à jour en direct.</summary>
    public static void Attach(Window w)
    {
        void Refresh()
        {
            var h = new WindowInteropHelper(w).Handle;
            if (h != IntPtr.Zero && Resolved != null) WindowBackdrop.Apply(h, Resolved);
        }
        w.SourceInitialized += (_, _) => Refresh();
        Changed += Refresh;
        w.Closed += (_, _) => Changed -= Refresh;
    }

    private static Brush ToBrush(BrushSpec spec)
    {
        Brush b;
        switch (spec)
        {
            case SolidBrushSpec s:
                b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s.Argb));
                break;
            case LinearBrushSpec l:
                var g = new LinearGradientBrush { StartPoint = new Point(l.X1, l.Y1), EndPoint = new Point(l.X2, l.Y2) };
                foreach (var st in l.Stops) g.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(st.Argb), st.Offset));
                b = g;
                break;
            default:
                throw new NotSupportedException(spec.GetType().Name);
        }
        b.Freeze();
        return b;
    }
}
