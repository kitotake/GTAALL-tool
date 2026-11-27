using System.Buffers.Binary;
using System.IO.Compression;

namespace GtaAll.Textures.Decoding;

/// <summary>Écriture de PNG (RGBA 8 bits) et de DDS (en-tête DX10) sans dépendance externe. Ne modifie jamais une source.</summary>
public static class ImageFiles
{
    private static readonly uint[] Crc = BuildCrc();
    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++) { uint c = n; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1; t[n] = c; }
        return t;
    }
    private static uint Crc32(uint seed, ReadOnlySpan<byte> d) { uint c = seed; foreach (var b in d) c = Crc[(c ^ b) & 0xFF] ^ (c >> 8); return c; }

    private static void Chunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(len, (uint)data.Length); s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type); s.Write(t); s.Write(data);
        uint crc = Crc32(0xFFFFFFFF, t); crc = Crc32(crc, data) ^ 0xFFFFFFFF;
        Span<byte> c = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(c, crc); s.Write(c);
    }

    public static byte[] PngBytes(ReadOnlySpan<byte> rgba, int w, int h)
    {
        if (rgba.Length != w * h * 4) throw new ArgumentException("taille RGBA incohérente");
        using var o = new MemoryStream();
        o.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)w); BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)h);
        ihdr[8] = 8; ihdr[9] = 6; // 8 bits, RGBA
        Chunk(o, "IHDR", ihdr);
        var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Fastest, true))
            for (int y = 0; y < h; y++) { z.WriteByte(0); z.Write(rgba.Slice(y * w * 4, w * 4)); }
        Chunk(o, "IDAT", raw.ToArray());
        Chunk(o, "IEND", ReadOnlySpan<byte>.Empty);
        return o.ToArray();
    }

    /// <summary>DDS à une seule mip, en-tête DX10 (le format DXGI d'origine est conservé).</summary>
    public static byte[] DdsBytes(int w, int h, int dxgi, ReadOnlySpan<byte> elements)
    {
        var hd = new byte[4 + 124 + 20];
        "DDS "u8.CopyTo(hd);
        void U(int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(hd.AsSpan(o), v);
        U(4, 124); U(8, 0x1007 | 0x80000);            // CAPS|HEIGHT|WIDTH|PIXELFORMAT|LINEARSIZE
        U(12, (uint)h); U(16, (uint)w); U(20, (uint)elements.Length);
        U(28, 1);                                      // mips
        U(76, 32); U(80, 4); "DX10"u8.CopyTo(hd.AsSpan(84)); // pixelformat : FOURCC DX10
        U(108, 0x1000);                                // caps : texture
        U(128, (uint)dxgi); U(132, 3); U(140, 1);      // dxgiFormat, dimension 2D, arraySize 1
        var o = new byte[hd.Length + elements.Length];
        hd.CopyTo(o, 0); elements.CopyTo(o.AsSpan(hd.Length));
        return o;
    }

    /// <summary>Copie des pixels RGBA en BGRA (ordre attendu par WPF, PixelFormats.Bgra32). <paramref name="opaque"/> force l'alpha à 255.</summary>
    public static byte[] RgbaToBgra(byte[] rgba, bool opaque = false)
    {
        var o = new byte[rgba.Length];
        for (int i = 0; i + 3 < rgba.Length; i += 4)
        {
            o[i] = rgba[i + 2]; o[i + 1] = rgba[i + 1]; o[i + 2] = rgba[i];
            o[i + 3] = opaque ? (byte)255 : rgba[i + 3];
        }
        return o;
    }

    /// <summary>Réduction par moyenne de blocs (aperçu). Conserve le rapport d'aspect, côté max = <paramref name="max"/>.</summary>
    public static (byte[] rgba, int w, int h) Thumbnail(byte[] rgba, int w, int h, int max)
    {
        if (w <= max && h <= max) return (rgba, w, h);
        double s = (double)max / Math.Max(w, h);
        int nw = Math.Max(1, (int)(w * s)), nh = Math.Max(1, (int)(h * s));
        var o = new byte[nw * nh * 4];
        for (int y = 0; y < nh; y++)
            for (int x = 0; x < nw; x++)
            {
                int x0 = x * w / nw, x1 = Math.Max(x0 + 1, (x + 1) * w / nw), y0 = y * h / nh, y1 = Math.Max(y0 + 1, (y + 1) * h / nh);
                long r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { int i = (yy * w + xx) * 4; r += rgba[i]; g += rgba[i + 1]; b += rgba[i + 2]; a += rgba[i + 3]; n++; }
                int d = (y * nw + x) * 4; o[d] = (byte)(r / n); o[d + 1] = (byte)(g / n); o[d + 2] = (byte)(b / n); o[d + 3] = (byte)(a / n);
            }
        return (o, nw, nh);
    }
}
