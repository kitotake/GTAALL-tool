using GtaAll.Core.Formats;

namespace GtaAll.Core.Sources;

public enum SourceType { Rpf9, RpfOther, Ttpf, Audio, Video, Texture, Structured, Unknown }

public sealed record SourceFile(string Path, string RelativePath, long Size, SourceType Type, string Status)
{
    public bool CanIndex => Type == SourceType.Rpf9;
}

/// <summary>Liste les fichiers reconnus d'un dossier (lecture seule).</summary>
public static class SourceScanner
{
    private static readonly Dictionary<string, SourceType> Ext = new(StringComparer.OrdinalIgnoreCase)
    {
        [".rpf"] = SourceType.Rpf9, [".ttpf"] = SourceType.Ttpf,
        [".awc"] = SourceType.Audio, [".wav"] = SourceType.Audio, [".ogg"] = SourceType.Audio, [".bik"] = SourceType.Video, [".bk2"] = SourceType.Video,
        [".dds"] = SourceType.Texture, [".xml"] = SourceType.Structured, [".pso"] = SourceType.Structured,
        [".rbf"] = SourceType.Structured,
    };

    public static List<SourceFile> Scan(string root, CancellationToken ct = default)
    {
        var res = new List<SourceFile>();
        var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, ReturnSpecialDirectories = false };
        foreach (var p in Directory.EnumerateFiles(root, "*", opt))
        {
            ct.ThrowIfCancellationRequested();
            if (!Ext.TryGetValue(Path.GetExtension(p), out var t)) continue;
            long size; try { size = new FileInfo(p).Length; } catch (IOException) { continue; }
            var rel = Path.GetRelativePath(root, p);
            string status;
            if (t == SourceType.Rpf9)
            {
                var b = new byte[4];
                try
                {
                    using var s = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read);
                    int n = s.Read(b, 0, 4);
                    if (n == 4 && Rpf9Header.HasMagic(b)) status = "RPF9 : inspection et indexation disponibles";
                    else { t = SourceType.RpfOther; status = "RPF d'une autre version ou illisible : non pris en charge"; }
                }
                catch (IOException) { t = SourceType.RpfOther; status = "Illisible"; }
            }
            else status = t switch
            {
                SourceType.Ttpf => "TTPF : lecture de l'index disponible (pas de décodage des pixels)",
                SourceType.Audio => "Audio : inspection de l'en-tête disponible (pas de lecture)",
                SourceType.Video => "Vidéo : inspection de l'en-tête disponible (pas de lecture)",
                SourceType.Texture => "Reconnu — prévisualisation pas encore implémentée",
                _ => "Données structurées : analyse disponible"
            };
            res.Add(new SourceFile(p, rel, size, t, status));
        }
        res.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
        return res;
    }
}
