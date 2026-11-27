using System.Globalization;
using System.Text;
using GtaAll.Core.Formats;
using GtaAll.Core.IO;
using GtaAll.Textures.Decoding;

/// <summary>Liste, export de textures TTPF (PNG + DDS) et planche-contact HTML. Lecture seule sur la source.</summary>
internal static class TextureTools
{
    public static int Run(string ttpf, string? exportDir, string? previewPage, int max, string? hashHex, int thumb = 160, bool listOnly = false)
    {
        var st = TtpfInspector.Read(ttpf);
        ulong? hashFilter = null;
        if (hashHex != null)
        {
            if (!ulong.TryParse(hashHex.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) { Console.WriteLine("--hash : valeur hexadécimale invalide"); return 2; }
            hashFilter = h;
        }
        var list = st.Textures.Where(t => hashFilter == null || t.Hash == hashFilter).ToList();
        if (list.Count == 0) { Console.WriteLine("Aucune texture ne correspond."); return 1; }

        if (listOnly) return List(st, list, max);

        var src = Path.GetDirectoryName(Path.GetFullPath(ttpf))!;
        if (exportDir != null)
        {
            SafeFileAccess.EnsureOutputOutsideSources(src, exportDir);
            Directory.CreateDirectory(exportDir);
        }
        int sheetCode = 0;
        if (previewPage != null)
        {
            var pageDir = Path.GetDirectoryName(Path.GetFullPath(previewPage))!;
            SafeFileAccess.EnsureOutputOutsideSources(src, pageDir);
            Directory.CreateDirectory(pageDir);
            var sheet = TtpfContactSheet.Build(ttpf, max, default, thumb, hashFilter == null ? null : t => t.Hash == hashFilter);
            File.WriteAllText(previewPage, sheet.Html, new UTF8Encoding(false));
            Console.WriteLine($"planche écrite : {previewPage}");
            Console.WriteLine($"{sheet.Shown} texture(s) affichée(s), {sheet.Failed} non décodée(s).");
            foreach (var kv in sheet.Reasons.OrderByDescending(k => k.Value)) Console.WriteLine($"  non décodée x{kv.Value} : {kv.Key}");
            sheetCode = sheet.Shown > 0 ? 0 : 1;
            if (exportDir == null) return sheetCode;
        }

        using var fs = SafeFileAccess.OpenRead(ttpf);
        var cache = new TtpfChunkCache();
        int ok = 0, fail = 0, tried = 0;
        var reasons = new Dictionary<string, int>();
        foreach (var t in list)
        {
            if (ok >= max) break;
            tried++;
            var img = TtpfImages.DecodeMip0(fs, st, t, out var problem, cache);
            string name = $"{t.Hash:X16}_{t.FormatName}_{t.Width}x{t.Height}".Replace(' ', '_');
            if (img == null)
            {
                fail++; reasons[problem ?? "?"] = reasons.GetValueOrDefault(problem ?? "?") + 1;
                if (hashFilter != null) Console.WriteLine($"{name} : non décodée ({problem})");
                continue;
            }
            ok++;
            if (exportDir != null)
            {
                File.WriteAllBytes(Path.Combine(exportDir, name + ".png"), ImageFiles.PngBytes(img.Rgba, img.Width, img.Height));
                File.WriteAllBytes(Path.Combine(exportDir, name + ".dds"), ImageFiles.DdsBytes(img.Width, img.Height, t.DxgiFormat, img.RawElements));
                Console.WriteLine($"écrit : {name}.png / .dds");
            }
        }
        Console.WriteLine($"{ok} texture(s) décodée(s), {fail} non décodée(s) sur {tried} essayée(s).");
        foreach (var kv in reasons.OrderByDescending(k => k.Value)) Console.WriteLine($"  non décodée x{kv.Value} : {kv.Key}");
        return ok > 0 || sheetCode == 0 && previewPage != null ? 0 : 1;
    }

    /// <summary>Tableau des textures (aucun pixel lu) : hash, taille, format, mips, et « ok » ou la raison pour laquelle elle n'est pas décodable.</summary>
    private static int List(TtpfStore st, List<TtpfTexture> list, int max)
    {
        int shown = 0, ok = 0, total = list.Count;
        var reasons = new Dictionary<string, int>();
        Console.WriteLine($"{"#",5}  {"hash",-16}  {"taille",-11} {"format",-11} {"mips",4}  état");
        for (int i = 0; i < list.Count; i++)
        {
            var t = list[i];
            var why = TtpfImages.Unavailable(st, t);
            if (why == null) ok++; else reasons[why] = reasons.GetValueOrDefault(why) + 1;
            if (shown < max) { shown++; Console.WriteLine($"{i,5}  {t.Hash:X16}  {$"{t.Width}x{t.Height}",-11} {t.FormatName,-11} {t.Mips,4}  {why ?? "ok"}"); }
        }
        if (shown < total) Console.WriteLine($"... {total - shown} ligne(s) non affichée(s) : ajoute --max {total} pour tout voir.");
        Console.WriteLine($"{ok} décodable(s) sur {total}.");
        foreach (var kv in reasons.OrderByDescending(k => k.Value)) Console.WriteLine($"  x{kv.Value} : {kv.Key}");
        return 0;
    }
}
