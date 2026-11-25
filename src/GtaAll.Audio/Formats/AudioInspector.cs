using GtaAll.Core.Formats;

namespace GtaAll.Core.Formats;

/// <summary>Conteneurs audio reconnus par signature. Le contenu des flux n'est pas décodé.</summary>
public static class AudioInspector
{
    public static InspectionResult Inspect(ReadOnlySpan<byte> h, long fileLength)
    {
        var f = new List<HeaderField>(); var n = new List<string>();
        if (h.Length >= 12 && h[..4].SequenceEqual("RIFF"u8) && h.Slice(8, 4).SequenceEqual("WAVE"u8))
        {
            f.Add(new(0, 4, "signature", "RIFF/WAVE", Confidence.Confirmed));
            f.Add(new(4, 4, "taille RIFF", $"{ByteReader.U32(h, 4)}", Confidence.Confirmed));
            // chunk fmt : cherche dans les octets lus
            int p = 12;
            while (p + 8 <= h.Length)
            {
                uint csz = ByteReader.U32(h, p + 4);
                if (h.Slice(p, 4).SequenceEqual("fmt "u8) && p + 8 + 16 <= h.Length)
                {
                    f.Add(new(p + 8, 2, "format (1 = PCM)", $"{ByteReader.U16(h, p + 8)}", Confidence.Confirmed));
                    f.Add(new(p + 10, 2, "canaux", $"{ByteReader.U16(h, p + 10)}", Confidence.Confirmed));
                    f.Add(new(p + 12, 4, "fréquence (Hz)", $"{ByteReader.U32(h, p + 12)}", Confidence.Confirmed));
                    f.Add(new(p + 22, 2, "bits par échantillon", $"{ByteReader.U16(h, p + 22)}", Confidence.Confirmed));
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

