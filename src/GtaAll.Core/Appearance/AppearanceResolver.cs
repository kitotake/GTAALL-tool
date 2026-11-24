namespace GtaAll.Core.Appearance;

/// <summary>Capacités du système, détectées par l'application WPF (injectées ici pour rester testable).</summary>
public sealed record GlassCapabilities(bool BackdropSupported, bool TransparencyEnabled, bool HighContrast)
{
    public static readonly GlassCapabilities Full = new(true, true, false);
    public static readonly GlassCapabilities NoBackdrop = new(false, true, false);
    public static readonly GlassCapabilities Opaque = new(false, false, false);
}

public abstract record BrushSpec;
public sealed record SolidBrushSpec(string Argb) : BrushSpec;
public sealed record GradientStop(double Offset, string Argb);
public sealed record LinearBrushSpec(double X1, double Y1, double X2, double Y2, IReadOnlyList<GradientStop> Stops) : BrushSpec;

public sealed record ResolvedAppearance(
    ThemeInfo Theme, StyleInfo Style, bool IsDark, bool Translucent, bool UseBackdrop,
    IReadOnlyDictionary<string, BrushSpec> Brushes, IReadOnlyList<string> Notes);

public static class AppearanceResolver
{
    /// <summary>Clés de ressources que les gabarits XAML (DynamicResource) peuvent utiliser.</summary>
    public static readonly string[] RequiredBrushKeys =
    {
        "BgBrush", "WindowBackgroundBrush", "PanelBrush", "PanelSolidBrush", "PanelFillBrush", "PanelEdgeBrush",
        "Panel2Brush", "PanelAltBrush", "PopupBrush", "InputBrush", "LineBrush", "HoverBrush", "TextBrush", "MutedBrush",
        "AccentBrush", "AccentDimBrush", "OnAccentBrush", "ErrorBrush", "WarnBrush", "OkBrush", "OverlayBrush",
        "OnSelectionBrush", "ScrollThumbBrush",
    };

    private static readonly Rgb White = new(255, 255, 255);
    private static readonly Rgb DarkOn = new(0x07, 0x11, 0x13);

    public static ResolvedAppearance Resolve(AppearanceSettings input, bool windowsIsDark, GlassCapabilities caps,
        AppearanceCatalog? catalog = null)
    {
        catalog ??= AppearanceCatalog.Default;
        var s = input.Clone();
        var notes = s.Normalize(catalog);

        var themeKey = s.FollowWindowsTheme ? (windowsIsDark ? s.WindowsDarkThemeKey : AppearanceCatalog.LightTheme) : s.ThemeKey;
        var theme = catalog.FindTheme(themeKey) ?? catalog.FindTheme(s.ThemeKey) ?? catalog.Themes[0];

        var styleKey = s.StyleKey;
        if (!catalog.Compatible(theme.Key, styleKey))
        {
            if (styleKey != AppearanceCatalog.StandardStyle)
                notes.Add($"Ce style n'est pas compatible avec « {theme.Name} » : style Standard appliqué.");
            styleKey = AppearanceCatalog.StandardStyle;
        }
        if (caps.HighContrast && styleKey != AppearanceCatalog.StandardStyle)
        {
            notes.Add("Contraste élevé de Windows détecté : style Standard imposé.");
            styleKey = AppearanceCatalog.StandardStyle;
        }
        var style = catalog.FindStyle(styleKey) ?? catalog.Styles[0];

        bool translucent = style.NeedsTranslucency && !s.ReduceTransparency && caps.TransparencyEnabled;
        if (style.NeedsTranslucency && !translucent)
            notes.Add(s.ReduceTransparency ? "Mode sans transparence activé : surfaces opaques."
                                           : "Effets de transparence désactivés dans Windows : surfaces opaques.");
        bool backdrop = translucent && caps.BackdropSupported;
        if (translucent && !backdrop)
            notes.Add("Flou du fond indisponible sur ce système : surfaces translucides sans flou.");

        var p = theme.Palette;
        Rgb bg = Rgb.Parse(p.Bg), panel = Rgb.Parse(p.Panel), panel2 = Rgb.Parse(p.Panel2), panelAlt = Rgb.Parse(p.PanelAlt),
            inputC = Rgb.Parse(p.Input), line = Rgb.Parse(p.Line), hover = Rgb.Parse(p.Hover);
        Rgb accent = Rgb.Parse(p.Accent), accentDim = Rgb.Parse(p.AccentDim), onAccent = Rgb.Parse(p.OnAccent);
        if (s.AccentHex != null)
        {
            accent = ColorMath.EnsureContrast(Rgb.Parse(s.AccentHex), panel, 3.0, out bool adjusted);
            if (adjusted) notes.Add("Accent ajusté pour rester lisible sur ce thème.");
            accentDim = ColorMath.Blend(accent, panel, theme.IsDark ? 0.34 : 0.20);
            onAccent = ColorMath.PickOn(accent, DarkOn, White);
        }

        var b = new Dictionary<string, BrushSpec>();
        BrushSpec Solid(Rgb c, double a = 1) => new SolidBrushSpec(c.Argb(a));

        b["BgBrush"] = Solid(bg); b["PanelSolidBrush"] = Solid(panel); b["PopupBrush"] = Solid(panel2);
        b["LineBrush"] = Solid(line); b["HoverBrush"] = Solid(hover);
        b["TextBrush"] = Solid(Rgb.Parse(p.Text)); b["MutedBrush"] = Solid(Rgb.Parse(p.Muted));
        b["AccentBrush"] = Solid(accent); b["AccentDimBrush"] = Solid(accentDim); b["OnAccentBrush"] = Solid(onAccent);
        b["ErrorBrush"] = Solid(Rgb.Parse(p.Error)); b["WarnBrush"] = Solid(Rgb.Parse(p.Warn)); b["OkBrush"] = Solid(Rgb.Parse(p.Ok));
        b["OverlayBrush"] = Solid(Rgb.Parse(p.Overlay)); b["OnSelectionBrush"] = Solid(Rgb.Parse(p.OnSelection));
        b["ScrollThumbBrush"] = Solid(Rgb.Parse(p.ScrollThumb));

        if (!translucent)
        {
            b["PanelBrush"] = Solid(panel); b["Panel2Brush"] = Solid(panel2); b["PanelAltBrush"] = Solid(panelAlt);
            b["InputBrush"] = Solid(inputC); b["PanelFillBrush"] = Solid(panel); b["PanelEdgeBrush"] = Solid(line);
            b["WindowBackgroundBrush"] = Solid(bg);
        }
        else
        {
            double t = s.TransparencyPercent / 100.0, a = 1 - t;
            b["PanelBrush"] = Solid(panel, a);
            b["Panel2Brush"] = Solid(panel2, Math.Min(1, a + 0.08));
            b["PanelAltBrush"] = Solid(panelAlt, a);
            b["InputBrush"] = Solid(inputC, Math.Max(0.85, 1 - t * 0.3));   // zones de lecture dense : toujours quasi opaques

            var top = theme.IsDark ? ColorMath.Blend(White, panel, 0.10) : White;
            var bottom = theme.IsDark ? panel : panel2;
            b["PanelFillBrush"] = new LinearBrushSpec(0, 0, 0, 1, new[] { new GradientStop(0, top.Argb(a)), new GradientStop(1, bottom.Argb(a)) });
            b["PanelEdgeBrush"] = theme.IsDark
                ? new LinearBrushSpec(0, 0, 1, 1, new[] { new GradientStop(0, White.Argb(0.34)), new GradientStop(1, White.Argb(0.06)) })
                : new LinearBrushSpec(0, 0, 1, 1, new[] { new GradientStop(0, White.Argb(0.95)), new GradientStop(1, line.Argb(1)) });
            b["WindowBackgroundBrush"] = backdrop
                ? Solid(bg, 0)
                : new LinearBrushSpec(0, 0, 1, 1, new[]
                {
                    new GradientStop(0, ColorMath.Blend(accent, bg, theme.IsDark ? 0.22 : 0.12).Argb(1)),
                    new GradientStop(0.55, bg.Argb(1)),
                    new GradientStop(1, ColorMath.Blend(accent, bg, theme.IsDark ? 0.10 : 0.06).Argb(1)),
                });
        }
        return new ResolvedAppearance(theme, style, theme.IsDark, translucent, backdrop, b, notes);
    }
}
