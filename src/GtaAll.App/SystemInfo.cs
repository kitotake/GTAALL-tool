using System.Windows;
using GtaAll.Core.Appearance;
using Microsoft.Win32;

namespace GtaAll.App;

internal sealed record SystemSnapshot(bool WindowsDark, GlassCapabilities Glass);

/// <summary>Détecte le thème de Windows et ses capacités de transparence.</summary>
internal static class SystemInfo
{
    /// <summary>Le fond Mica (DWMWA_SYSTEMBACKDROP_TYPE) existe à partir de Windows 11 22H2 (build 22621).</summary>
    private const int MinBackdropBuild = 22621;

    public static SystemSnapshot Detect()
    {
        bool dark = false, transparency = true;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (k?.GetValue("AppsUseLightTheme") is int light) dark = light == 0;
            if (k?.GetValue("EnableTransparency") is int t) transparency = t != 0;
        }
        catch (Exception) { /* valeurs par défaut : clair, transparence active */ }

        bool backdrop = Environment.OSVersion.Version.Build >= MinBackdropBuild;
        return new SystemSnapshot(dark, new GlassCapabilities(backdrop, transparency, SystemParameters.HighContrast));
    }
}
