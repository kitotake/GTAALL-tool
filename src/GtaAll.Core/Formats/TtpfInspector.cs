using System.Buffers.Binary;

namespace GtaAll.Core.Formats;

/// <summary>Une texture décrite par l'index ETPF d'un magasin TTPF (aucun pixel n'est lu).</summary>
public sealed record TtpfTexture(ulong Hash, int Width, int Height, int DxgiFormat, int Kind, int TileMode, int Slices, int MipField)
{
    public string FormatName => TtpfInspector.DxgiName(DxgiFormat);
    public string KindName => Kind switch { 2 => "2D", 4 => "cube", 6 => "3D", 10 => "tableau 2D", _ => $"type {Kind}" };
}

public sealed record TtpfStore(long FileLength, uint VirtualSizeField, IReadOnlyList<TtpfChunk> Chunks,
    IReadOnlyList<TtpfTexture> Textures, uint EtpfVersion, IReadOnlyList<string> Warnings);

public sealed record TtpfChunk(long FileOffset, long VirtualOffset, int Flags, long Padding)
{
    public string Codec => Flags switch
    {
        0x00 => "stocké", 0x20 => "zlib", 0x04 => "BCPack BC1", 0x0C => "BCPack BC3", 0x10 => "BCPack BC4",
        0x14 => "BCPack BC5", 0x18 => "BCPack BC6H", 0x1C => "BCPack BC7", _ => $"inconnu 0x{Flags:X2}"
    };
}

/// <summary>
/// Lecteur d'en-tête et d'index des magasins de textures TTPF (magic "RDS5", index "ETPF").
/// Disposition d'après les notes de SixNG (crates/sixng-core/src/ttpf.rs, GPL-3.0, commit 4a8da27) et de
/// gta6-explorer (MIT) ; code C# original, réécrit à partir de la description du format, pas porté ligne à ligne.
/// Ne décode AUCUN pixel : ni désentrelacement Gfx10, ni BCPack, ni zlib.
/// </summary>
public static class TtpfInspector
{
    public const int MaxChunks = 1 << 22;
    public const int MaxTextures = 1 << 22;

    public static bool HasMagic(ReadOnlySpan<byte> b) => b.Length >= 4 && b[..4].SequenceEqual("RDS5"u8);

    public static string DxgiName(int f) => f switch
    {
        28 => "RGBA8", 29 => "RGBA8 sRGB", 87 => "BGRA8", 91 => "BGRA8 sRGB", 61 => "R8", 49 => "RG8", 54 => "R16F",
        71 => "BC1", 72 => "BC1 sRGB", 74 => "BC2", 75 => "BC2 sRGB", 77 => "BC3", 78 => "BC3 sRGB",
        80 => "BC4", 81 => "BC4 s", 83 => "BC5", 84 => "BC5 s", 95 => "BC6H", 96 => "BC6H s", 98 => "BC7", 99 => "BC7 sRGB",
        _ => $"DXGI {f}"
    };

    public static TtpfStore Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(fs);
    }

    public static TtpfStore Read(Stream s)
    {
        long len = s.Length;
        var warn = new List<string>();
        var h = new byte[24];
        if (s.Read(h, 0, 24) < 24 || !HasMagic(h)) throw new InvalidDataException("Signature RDS5 absente.");
        uint n = U32(h, 4), virt = U32(h, 8);
        if (n == 0 || n > MaxChunks || 0x18L + 8L * n > len) throw new InvalidDataException($"Table de blocs incohérente ({n} entrées).");

        var t = new byte[8 * n];
        s.ReadExactly(t);
        var chunks = new List<TtpfChunk>((int)n);
        for (int i = 0; i < n; i++)
        {
            uint a = U32(t, 8 * i), b = U32(t, 8 * i + 4);
            if (a == 0 && b == 0) break; // terminateur
            long fileOff = (long)(a & 0xFFFFFF) << 8;
            long virtOff = 0x1000L + (b & 0xFFFFF) * 0x4000L + (a >> 30) * 0x1000L;
            chunks.Add(new TtpfChunk(fileOff, virtOff, (int)((a >> 24) & 0x3F), b >> 20));
        }
        if (chunks.Any(c => c.FileOffset > len)) warn.Add("Un bloc pointe au-delà de la fin du fichier : magasin tronqué ?");

        long eoff = (0x10L + 8L * n + 0xFF) & ~0xFFL;
        if (eoff + 16 > len) throw new InvalidDataException("Index ETPF hors du fichier.");
        s.Position = eoff;
        var e = new byte[16];
        s.ReadExactly(e);
        if (!e.AsSpan(0, 4).SequenceEqual("ETPF"u8)) throw new InvalidDataException($"Index ETPF absent à 0x{eoff:X}.");
        uint ver = U32(e, 4), nt = U32(e, 8);
        if (nt > MaxTextures || eoff + 16 + 24L * nt > len) throw new InvalidDataException($"Nombre de textures incohérent ({nt}).");

        var body = new byte[24 * nt];
        s.ReadExactly(body);
        var tex = new List<TtpfTexture>((int)nt);
        for (int i = 0; i < nt; i++)
        {
            uint d0 = U32(body, 16 * i), d1 = U32(body, 16 * i + 4);
            ulong hash = BinaryPrimitives.ReadUInt64LittleEndian(body.AsSpan((int)(16 * nt + 8 * i), 8));
            tex.Add(new TtpfTexture(hash, (int)(d0 & 0x3FFF) + 1, (int)((d0 >> 14) & 0x3FFF) + 1, (int)((d1 >> 3) & 0x7F),
                (int)((d1 >> 20) & 0xF), (int)((d1 >> 24) & 0xF), (int)((d1 >> 10) & 0x3FF) + 1, (int)(d0 >> 28)));
        }
        if (tex.Select(x => x.Hash).Distinct().Count() != tex.Count) warn.Add("Hash de texture en double dans l'index.");
        warn.Add("Le champ de mipmaps du descripteur peut sous-estimer le nombre réel : valeur brute affichée, non corrigée.");
        return new TtpfStore(len, virt, chunks, tex, ver, warn);
    }

    public static InspectionResult Inspect(string path, long fileLength)
    {
        var f = new List<HeaderField>(); var n = new List<string>();
        TtpfStore st;
        try { st = Read(path); }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or IOException)
        { n.Add("TTPF illisible : " + ex.Message); return new(f, n); }

        f.Add(new(0, 4, "signature", "RDS5 (TTPF)", Confidence.Confirmed));
        f.Add(new(4, 4, "blocs (avec terminateur)", $"{st.Chunks.Count + 1}", Confidence.Inferred));
        f.Add(new(8, 4, "espace virtuel (tronqué à 32 bits)", $"0x{st.VirtualSizeField:X}", Confidence.Inferred));
        f.Add(new(-1, 4, "version de l'index ETPF", $"0x{st.EtpfVersion:X}", Confidence.Inferred));
        f.Add(new(-1, 4, "textures", $"{st.Textures.Count:N0}", Confidence.Inferred));
        foreach (var g in st.Chunks.GroupBy(c => c.Codec).OrderByDescending(g => g.Count()).Take(4))
            f.Add(new(-1, 0, $"blocs {g.Key}", $"{g.Count():N0}", Confidence.Inferred));
        foreach (var g in st.Textures.GroupBy(x => x.FormatName).OrderByDescending(g => g.Count()).Take(5))
            f.Add(new(-1, 0, $"textures {g.Key}", $"{g.Count():N0}", Confidence.Inferred));
        foreach (var g in st.Textures.GroupBy(x => x.KindName).OrderByDescending(g => g.Count()).Take(3))
            f.Add(new(-1, 0, $"type {g.Key}", $"{g.Count():N0}", Confidence.Inferred));
        n.AddRange(st.Warnings);
        n.Add("Index lu seulement : aucun pixel n'est décodé (ni BCPack, ni désentrelacement Gfx10). Pas de prévisualisation.");
        return new(f, n);
    }

    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
}
