using GtaAll.Core.Logging;
using GtaAll.Core.Modules;

namespace GtaAll.Core.Formats;

/// <summary>Point d'entrée unique : délègue au module qui possède le type de source. Lecture seule ; erreurs d'E/S converties en note et journalisées.</summary>
public static class FileInspector
{
    public static InspectionResult? Inspect(Sources.SourceFile file, ModuleRegistry? registry = null)
    {
        var module = (registry ?? ModuleRegistry.Default).ForType(file.Type);
        if (module == null) return null;
        try { return module.Inspect(file); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Warn("inspection", $"{file.RelativePath} illisible", ex);
            return new(Array.Empty<HeaderField>(), new[] { "Fichier illisible : " + ex.Message });
        }
    }
}
