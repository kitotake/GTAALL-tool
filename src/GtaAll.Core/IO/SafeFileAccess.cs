using GtaAll.Core.Settings;

namespace GtaAll.Core.IO;

/// <summary>Règles d'accès aux fichiers communes à tous les modules : sources en lecture seule, sorties hors des sources.</summary>
public static class SafeFileAccess
{
    public static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    /// <summary>Refuse un dossier de sortie situé dans le dossier des sources.</summary>
    public static void EnsureOutputOutsideSources(string sourceDir, string outputDir)
    {
        if (PathUtil.IsInside(outputDir, sourceDir))
            throw new InvalidOperationException("Le dossier d'export ne doit pas se trouver dans le dossier des sources.");
    }
}
