using GtaAll.Core.Formats;
using GtaAll.Core.Modules;
using GtaAll.Core.Sources;

// gtaall-diag : rapport TEXTE en lecture seule, à copier-coller. N'écrit rien, ne modifie aucune source.
// Usage : gtaall-diag <fichier|dossier> [--rapide]     (--rapide : pour un .rpf, en-tête seulement, sans indexation)
foreach (IFormatModule m in new IFormatModule[] { new Rpf9Module(), new TtpfModule(), new AudioModule(), new VideoModule(), new StructuredDataModule() })
    ModuleRegistry.Default.Register(m);

var a = args.Where(x => !x.StartsWith("--")).ToArray();
bool fast = args.Contains("--rapide");
if (a.Length != 1) { Console.WriteLine("Usage : gtaall-diag <fichier|dossier> [--rapide]"); return 2; }
var target = a[0];

try
{
    if (Directory.Exists(target))
    {
        var list = SourceScanner.Scan(target);
        Console.WriteLine($"Dossier : {target}\nFichiers reconnus : {list.Count}");
        foreach (var g in list.GroupBy(x => x.Type).OrderBy(g => g.Key.ToString()))
            Console.WriteLine($"  {g.Key,-12} {g.Count(),6}   {g.Sum(x => x.Size) / 1048576:N0} Mo");
        foreach (var g in list.GroupBy(x => x.Type))
        {
            var smallest = g.OrderBy(x => x.Size).First();
            Console.WriteLine($"\n--- exemple {g.Key} (le plus petit) ---");
            Report(smallest.Path, true);
        }
        return 0;
    }
    if (!File.Exists(target)) { Console.WriteLine("Introuvable : " + target); return 1; }
    Report(target, fast);
    return 0;
}
catch (Exception ex) { Console.WriteLine($"ERREUR {ex.GetType().Name} : {ex.Message}"); return 1; }

static void Report(string path, bool fast)
{
    var fi = new FileInfo(path);
    Console.WriteLine($"Fichier : {fi.Name}   taille : {fi.Length:N0} octets ({fi.Length / 1048576.0:N1} Mo)");
    var head = ByteReader.Head(path, 64);
    Console.WriteLine("64 premiers octets : " + Convert.ToHexString(head));
    var ext = fi.Extension.ToLowerInvariant();
    if (ext == ".rpf" && Rpf9Header.HasMagic(head))
    {
        var h = Rpf9Header.Parse(head, fi.Length);
        foreach (var f in h.Fields) Console.WriteLine($"  {f.Name,-30} {f.Value}  [{f.Confidence}]");
        foreach (var w in h.Warnings) Console.WriteLine("  AVERTISSEMENT : " + w);
        if (fast) { Console.WriteLine("(--rapide : indexation non lancée)"); return; }
        Console.WriteLine("Indexation en cours (peut être longue sur un gros fichier)...");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var idx = Rpf9Indexer.Build(path, null, default);
        Console.WriteLine($"  flux zlib : {idx.StreamCount:N0} ; entrées : {idx.Entries.Count:N0} ; octets couverts : {idx.CoveredBytes / 1048576.0:N1} Mo ({100.0 * idx.CoveredBytes / Math.Max(1, fi.Length):N1} %) ; durée {sw.Elapsed.TotalSeconds:N1} s");
        foreach (var g in idx.Entries.GroupBy(e => e.Kind).OrderByDescending(g => g.Count()))
            Console.WriteLine($"  type {g.Key,-10} {g.Count(),8:N0}   {g.Sum(e => e.InflatedSize) / 1048576.0:N1} Mo décompressés");
        foreach (var w in idx.Warnings.Take(10)) Console.WriteLine("  AVERTISSEMENT : " + w);
        return;
    }
    var src = new SourceFile(path, fi.Name, fi.Length, ModuleRegistry.Default.ForExtension(ext)?.Classify(path, ext).Type ?? SourceType.Structured, "");
    var r = FileInspector.Inspect(src);
    if (r == null) { Console.WriteLine("Aucun module pour ce fichier."); return; }
    foreach (var f in r.Fields) Console.WriteLine($"  {f.Name,-38} {f.Value}  [{f.Confidence}]");
    foreach (var n in r.Notes) Console.WriteLine("  NOTE : " + n);
}
