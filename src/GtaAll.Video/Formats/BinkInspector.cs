using GtaAll.Core.Formats;

namespace GtaAll.Core.Formats;

/// <summary>Vidéo Bink 1 (BIK*) et Bink 2 (KB2*). Disposition identique à celle lue par FFmpeg (bink.c).</summary>
public static class BinkInspector
{
    public static bool IsBink(ReadOnlySpan<byte> h) =>
        h.Length >= 4 && ((h[0] == 'B' && h[1] == 'I' && h[2] == 'K') || (h[0] == 'K' && h[1] == 'B' && h[2] == '2'));

    public static InspectionResult Inspect(ReadOnlySpan<byte> h, long fileLength)
    {
        var f = new List<HeaderField>(); var n = new List<string>();
        if (!IsBink(h)) { n.Add("Signature Bink absente."); return new(f, n); }
        if (h.Length < 0x2C) { n.Add("En-tête Bink tronqué."); return new(f, n); }
        bool v2 = h[0] == 'K';
        f.Add(new(0, 4, "signature", $"{(char)h[0]}{(char)h[1]}{(char)h[2]}{(char)h[3]} ({(v2 ? "Bink 2" : "Bink 1")})", Confidence.Confirmed));
        uint size = ByteReader.U32(h, 4), frames = ByteReader.U32(h, 8), largest = ByteReader.U32(h, 0xC);
        uint w = ByteReader.U32(h, 0x14), ht = ByteReader.U32(h, 0x18), fn = ByteReader.U32(h, 0x1C), fd = ByteReader.U32(h, 0x20);
        uint flags = ByteReader.U32(h, 0x24), tracks = ByteReader.U32(h, 0x28);
        f.Add(new(4, 4, "taille − 8", $"{size}", Confidence.Confirmed));
        f.Add(new(8, 4, "images", $"{frames}", Confidence.Confirmed));
        f.Add(new(0xC, 4, "plus grande image (octets)", $"{largest}", Confidence.Confirmed));
        f.Add(new(0x14, 4, "largeur", $"{w}", Confidence.Confirmed));
        f.Add(new(0x18, 4, "hauteur", $"{ht}", Confidence.Confirmed));
        string fps = fd == 0 ? "invalide" : ((double)fn / fd).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        f.Add(new(0x1C, 8, "cadence (i/s)", $"{fn}/{fd} = {fps}", Confidence.Confirmed));
        f.Add(new(0x24, 4, "drapeaux vidéo", $"0x{flags:X8}", Confidence.Inferred));
        f.Add(new(0x28, 4, "pistes audio", $"{tracks}", Confidence.Confirmed));
        if (size + 8 != fileLength) n.Add($"Taille déclarée ({size + 8}) ≠ taille du fichier ({fileLength}) : fichier tronqué ou extrait d'une archive.");
        if (w == 0 || ht == 0 || w > 16384 || ht > 16384 || fd == 0) n.Add("Dimensions ou cadence incohérentes : en-tête douteux.");
        if (tracks > 256) n.Add("Nombre de pistes audio improbable.");
        n.Add("Seul l'en-tête est lu : aucun décodage vidéo n'est fourni.");
        return new(f, n);
    }
}

