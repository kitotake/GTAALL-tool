using GtaAll.Core.Formats;

namespace GtaAll.Core.Formats;

/// <summary>Données structurées : XML analysé réellement ; PSO/RBF reconnus par signature seulement.</summary>
public static class StructuredInspector
{
    private enum Kind { Unknown, Pso, Rbf, Xml }

    /// <summary>Reconnaissance par signature : PSIN, RBF0, ou XML (BOM UTF-8 toléré puis « &lt; »).</summary>
    private static Kind Sniff(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 4 && h[..4].SequenceEqual("PSIN"u8)) return Kind.Pso;
        if (h.Length >= 4 && h[..4].SequenceEqual("RBF0"u8)) return Kind.Rbf;
        int s = (h.Length >= 3 && h[0] == 0xEF && h[1] == 0xBB && h[2] == 0xBF) ? 3 : 0;
        return h.Length > s && h[s] == (byte)'<' ? Kind.Xml : Kind.Unknown;
    }

    public static InspectionResult Inspect(string path, ReadOnlySpan<byte> head, long fileLength)
    {
        var f = new List<HeaderField>(); var n = new List<string>();
        var kind = Sniff(head);
        f.Add(new(-1, 8, "taille du fichier", $"{fileLength}", Confidence.Confirmed));
        switch (kind)
        {
            case Kind.Pso:
                f.Add(new(0, 4, "signature", "PSIN (PSO)", Confidence.Confirmed));
                n.Add("Structure interne PSO non analysée (non documentée ici) : export brut uniquement.");
                break;
            case Kind.Rbf:
                f.Add(new(0, 4, "signature", "RBF0", Confidence.Confirmed));
                n.Add("Structure interne RBF non analysée (non documentée ici) : export brut uniquement.");
                break;
            case Kind.Xml:
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
            f.Add(new(-1, 0, "éléments", $"{elements}", Confidence.Confirmed));
            f.Add(new(-1, 0, "attributs", $"{attrs}", Confidence.Confirmed));
            f.Add(new(-1, 0, "profondeur max", $"{maxDepth}", Confidence.Confirmed));
        }
        catch (System.Xml.XmlException ex) { f.Add(new(-1, 0, "XML bien formé", "non", Confidence.Confirmed)); n.Add("XML invalide : " + ex.Message); }
    }
}

