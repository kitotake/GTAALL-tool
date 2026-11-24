using GtaAll.Core.Settings;

namespace GtaAll.App;

/// <summary>Préférences partagées par toutes les fenêtres (une seule instance, chargée au démarrage).</summary>
internal static class AppState
{
    public static SettingsStore Store { get; } = new();
    public static AppSettings Settings { get; private set; } = new();
    public static void Load() => Settings = Store.Load();
}
