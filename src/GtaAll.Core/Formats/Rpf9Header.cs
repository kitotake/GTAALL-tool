using System.Buffers.Binary;

namespace GtaAll.Core.Formats;

/// <summary>
/// En-tête RPF9 (0x40 octets). Disposition et niveaux de certitude d'après
/// GTA6-Asset-Info/formats/01-rpf9.md (GPL-3.0, documentation uniquement). Code original.
/// </summary>
public sealed class Rpf9Header
{
    public const int Size = 0x40;
    public static readonly byte[] Magic = { 0x39, 0x46, 0x50, 0x52 }; // "9FPR" sur disque

    public uint TocEnd { get; private init; }
    public uint NamesSize { get; private init; }
    public uint EntryCountHint { get; private init; }
    public uint KeyId { get; private init; }
    public IReadOnlyList<HeaderField> Fields { get; private init; } = Array.Empty<HeaderField>();
    public IReadOnlyList<string> Warnings { get; private init; } = Array.Empty<string>();

    public static bool HasMagic(ReadOnlySpan<byte> b) => b.Length >= 4 && b[..4].SequenceEqual(Magic);

    public static Rpf9Header Parse(ReadOnlySpan<byte> span, long fileLength)
    {
        var b = span.ToArray();
        if (b.Length < Size || !HasMagic(b.AsSpan())) throw new InvalidDataException("Signature RPF9 absente.");
        uint U32(int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o, 4));
        ushort U16(int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o, 2));
        ulong U64(int o) => BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(o, 8));

        var f = new List<HeaderField>
        {
            new(0x00, 4, "magic", "9FPR (RPF9)", Confidence.Confirmed),
            new(0x04, 4, "toc_end", $"0x{U32(0x04):X}", Confidence.Confirmed),
            new(0x08, 4, "(variable)", $"0x{U32(0x08):X}", Confidence.Unknown),
            new(0x0C, 4, "names_size", $"{U32(0x0C)}", Confidence.Inferred),
            new(0x10, 8, "hash/sel par archive", $"0x{U64(0x10):X16}", Confidence.Inferred),
            new(0x18, 4, "constante", $"0x{U32(0x18):X}", Confidence.Unknown),
            new(0x1C, 4, "constante", $"0x{U32(0x1C):X}", Confidence.Unknown),
            new(0x20, 2, "version/niveau ?", $"{U16(0x20)}", Confidence.Unknown),
            new(0x22, 2, "(souvent 1)", $"{U16(0x22)}", Confidence.Unknown),
            new(0x24, 4, "nombre d'entrées", $"{U32(0x24)}", Confidence.Inferred),
            new(0x28, 4, "key_id", $"0x{U32(0x28):X}", Confidence.Inferred),
            new(0x2C, 4, "drapeaux", $"0x{U32(0x2C):X8}", Confidence.Unknown),
            new(0x30, 4, "constante", $"0x{U32(0x30):X}", Confidence.Unknown),
        };
        var w = new List<string>();
        uint toc = U32(0x04);
        if (toc < Size || toc > fileLength) w.Add($"toc_end 0x{toc:X} hors du fichier : en-tête incohérent.");
        w.Add("La table des matières est chiffrée : aucun nom de fichier ni chemin n'est disponible depuis l'archive.");

        return new Rpf9Header
        {
            TocEnd = toc, NamesSize = U32(0x0C), EntryCountHint = U32(0x24), KeyId = U32(0x28),
            Fields = f, Warnings = w
        };
    }
}
