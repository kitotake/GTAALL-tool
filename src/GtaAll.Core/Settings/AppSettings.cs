using System.Text.Json;
using System.Text.Json.Serialization;
using GtaAll.Core.Appearance;

namespace GtaAll.Core.Settings;

public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public bool FirstRunCompleted { get; set; }
    public List<ModuleId> EnabledModules { get; set; } = new() { ModuleId.Rpf9 };
    /// <summary>Dossier de cache et d'export choisi ; vide = dossier par défaut.</summary>
    public string? WorkDirectory { get; set; }
    public string? LastSourceDirectory { get; set; }

    public AppearanceSettings Appearance { get; set; } = new();
    /// <summary>Apparence précédente (pour revenir en arrière immédiatement).</summary>
    public AppearanceSettings? PreviousAppearance { get; set; }

    /// <summary>Messages à afficher au démarrage (ex. style retiré). Non enregistré.</summary>
    [JsonIgnore] public List<string> StartupNotices { get; } = new();

    /// <summary>Enregistre une nouvelle apparence et mémorise l'ancienne si elle diffère.</summary>
    public void CommitAppearance(AppearanceSettings newValue)
    {
        var v = newValue.Clone(); v.Normalize();
        if (!v.SameAs(Appearance)) PreviousAppearance = Appearance.Clone();
        Appearance = v;
    }

    /// <summary>Échange apparence courante et précédente ; renvoie false s'il n'y a pas de précédente.</summary>
    public bool SwapWithPreviousAppearance()
    {
        if (PreviousAppearance == null) return false;
        (Appearance, PreviousAppearance) = (PreviousAppearance, Appearance);
        return true;
    }

    /// <summary>Copie des valeurs que la fenêtre Paramètres modifie, pour annuler proprement si l'enregistrement échoue.</summary>
    public sealed record Snapshot(List<ModuleId> EnabledModules, string? WorkDirectory, AppearanceSettings Appearance, AppearanceSettings? PreviousAppearance);

    public Snapshot TakeSnapshot() =>
        new(new List<ModuleId>(EnabledModules), WorkDirectory, Appearance.Clone(), PreviousAppearance?.Clone());

    public void Restore(Snapshot s)
    {
        EnabledModules = new List<ModuleId>(s.EnabledModules);
        WorkDirectory = s.WorkDirectory;
        Appearance = s.Appearance.Clone();
        PreviousAppearance = s.PreviousAppearance?.Clone();
    }

    public static string DefaultWorkDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GTAALL-tool", "work");

    public string EffectiveWorkDirectory =>
        string.IsNullOrWhiteSpace(WorkDirectory) ? DefaultWorkDirectory() : WorkDirectory!;

    /// <summary>Activé = implémenté ET choisi par l'utilisateur.</summary>
    public bool IsEnabled(ModuleId id) => ModuleCatalog.Get(id).Implemented && EnabledModules.Contains(id);

    /// <summary>
    /// Retire les modules inconnus ou non implémentés ; garantit une liste sans doublons. Ne lève jamais d'exception :
    /// un fichier écrit par une version plus récente ou édité à la main peut contenir une valeur hors énumération ou null.
    /// </summary>
    public void Normalize()
    {
        EnabledModules = EnabledModules == null
            ? new List<ModuleId> { ModuleId.Rpf9 }   // liste absente : valeur par défaut (une liste vide, elle, est respectée)
            : EnabledModules.Where(m => Enum.IsDefined(m)).Distinct().Where(m => ModuleCatalog.Get(m).Implemented).ToList();
        Appearance ??= new AppearanceSettings();
        StartupNotices.AddRange(Appearance.Normalize());
        PreviousAppearance?.Normalize();
    }

    public bool WorkDirIsInside(string? sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot)) return false;
        return PathUtil.IsInside(EffectiveWorkDirectory, sourceRoot);
    }
}

public static class PathUtil
{
    public static bool IsInside(string child, string parent)
    {
        static string N(string p) => Path.GetFullPath(p).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        return N(child).StartsWith(N(parent), StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>Préférences locales en JSON (%APPDATA%\GTAALL-tool\settings.json par défaut).</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };
    public string FilePath { get; }

    public SettingsStore(string? filePath = null)
    {
        FilePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GTAALL-tool", "settings.json");
    }

    /// <summary>Fichier absent ou corrompu : paramètres par défaut (premier démarrage).</summary>
    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath));
                if (s != null) { s.Normalize(); return s; }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        return new AppSettings();
    }

    /// <summary>
    /// Applique <paramref name="change"/> puis enregistre. Si l'écriture échoue, les valeurs d'origine sont rétablies
    /// en mémoire (sinon « Annuler » ne restaurerait rien) et l'erreur est propagée.
    /// </summary>
    public void ApplyAndSave(AppSettings settings, Action<AppSettings> change)
    {
        var before = settings.TakeSnapshot();
        try { change(settings); Save(settings); }
        catch { settings.Restore(before); throw; }
    }

    public void Save(AppSettings s)
    {
        s.Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Opt));
        File.Move(tmp, FilePath, true);
    }
}
