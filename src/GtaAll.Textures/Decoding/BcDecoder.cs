namespace GtaAll.Textures.Decoding;

/// <summary>
/// Décodeurs de blocs BC1, BC3, BC4, BC5 et BC7 vers RGBA8 (R, G, B, A), écrits d'après la spécification publique
/// des formats Direct3D. Validés par comparaison avec un décodeur de référence indépendant (voir tests).
/// BC6H et BC2 ne sont pas gérés.
/// </summary>
public static class BcDecoder
{
    public enum Format { Bc1, Bc3, Bc4, Bc5, Bc7 }

    public static int BytesPerBlock(Format f) => f is Format.Bc1 or Format.Bc4 ? 8 : 16;

    /// <summary>Décode une image entière (blocs en ordre ligne par ligne). Retourne width*height*4 octets RGBA.</summary>
    public static byte[] Decode(Format f, ReadOnlySpan<byte> data, int width, int height)
    {
        int bw = (width + 3) / 4, bh = (height + 3) / 4, bs = BytesPerBlock(f);
        var img = new byte[width * height * 4];
        Span<byte> blk = stackalloc byte[64];
        for (int by = 0; by < bh; by++)
            for (int bx = 0; bx < bw; bx++)
            {
                int o = (by * bw + bx) * bs;
                blk.Clear();
                if (o + bs <= data.Length) DecodeBlock(f, data.Slice(o, bs), blk);
                for (int y = 0; y < 4 && by * 4 + y < height; y++)
                    for (int x = 0; x < 4 && bx * 4 + x < width; x++)
                        blk.Slice((y * 4 + x) * 4, 4).CopyTo(img.AsSpan(((by * 4 + y) * width + bx * 4 + x) * 4, 4));
            }
        return img;
    }

    /// <summary>Décode un bloc 4x4 vers 16 pixels RGBA (64 octets).</summary>
    public static void DecodeBlock(Format f, ReadOnlySpan<byte> b, Span<byte> rgba)
    {
        switch (f)
        {
            case Format.Bc1: Bc1(b, rgba, false); break;
            case Format.Bc3: Bc1(b.Slice(8, 8), rgba, true); Bc4Channel(b[..8], rgba, 3); break;
            case Format.Bc4:
                { Span<byte> v = stackalloc byte[16]; Bc4Values(b, v); for (int i = 0; i < 16; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = v[i]; rgba[i * 4 + 3] = 255; } break; }
            case Format.Bc5:
                {
                    Span<byte> r = stackalloc byte[16], g = stackalloc byte[16]; Bc4Values(b[..8], r); Bc4Values(b.Slice(8, 8), g);
                    for (int i = 0; i < 16; i++)
                    {
                        // normale : Z reconstruit (aperçu) ; les canaux R et G sont les données brutes
                        double nx = r[i] / 127.5 - 1, ny = g[i] / 127.5 - 1, z2 = 1 - nx * nx - ny * ny;
                        rgba[i * 4] = r[i]; rgba[i * 4 + 1] = g[i]; rgba[i * 4 + 2] = (byte)Math.Clamp((Math.Sqrt(Math.Max(0, z2)) + 1) * 127.5 + 0.5, 0, 255); rgba[i * 4 + 3] = 255;
                    }
                    break;
                }
            default: Bc7(b, rgba); break;
        }
    }

    // ---------------- BC1 / BC3 couleur ----------------
    private static void Bc1(ReadOnlySpan<byte> b, Span<byte> rgba, bool noAlpha)
    {
        int c0 = b[0] | b[1] << 8, c1 = b[2] | b[3] << 8;
        Span<int> pal = stackalloc int[16];
        Rgb565(c0, pal, 0); Rgb565(c1, pal, 4);
        bool four = noAlpha || c0 > c1;
        for (int k = 0; k < 3; k++)
        {
            int a = pal[k], bb = pal[4 + k];
            pal[8 + k] = four ? (2 * a + bb) / 3 : (a + bb) / 2;
            pal[12 + k] = four ? (a + 2 * bb) / 3 : 0;
        }
        uint idx = (uint)(b[4] | b[5] << 8 | b[6] << 16 | b[7] << 24);
        for (int i = 0; i < 16; i++)
        {
            int s = (int)((idx >> (2 * i)) & 3);
            rgba[i * 4] = (byte)pal[s * 4]; rgba[i * 4 + 1] = (byte)pal[s * 4 + 1]; rgba[i * 4 + 2] = (byte)pal[s * 4 + 2];
            rgba[i * 4 + 3] = (byte)(!four && s == 3 ? 0 : 255);
        }
    }

    private static void Rgb565(int c, Span<int> o, int at)
    {
        int r = (c >> 11) & 31, g = (c >> 5) & 63, bl = c & 31;
        o[at] = r << 3 | r >> 2; o[at + 1] = g << 2 | g >> 4; o[at + 2] = bl << 3 | bl >> 2;
    }

    // ---------------- BC4 (un canal) ----------------
    private static void Bc4Values(ReadOnlySpan<byte> b, Span<byte> v)
    {
        int a0 = b[0], a1 = b[1];
        Span<int> pal = stackalloc int[8];
        pal[0] = a0; pal[1] = a1;
        if (a0 > a1) for (int i = 1; i <= 6; i++) pal[1 + i] = ((7 - i) * a0 + i * a1) / 7;
        else { for (int i = 1; i <= 4; i++) pal[1 + i] = ((5 - i) * a0 + i * a1) / 5; pal[6] = 0; pal[7] = 255; }
        ulong bits = 0; for (int i = 0; i < 6; i++) bits |= (ulong)b[2 + i] << (8 * i);
        for (int i = 0; i < 16; i++) v[i] = (byte)pal[(int)((bits >> (3 * i)) & 7)];
    }

    private static void Bc4Channel(ReadOnlySpan<byte> b, Span<byte> rgba, int ch)
    {
        Span<byte> v = stackalloc byte[16]; Bc4Values(b, v);
        for (int i = 0; i < 16; i++) rgba[i * 4 + ch] = v[i];
    }

    // ---------------- BC7 ----------------
    private readonly record struct Mode(int Ns, int Pb, int Rb, int Isb, int Cb, int Ab, int Epb, int Spb, int Ib, int Ib2);
    private static readonly Mode[] Modes =
    {
        new(3, 4, 0, 0, 4, 0, 1, 0, 3, 0), new(2, 6, 0, 0, 6, 0, 0, 1, 3, 0), new(3, 6, 0, 0, 5, 0, 0, 0, 2, 0), new(2, 6, 0, 0, 7, 0, 1, 0, 2, 0),
        new(1, 0, 2, 1, 5, 6, 0, 0, 2, 3), new(1, 0, 2, 0, 7, 8, 0, 0, 2, 2), new(1, 0, 0, 0, 7, 7, 1, 0, 4, 0), new(2, 6, 0, 0, 5, 5, 1, 0, 2, 0)
    };
    private static readonly byte[] Anchor2 = { 15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15,15, 2, 8, 2, 2, 8, 8,15, 2, 8, 2, 2, 8, 8, 2, 2,15,15, 6, 8, 2, 8,15,15, 2, 8, 2, 2, 2,15,15, 6, 6, 2, 6, 8,15,15, 2, 2,15,15,15,15,15, 2, 2,15 };
    private static readonly byte[] Anchor3a = { 3, 3,15,15, 8, 3,15,15, 8, 8, 6, 6, 6, 5, 3, 3, 3, 3, 8,15, 3, 3, 6,10, 5, 8, 8, 6, 8, 5,15,15, 8,15, 3, 5, 6,10, 8,15,15, 3,15, 5,15,15,15,15, 3,15, 5, 5, 5, 8, 5,10, 5,10, 8,13,15,12, 3, 3 };
    private static readonly byte[] Anchor3b = { 15, 8, 8, 3,15,15, 3, 8,15,15,15,15,15,15,15, 8,15, 8,15, 3,15, 8,15, 8, 3,15, 6,10,15,15,10, 8,15, 3,15,10,10, 8, 9,10, 6,15, 8,15, 3, 6, 6, 8,15, 3,15,15,15,15,15,15,15,15,15,15, 3,15,15, 8 };
    private static readonly byte[] W2 = { 0, 21, 43, 64 };
    private static readonly byte[] W3 = { 0, 9, 18, 27, 37, 46, 55, 64 };
    private static readonly byte[] W4 = { 0, 4, 9, 13, 17, 21, 26, 30, 34, 38, 43, 47, 51, 55, 60, 64 };
    // Tables de partition BC7 (2 bits par pixel, pixel 0 = bits 0-1) : 2 sous-ensembles puis 3 sous-ensembles.
    // Obtenues en sondant un décodeur de référence indépendant (texture2ddecoder) sur des blocs construits.
    private static readonly uint[] Part2 = { 0x50505050,0x40404040,0x54545454,0x54505040,0x50404000,0x55545450,0x55545040,0x54504000,0x50400000,0x55555450,0x55544000,0x54400000,0x55555440,0x55550000,0x55555500,0x55000000,0x55150100,0x00004054,0x15010000,0x00405054,0x00004050,0x15050100,0x05010000,0x40505054,0x00404050,0x05010100,0x14141414,0x05141450,0x01155440,0x00555500,0x15014054,0x05414150,0x44444444,0x55005500,0x11441144,0x05055050,0x05500550,0x11114444,0x41144114,0x44111144,0x15055054,0x01055040,0x05041050,0x05455150,0x14414114,0x50050550,0x41411414,0x00141400,0x00041504,0x00105410,0x10541000,0x04150400,0x50410514,0x41051450,0x05415014,0x14054150,0x41050514,0x41505014,0x40011554,0x54150140,0x50505500,0x00555050,0x15151010,0x54540404 };
    private static readonly uint[] Part3 = { 0xAA685050,0x6A5A5040,0x5A5A4200,0x5450A0A8,0xA5A50000,0xA0A05050,0x5555A0A0,0x5A5A5050,0xAA550000,0xAA555500,0xAAAA5500,0x90909090,0x94949494,0xA4A4A4A4,0xA9A59450,0x2A0A4250,0xA5945040,0x0A425054,0xA5A5A500,0x55A0A0A0,0xA8A85454,0x6A6A4040,0xA4A45000,0x1A1A0500,0x0050A4A4,0xAAA59090,0x14696914,0x69691400,0xA08585A0,0xAA821414,0x50A4A450,0x6A5A0200,0xA9A58000,0x5090A0A8,0xA8A09050,0x24242424,0x00AA5500,0x24924924,0x24499224,0x50A50A50,0x500AA550,0xAAAA4444,0x66660000,0xA5A0A5A0,0x50A050A0,0x69286928,0x44AAAA44,0x66666600,0xAA444444,0x54A854A8,0x95809580,0x96969600,0xA85454A8,0x80959580,0xAA141414,0x96960000,0xAAAA1414,0xA05050A0,0xA0A5A5A0,0x96000000,0x40804080,0xA9A8A9A8,0xAAAAAA44,0x2A4A5254 };

    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _d; public int Pos;
        public BitReader(ReadOnlySpan<byte> d) { _d = d; Pos = 0; }
        public int Read(int n)
        {
            int v = 0;
            for (int i = 0; i < n; i++, Pos++) v |= ((_d[Pos >> 3] >> (Pos & 7)) & 1) << i;
            return v;
        }
    }

    private static void Bc7(ReadOnlySpan<byte> blk, Span<byte> rgba)
    {
        var br = new BitReader(blk);
        int mode = 0; while (mode < 8 && br.Read(1) == 0) mode++;
        if (mode >= 8) { rgba[..64].Clear(); return; } // bloc réservé : transparent noir, comme D3D
        var m = Modes[mode];
        int part = br.Read(m.Pb), rot = br.Read(m.Rb), isel = br.Read(m.Isb);
        int ne = m.Ns * 2;
        Span<int> ep = stackalloc int[6 * 4];
        for (int c = 0; c < 3; c++) for (int e = 0; e < ne; e++) ep[e * 4 + c] = br.Read(m.Cb);
        if (m.Ab > 0) for (int e = 0; e < ne; e++) ep[e * 4 + 3] = br.Read(m.Ab);
        Span<int> pb = stackalloc int[6];
        if (m.Epb == 1) for (int e = 0; e < ne; e++) pb[e] = br.Read(1);
        else if (m.Spb == 1) for (int s = 0; s < m.Ns; s++) { int p = br.Read(1); pb[s * 2] = p; pb[s * 2 + 1] = p; }
        bool hasP = m.Epb == 1 || m.Spb == 1;
        int cbits = m.Cb + (hasP ? 1 : 0), abits = m.Ab + (hasP && m.Ab > 0 ? 1 : 0);
        for (int e = 0; e < ne; e++)
        {
            for (int c = 0; c < 3; c++) { int v = hasP ? (ep[e * 4 + c] << 1) | pb[e] : ep[e * 4 + c]; ep[e * 4 + c] = Expand(v, cbits); }
            if (m.Ab > 0) { int v = hasP ? (ep[e * 4 + 3] << 1) | pb[e] : ep[e * 4 + 3]; ep[e * 4 + 3] = Expand(v, abits); }
            else ep[e * 4 + 3] = 255;
        }
        // sous-ensemble de chaque pixel et pixels « ancres »
        Span<int> subset = stackalloc int[16];
        int a1 = 15, a2 = 15;
        for (int i = 0; i < 16; i++)
            subset[i] = m.Ns == 1 ? 0 : m.Ns == 2 ? (int)((Part2[part] >> (2 * i)) & 3) : (int)((Part3[part] >> (2 * i)) & 3);
        if (m.Ns == 2) a1 = Anchor2[part]; else if (m.Ns == 3) { a1 = Anchor3a[part]; a2 = Anchor3b[part]; }
        Span<int> idx = stackalloc int[16], idx2 = stackalloc int[16];
        for (int i = 0; i < 16; i++)
        {
            bool anchor = i == 0 || (m.Ns >= 2 && i == a1 && subset[i] == 1) || (m.Ns == 3 && i == a2 && subset[i] == 2);
            idx[i] = br.Read(anchor ? m.Ib - 1 : m.Ib);
        }
        if (m.Ib2 > 0) for (int i = 0; i < 16; i++) idx2[i] = br.Read(i == 0 ? m.Ib2 - 1 : m.Ib2);
        for (int i = 0; i < 16; i++)
        {
            int s = subset[i], e0 = s * 2, e1 = s * 2 + 1;
            int ic = idx[i], ia = idx[i], bc = m.Ib, ba = m.Ib;
            if (m.Ib2 > 0)
            {
                if (isel == 0) { ia = idx2[i]; ba = m.Ib2; } else { ic = idx2[i]; bc = m.Ib2; ia = idx[i]; ba = m.Ib; }
            }
            int wc = Weight(bc, ic), wa = Weight(ba, ia);
            int r = Lerp(ep[e0 * 4], ep[e1 * 4], wc), g = Lerp(ep[e0 * 4 + 1], ep[e1 * 4 + 1], wc), bl = Lerp(ep[e0 * 4 + 2], ep[e1 * 4 + 2], wc);
            int a = m.Ab > 0 ? Lerp(ep[e0 * 4 + 3], ep[e1 * 4 + 3], wa) : 255;
            switch (rot) { case 1: (a, r) = (r, a); break; case 2: (a, g) = (g, a); break; case 3: (a, bl) = (bl, a); break; }
            rgba[i * 4] = (byte)r; rgba[i * 4 + 1] = (byte)g; rgba[i * 4 + 2] = (byte)bl; rgba[i * 4 + 3] = (byte)a;
        }
    }

    private static int Expand(int v, int bits) => bits >= 8 ? v : (v << (8 - bits)) | (v >> (2 * bits - 8));
    private static int Weight(int bits, int i) => bits == 2 ? W2[i] : bits == 3 ? W3[i] : W4[i];
    private static int Lerp(int a, int b, int w) => (a * (64 - w) + b * w + 32) >> 6;
}
