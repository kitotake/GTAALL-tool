namespace GtaAll.Core.Formats;

public enum EntryKind { Bin, Resource, RageObj, Xml, Pso, Rbf, Dds, Png, Text }

public static class EntryClassifier
{
    /// <summary>Classe d'après les premiers octets décompressés (règle 4 de la doc RPF9).</summary>
    public static EntryKind Classify(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 4)
        {
            if (h[..4].SequenceEqual("PSIN"u8)) return EntryKind.Pso;
            if (h[..4].SequenceEqual("RBF0"u8)) return EntryKind.Rbf;
            if (h[..4].SequenceEqual("DDS "u8)) return EntryKind.Dds;
            if (h[0] == 0x89 && h[1] == 'P' && h[2] == 'N' && h[3] == 'G') return EntryKind.Png;
        }
        // "PRD0" s'écrit "0DRP" sur disque, à +0x10.
        if (h.Length >= 0x14 && h.Slice(0x10, 4).SequenceEqual("0DRP"u8)) return EntryKind.Resource;
        // Pointeur système 0x5xxxxxxx (u64 LE) à +8.
        if (h.Length >= 16)
        {
            ulong p = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(h.Slice(8, 8));
            if (p <= 0xFFFFFFFF && (p & 0xF0000000) == 0x50000000) return EntryKind.RageObj;
        }
        int s = (h.Length >= 3 && h[0] == 0xEF && h[1] == 0xBB && h[2] == 0xBF) ? 3 : 0;
        if (h.Length > s && h[s] == '<') return EntryKind.Xml;
        if (h.Length > 0 && IsPrintable(h)) return EntryKind.Text;
        return EntryKind.Bin;
    }

    private static bool IsPrintable(ReadOnlySpan<byte> h)
    {
        foreach (var b in h) if (!(b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127))) return false;
        return true;
    }

    public static string Extension(EntryKind k) => k switch
    {
        EntryKind.Xml => "xml", EntryKind.Pso => "pso", EntryKind.Rbf => "rbf", EntryKind.Dds => "dds",
        EntryKind.Png => "png", EntryKind.Text => "txt", EntryKind.Resource => "resource.bin",
        EntryKind.RageObj => "rageobj.bin", _ => "bin"
    };
}
