using GtaAll.Core.Formats;

namespace GtaAll.Core.Sources;

public enum SourceType { Rpf9, RpfOther, Ttpf, Audio, Video, Texture, Structured, Unknown }

public sealed record SourceFile(string Path, string RelativePath, long Size, SourceType Type, string Status)
{
    public bool CanIndex => Type == SourceType.Rpf9;
}

/// <summary>Liste les fichiers reconnus d'un dossier (lecture seule). Les extensions et la classification viennent des modules enregistrés.</summary>
public static class SourceScanner
{
    public static List<SourceFile> Scan(string root, CancellationToken ct = default, Modules.ModuleRegistry? registry = null)
    {
        registry ??= Modules.ModuleRegistry.Default;
        var res = new List<SourceFile>();
        var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, ReturnSpecialDirectories = false };
        foreach (var p in Directory.EnumerateFiles(root, "*", opt))
        {
            ct.ThrowIfCancellationRequested();
            var ext = Path.GetExtension(p);
            var module = registry.ForExtension(ext);
            if (module == null) continue;
            long size; try { size = new FileInfo(p).Length; } catch (IOException) { continue; }
            var c = module.Classify(p, ext);
            res.Add(new SourceFile(p, Path.GetRelativePath(root, p), size, c.Type, c.Status));
        }
        res.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return res;
    }
}
