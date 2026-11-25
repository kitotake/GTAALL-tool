namespace GtaAll.Core.Formats;

/// <summary>Résultat d'une inspection en lecture seule : champs d'en-tête + notes.</summary>
public sealed record InspectionResult(IReadOnlyList<HeaderField> Fields, IReadOnlyList<string> Notes);

internal static class Rd
{
    public static uint U32(ReadOnlySpan<byte> b, int o) => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(b.Slice(o, 4));
    public static ushort U16(ReadOnlySpan<byte> b, int o) => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(o, 2));
    public static byte[] Head(string path, int max)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[(int)Math.Min(max, fs.Length)];
        fs.ReadExactly(buf);
        return buf;
    }
}

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
        uint size = Rd.U32(h, 4), frames = Rd.U32(h, 8), largest = Rd.U32(h, 0xC);
        uint w = Rd.U32(h, 0x14), ht = Rd.U32(h, 0x18), fn = Rd.U32(h, 0x1C), fd = Rd.U32(h, 0x20);
        uint flags = Rd.U32(h, 0x24), tracks = Rd.U32(h, 0x28);
        f.Add(new(4, 4, "taille − 8", $"{size}", Confidence.Confirmed));
        f.Add(new(8, 4, "images", $"{frames}", Confidence.Confirmed));
        f.Add(new(0xC, 4, "plus grande image (octets)", $"{largest}", Confidence.Confirmed));
        f.Add(new(0x14, 4, "largeur", $"{w}", Confidence.Confirmed));
        f.Add(new(0x18, 4, "hauteur", $"{ht}", Confidence.Confirmed));
        string fps = fd == 0 ? "invalide" : $"{(double)fn / fd:0.###}";
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

/// <summary>Conteneurs audio reconnus par signature. Le contenu des flux n'est pas décodé.</summary>
public static class AudioInspector
{
    public static InspectionResult Inspect(ReadOnlySpan<byte> h, long fileLength)
    {
        var f = new List<HeaderField>(); var n = new List<string>();
        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            f.Add(new(0, 4, "signature", "RIFF/WAVE", Confidence.Confirmed));
            f.Add(new(4, 4, "taille RIFF", $"{Rd.U32(h, 4)}", Confidence.Confirmed));
            // chunk fmt : cherche dans les octets lus
            int p = 12;
            while (p + 8 <= h.Length)
            {
                uint csz = Rd.U32(h, p + 4);
                if (h.Slice(p, 4).SequenceEqual("fmt "u8) && p + 8 + 16 <= h.Length)
                {
                    f.Add(new(p + 8, 2, "format (1 = PCM)", $"{Rd.U16(h, p + 8)}", Confidence.Confirmed));
                    f.Add(new(p + 10, 2, "canaux", $"{Rd.U16(h, p + 10)}", Confidence.Confirmed));
                    f.Add(new(p + 12, 4, "fréquence (Hz)", $"{Rd.U32(h, p + 12)}", Confidence.Confirmed));
                    f.Add(new(p + 22, 2, "bits par échantillon", $"{Rd.U16(h, p + 22)}", Confidence.Confirmed));
                    break;
                }
                long next = (long)p + 8 + csz + (csz & 1);
                if (next > h.Length) break;
                p = (int)next;
            }
        }
        else if (h.Length >= 4 && h[..4].SequenceEqual("OggS"u8)) f.Add(new(0, 4, "signature", "Ogg", Confidence.Confirmed));
        else if (h.Length >= 4 && h[..4].SequenceEqual("fLaC"u8)) f.Add(new(0, 4, "signature", "FLAC", Confidence.Confirmed));
        else if (h.Length >= 4 && (h[..4].SequenceEqual("ADAT"u8) || h[..4].SequenceEqual("TADA"u8)))
        {
            f.Add(new(0, 4, "signature", $"{(char)h[0]}{(char)h[1]}{(char)h[2]}{(char)h[3]} (AWC)", Confidence.Inferred));
            n.Add("Signature AWC reconnue d'après les formats connus des jeux précédents ; la structure interne pour ce jeu n'est pas documentée ici et n'est pas analysée.");
        }
        else
        {
            f.Add(new(0, Math.Min(4, h.Length), "premiers octets", Convert.ToHexString(h[..Math.Min(4, h.Length)]), Confidence.Unknown));
            n.Add("Conteneur audio non reconnu.");
        }
        f.Add(new(-1, 8, "taille du fichier", $"{fileLength}", Confidence.Confirmed));
        n.Add("Seul l'en-tête est lu : aucun décodage ni lecture audio n'est fourni.");
        return new(f, n);
    }
}

/// <summary>Données structurées : XML analysé réellement ; PSO/RBF reconnus par signature seulement.</summary>
public static class StructuredInspector
{
    public static InspectionResult Inspect(string path, ReadOnlySpan<byte> head, long fileLength)
    {
        var f = new List<HeaderField>(); var n = new List<string>();
        var kind = EntryClassifier.Classify(head);
        f.Add(new(-1, 8, "taille du fichier", $"{fileLength}", Confidence.Confirmed));
        switch (kind)
        {
            case EntryKind.Pso:
                f.Add(new(0, 4, "signature", "PSIN (PSO)", Confidence.Confirmed));
                n.Add("Structure interne PSO non analysée (non documentée ici) : export brut uniquement.");
                break;
            case EntryKind.Rbf:
                f.Add(new(0, 4, "signature", "RBF0", Confidence.Confirmed));
                n.Add("Structure interne RBF non analysée (non documentée ici) : export brut uniquement.");
                break;
            case EntryKind.Xml:
                AnalyzeXml(path, fileLength, f, n);
                break;
            default:
                f.Add(new(0, Math.Min(4, head.Length), "premiers octets", Convert.ToHexString(head[..Math.Min(4, head.Length)]), Confidence.Unknown));
                n.Add("Format structuré non reconnu.");
                break;
        }
        return new(f, n);
    }

    private static void AnalyzeXml(string path, long len, List<HeaderField> f, List<string> n)
    {
        const long Max = 64L << 20;
        if (len > Max) { n.Add("XML > 64 Mo : analyse ignorée."); return; }
        try
        {
            var s = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, CloseInput = true };
            using var r = System.Xml.XmlReader.Create(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), s);
            string? root = null; int elements = 0, attrs = 0, depth = 0, maxDepth = 0;
            while (r.Read())
            {
                if (r.NodeType != System.Xml.XmlNodeType.Element) continue;
                root ??= r.Name; elements++; attrs += r.AttributeCount;
                depth = r.Depth + 1; if (depth > maxDepth) maxDepth = depth;
            }
            f.Add(new(-1, 0, "XML bien formé", "oui", Confidence.Confirmed));
            f.Add(new(-1, 0, "élément racine", root ?? "(aucun)", Confidence.Confirmed));
            f.Add(new(-1, 0, "éléments", $"{elements:N0}", Confidence.Confirmed));
            f.Add(new(-1, 0, "attributs", $"{attrs:N0}", Confidence.Confirmed));
            f.Add(new(-1, 0, "profondeur max", $"{maxDepth}", Confidence.Confirmed));
        }
        catch (System.Xml.XmlException ex) { f.Add(new(-1, 0, "XML bien formé", "non", Confidence.Confirmed)); n.Add("XML invalide : " + ex.Message); }
    }
}

/// <summary>Point d'entrée unique : choisit l'inspecteur selon le type de source. Lecture seule.</summary>
public static class FileInspector
{
    public static InspectionResult? Inspect(Sources.SourceFile file)
    {
        if (file.Type is not (Sources.SourceType.Video or Sources.SourceType.Audio or Sources.SourceType.Structured or Sources.SourceType.Ttpf)) return null;
        if (file.Type == Sources.SourceType.Ttpf)
        {
            try { return TtpfInspector.Inspect(file.Path, file.Size); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(Array.Empty<HeaderField>(), new[] { "Fichier illisible : " + ex.Message }); }
        }
        byte[] head;
        try { head = Rd.Head(file.Path, 4096); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(Array.Empty<HeaderField>(), new[] { "Fichier illisible : " + ex.Message });
        }
        return file.Type switch
        {
            Sources.SourceType.Video => BinkInspector.IsBink(head)
                ? BinkInspector.Inspect(head, file.Size)
                : new(new[] { new HeaderField(0, Math.Min(4, head.Length), "premiers octets", Convert.ToHexString(head.AsSpan(0, Math.Min(4, head.Length))), Confidence.Unknown) },
                      new[] { "Vidéo non Bink : non analysée." }),
            Sources.SourceType.Audio => AudioInspector.Inspect(head, file.Size),
            _ => StructuredInspector.Inspect(file.Path, head, file.Size)
        };
    }
}
