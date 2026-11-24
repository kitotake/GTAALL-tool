namespace GtaAll.Core.Appearance;

/// <summary>Couleurs d'un thème (hexadécimal #RRGGBB). Un thème ne contient QUE des couleurs.</summary>
public sealed record ThemePalette(
    string Bg, string Panel, string Panel2, string PanelAlt, string Input, string Line, string Hover,
    string Text, string Muted, string Accent, string AccentDim, string OnAccent,
    string Error, string Warn, string Ok, string ScrollThumb, string OnSelection, string Overlay);

public sealed record ThemeInfo(string Key, string Name, string Description, bool IsDark, bool AllowsTranslucency, ThemePalette Palette);

/// <summary>Un style visuel agit sur les surfaces, indépendamment du thème.</summary>
public sealed record StyleInfo(string Key, string Name, string Description, bool IsBeta, bool NeedsTranslucency);

public sealed record AccentPreset(string Name, string Hex);

public sealed class AppearanceCatalog
{
    public const string StandardStyle = "standard";
    public const string GlassStyle = "liquid-glass";
    public const string DefaultTheme = "charcoal-teal";
    public const string LightTheme = "white";

    public IReadOnlyList<ThemeInfo> Themes { get; }
    public IReadOnlyList<StyleInfo> Styles { get; }

    public AppearanceCatalog(IEnumerable<ThemeInfo> themes, IEnumerable<StyleInfo> styles)
    {
        Themes = themes.ToList(); Styles = styles.ToList();
    }

    public ThemeInfo? FindTheme(string? key) => Themes.FirstOrDefault(t => t.Key == key);
    public StyleInfo? FindStyle(string? key) => Styles.FirstOrDefault(s => s.Key == key);

    /// <summary>Un style est compatible avec un thème si le thème accepte les surfaces translucides quand le style en a besoin.</summary>
    public bool Compatible(string themeKey, string styleKey)
    {
        var t = FindTheme(themeKey); var s = FindStyle(styleKey);
        return t != null && s != null && (!s.NeedsTranslucency || t.AllowsTranslucency);
    }

    public static readonly IReadOnlyList<AccentPreset> AccentPresets = new AccentPreset[]
    {
        new("Turquoise", "#2CC4B6"), new("Bleu pétrole", "#3A9AB0"), new("Bleu", "#4C8DF6"),
        new("Violet", "#9B7BEA"), new("Vert", "#5CC47A"), new("Ambre", "#E0A93B"), new("Rose", "#E5709A"),
    };

    private static readonly ThemePalette CharcoalTeal = new(
        Bg: "#14171A", Panel: "#1C2024", Panel2: "#252A30", PanelAlt: "#20252A", Input: "#101316", Line: "#323941", Hover: "#2B3239",
        Text: "#E6EAED", Muted: "#9AA6AF", Accent: "#2CC4B6", AccentDim: "#17524D", OnAccent: "#071113",
        Error: "#F0837A", Warn: "#E6B655", Ok: "#6FD39A", ScrollThumb: "#3A434C", OnSelection: "#FFFFFF", Overlay: "#FFFFFF");

    private static readonly ThemePalette CharcoalPetrol = new(
        Bg: "#13171B", Panel: "#1B2026", Panel2: "#242B33", PanelAlt: "#1F252C", Input: "#0F1317", Line: "#303A44", Hover: "#2A333D",
        Text: "#E3E8EC", Muted: "#94A3AE", Accent: "#3A9AB0", AccentDim: "#1D4B58", OnAccent: "#06121A",
        Error: "#E8857D", Warn: "#DDB35E", Ok: "#6CC79A", ScrollThumb: "#39454F", OnSelection: "#FFFFFF", Overlay: "#FFFFFF");

    private static readonly ThemePalette OledBlack = new(
        Bg: "#000000", Panel: "#0A0A0B", Panel2: "#141516", PanelAlt: "#0E0F10", Input: "#050505", Line: "#24272A", Hover: "#1A1C1E",
        Text: "#ECEFF1", Muted: "#98A0A6", Accent: "#2CC4B6", AccentDim: "#123F3B", OnAccent: "#000000",
        Error: "#F0837A", Warn: "#E6B655", Ok: "#6FD39A", ScrollThumb: "#30343A", OnSelection: "#FFFFFF", Overlay: "#FFFFFF");

    private static readonly ThemePalette White = new(
        Bg: "#F4F6F8", Panel: "#FFFFFF", Panel2: "#EDF0F3", PanelAlt: "#F7F9FA", Input: "#FFFFFF", Line: "#D3D9DF", Hover: "#E6EAEE",
        Text: "#1B2127", Muted: "#5B6772", Accent: "#0E7C86", AccentDim: "#CFE6E8", OnAccent: "#FFFFFF",
        Error: "#B3261E", Warn: "#8A5A00", Ok: "#1E7B46", ScrollThumb: "#B8C0C8", OnSelection: "#0B1F22", Overlay: "#000000");

    public static AppearanceCatalog Default { get; } = new(
        new[]
        {
            new ThemeInfo("charcoal-teal", "Charbon + turquoise", "Thème d'origine : charbon, accent turquoise.", true, true, CharcoalTeal),
            new ThemeInfo("charcoal-petrol", "Charbon + bleu pétrole", "Variante plus discrète.", true, true, CharcoalPetrol),
            new ThemeInfo("oled-black", "Noir OLED", "Noir profond, minimaliste (sans translucidité).", true, false, OledBlack),
            new ThemeInfo("white", "Blanc", "Interface claire et épurée.", false, true, White),
        },
        new[]
        {
            new StyleInfo(StandardStyle, "Standard", "Surfaces opaques.", false, false),
            new StyleInfo(GlassStyle, "Liquid Glass (Bêta)",
                "Surfaces vitrées translucides, reflets discrets. Expérimental : pourra être retiré dans une prochaine mise à jour.", true, true),
        });
}
