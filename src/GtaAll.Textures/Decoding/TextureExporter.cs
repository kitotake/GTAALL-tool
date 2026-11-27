using GtaAll.Core.Formats;
using GtaAll.Core.IO;

namespace GtaAll.Textures.Decoding;

/// <summary>Avancement d'un export en lot.</summary>
public sealed record ExportProgress(int Done, int Total, int Exported, int Failed);

/// <summary>Bilan d'un export en lot. <see cref="Skipped"/> : textures écartées sans essai car non décodables.</summary>
public sealed record ExportResult(int Exported, int Failed, int Skipped, bool Cancelled, string OutputDir, IReadOnlyDictionary<string, int> Reasons);

/// <summary>Export de textures TTPF en PNG (+ DDS) vers un dossier de travail. Lecture seule sur la source.</summary>
public static class TextureExporter
{
    /// <summary>Nom de fichier (sans extension) d'une texture : hash, format, taille.</summary>
    public static string FileName(TtpfTexture t) => $"{t.Hash:X16}_{t.FormatName}_{t.Width}x{t.Height}".Replace(' ', '_');

    /// <summary>
    /// Décode la mip 0 de chaque ligne et écrit <c>nom.png</c> (et <c>nom.dds</c> si <paramref name="dds"/>) dans <paramref name="outDir"/>.
    /// Refuse un dossier de sortie situé dans le dossier de la source. Les textures non décodables sont comptées, pas essayées.
    /// L'annulation n'est pas une erreur : le bilan indique <see cref="ExportResult.Cancelled"/> et garde ce qui est déjà écrit.
    /// </summary>
    public static ExportResult ExportAll(string ttpfPath, TtpfStore st, IEnumerable<TextureRow> rows, string outDir, bool dds,
                                         IProgress<ExportProgress>? progress = null, CancellationToken ct = default)
    {
        var sourceDir = Path.GetDirectoryName(Path.GetFullPath(ttpfPath))!;
        SafeFileAccess.EnsureOutputOutsideSources(sourceDir, outDir);
        Directory.CreateDirectory(outDir);

        var list = rows.ToList();
        var reasons = new Dictionary<string, int>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cache = new TtpfChunkCache();
        int done = 0, ok = 0, fail = 0, skipped = 0;
        bool cancelled = false;

        using var fs = SafeFileAccess.OpenRead(ttpfPath);
        foreach (var r in list)
        {
            if (ct.IsCancellationRequested) { cancelled = true; break; }
            if (!r.Decodable)
            {
                skipped++; done++;
                reasons[r.State] = reasons.GetValueOrDefault(r.State) + 1;
            }
            else
            {
                var img = TtpfImages.DecodeMip0(fs, st, r.Texture, out var problem, cache);
                if (img == null)
                {
                    fail++;
                    var why = problem ?? "raison inconnue";
                    reasons[why] = reasons.GetValueOrDefault(why) + 1;
                }
                else
                {
                    var name = FileName(r.Texture);
                    if (!used.Add(name)) { name += "_" + r.Index; used.Add(name); } // deux textures de même hash/format/taille
                    File.WriteAllBytes(Path.Combine(outDir, name + ".png"), ImageFiles.PngBytes(img.Rgba, img.Width, img.Height));
                    if (dds) File.WriteAllBytes(Path.Combine(outDir, name + ".dds"), ImageFiles.DdsBytes(img.Width, img.Height, r.Texture.DxgiFormat, img.RawElements));
                    ok++;
                }
                done++;
            }
            progress?.Report(new ExportProgress(done, list.Count, ok, fail));
        }
        return new ExportResult(ok, fail, skipped, cancelled, outDir, reasons);
    }
}
