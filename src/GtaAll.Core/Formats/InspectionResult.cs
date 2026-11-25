namespace GtaAll.Core.Formats;

/// <summary>Résultat d'une inspection en lecture seule : champs d'en-tête + notes.</summary>
public sealed record InspectionResult(IReadOnlyList<HeaderField> Fields, IReadOnlyList<string> Notes);

public static class ByteReader
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

