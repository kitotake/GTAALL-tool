namespace GtaAll.Core.Appearance;

/// <summary>
/// Préférences d'apparence. Thème et style sont stockés sous forme de CLÉS texte : si un style est retiré
/// d'une future version, le fichier reste lisible et le reste des préférences est conservé.
/// </summary>
public sealed class AppearanceSettings
{
    public const int MaxTransparencyPercent = 50;

    public string ThemeKey { get; set; } = AppearanceCatalog.DefaultTheme;
    public string StyleKey { get; set; } = AppearanceCatalog.StandardStyle;   // jamais la bêta par défaut
    public string? AccentHex { get; set; }                                    // null = couleur du thème
    public int TransparencyPercent { get; set; } = 30;
    public bool ReduceTransparency { get; set; }                              // mode sans transparence
    public bool FollowWindowsTheme { get; set; }
    public string WindowsDarkThemeKey { get; set; } = AppearanceCatalog.DefaultTheme;

    public AppearanceSettings Clone() => (AppearanceSettings)MemberwiseClone();

    public bool SameAs(AppearanceSettings o) =>
        ThemeKey == o.ThemeKey && StyleKey == o.StyleKey && AccentHex == o.AccentHex &&
        TransparencyPercent == o.TransparencyPercent && ReduceTransparency == o.ReduceTransparency &&
        FollowWindowsTheme == o.FollowWindowsTheme && WindowsDarkThemeKey == o.WindowsDarkThemeKey;

    /// <summary>Corrige les valeurs inconnues ou incompatibles ; renvoie les messages destinés à l'utilisateur.</summary>
    public List<string> Normalize(AppearanceCatalog? catalog = null)
    {
        catalog ??= AppearanceCatalog.Default;
        var notes = new List<string>();

        if (catalog.FindTheme(ThemeKey) == null) ThemeKey = AppearanceCatalog.DefaultTheme;
        var dark = catalog.FindTheme(WindowsDarkThemeKey);
        if (dark == null || !dark.IsDark) WindowsDarkThemeKey = AppearanceCatalog.DefaultTheme;

        var style = catalog.FindStyle(StyleKey);
        if (style == null)
        {
            notes.Add("Un style visuel n'est plus disponible : retour au style Standard. Vos autres préférences sont conservées.");
            StyleKey = AppearanceCatalog.StandardStyle;
        }
        else if (!catalog.Compatible(ThemeKey, StyleKey) && !FollowWindowsTheme)
        {
            notes.Add($"{style.Name} n'est pas compatible avec ce thème : style repassé à Standard.");
            StyleKey = AppearanceCatalog.StandardStyle;
        }

        if (AccentHex != null && !Rgb.TryParse(AccentHex, out _)) AccentHex = null;
        else if (AccentHex != null) AccentHex = Rgb.Parse(AccentHex).Hex;
        TransparencyPercent = Math.Clamp(TransparencyPercent, 0, MaxTransparencyPercent);
        return notes;
    }
}
