using System.Globalization;
using System.Text;
using GtaAll.Core.Formats;
using GtaAll.Core.IO;
using GtaAll.Textures.Decoding;

/// <summary>Export de textures TTPF (PNG + DDS) et planche-contact HTML. Lecture seule sur la source.</summary>
internal static class TextureTools
{
    public static int Run(string ttpf, string? exportDir, string? previewPage, int max, string? hashHex)
    {
        var st = TtpfInspector.Read(ttpf);
        using var fs = SafeFileAccess.OpenRead(ttpf);
        var wanted = st.Textures.AsEnumerable();
        if (hashHex != null)
        {
            if (!ulong.TryParse(hashHex.Replace("0x", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var h)) { Console.WriteLine("--hash : valeur hexadécimale invalide"); return 2; }
            wanted = wanted.Where(t => t.Hash == h);
        }
        var list = wanted.ToList();
        if (list.Count == 0) { Console.WriteLine("Aucune texture ne correspond."); return 1; }
        if (exportDir != null)
        {
            var src = Path.GetDirectoryName(Path.GetFullPath(ttpf))!;
            SafeFileAccess.EnsureOutputOutsideSources(src, exportDir);
            Directory.CreateDirectory(exportDir);
        }
        if (previewPage != null && exportDir == null)
        {
            var pageDir = Path.GetDirectoryName(Path.GetFullPath(previewPage))!;
            SafeFileAccess.EnsureOutputOutsideSources(Path.GetDirectoryName(Path.GetFullPath(ttpf))!, pageDir);
            Directory.CreateDirectory(pageDir);
            var sheet = TtpfContactSheet.Build(ttpf, max);
            File.WriteAllText(previewPage, sheet.Html, new UTF8Encoding(false));
            Console.WriteLine($"planche écrite : {previewPage}");
            Console.WriteLine($"{sheet.Shown} texture(s) affichée(s), {sheet.Failed} non décodée(s).");
            foreach (var kv in sheet.Reasons.OrderByDescending(k => k.Value)) Console.WriteLine($"  non décodée x{kv.Value} : {kv.Key}");
            return sheet.Shown > 0 ? 0 : 1;
        }
        int ok = 0, fail = 0, tried = 0;
        var reasons = new Dictionary<string, int>();
        foreach (var t in list)
        {
            if (ok >= max) break;
            tried++;
            var img = TtpfImages.DecodeMip0(fs, st, t, out var problem);
            string name = $"{t.Hash:X16}_{t.FormatName}_{t.Width}x{t.Height}".Replace(' ', '_');
            if (img == null)
            {
                fail++; reasons[problem ?? "?"] = reasons.GetValueOrDefault(problem ?? "?") + 1;
                if (hashHex != null) Console.WriteLine($"{name} : non décodée ({problem})");
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
        return ok > 0 ? 0 : 1;
    }
}
