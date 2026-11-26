using System.Numerics;

namespace GtaAll.Textures.Decoding;

/// <summary>
/// Disposition mémoire AMD Gfx10 (RDNA2 / Xbox Series) d'une texture 2D à une tranche : modes SW_4KB_S (tile 1-3) et
/// SW_64KB_D (tile 0), sans XOR de pipe/bank. Mipmaps stockées de la plus petite à la plus grande (mip 0 en dernier),
/// petites mips regroupées dans un bloc « mip tail ».
/// Réécriture C# d'après la description de SixNG (gfx10.rs, GPL-3.0, mesurée sur les magasins) et d'AMD addrlib (MIT).
/// Les équations de bits sont des données de format ; la correction est vérifiée sur le fichier réel (voir tests/rapport).
/// </summary>
public sealed class Gfx10Layout
{
    private const byte X = 0, Y = 1;

    private static (byte axis, byte bit)[]? Equation(bool d64k, int bpe) => (d64k, bpe) switch
    {
        (false, 16) => new (byte, byte)[] { (Y, 0), (Y, 1), (X, 0), (X, 1), (Y, 2), (X, 2), (Y, 3), (X, 3) },
        (false, 8) => new (byte, byte)[] { (X, 0), (Y, 0), (Y, 1), (X, 1), (X, 2), (Y, 2), (X, 3), (Y, 3), (X, 4) },
        (true, 16) => new (byte, byte)[] { (X, 0), (Y, 0), (X, 1), (Y, 1), (Y, 2), (X, 2), (Y, 3), (X, 3), (Y, 4), (X, 4), (Y, 5), (X, 5) },
        (true, 8) => new (byte, byte)[] { (X, 0), (Y, 0), (X, 1), (X, 2), (Y, 1), (Y, 2), (X, 3), (Y, 3), (X, 4), (Y, 4), (X, 5), (Y, 5), (X, 6) },
        (false, 4) => new (byte, byte)[] { (X, 0), (X, 1), (Y, 0), (Y, 1), (Y, 2), (X, 2), (Y, 3), (X, 3), (Y, 4), (X, 4) },
        (true, 4) => new (byte, byte)[] { (X, 0), (X, 1), (Y, 0), (Y, 1), (X, 2), (Y, 2), (Y, 3), (X, 3), (Y, 4), (X, 4), (Y, 5), (X, 5), (Y, 6), (X, 6) },
        (false, 2) => new (byte, byte)[] { (X, 0), (X, 1), (X, 2), (Y, 0), (Y, 1), (Y, 2), (X, 3), (Y, 3), (X, 4), (Y, 4), (X, 5) },
        (true, 2) => new (byte, byte)[] { (X, 0), (X, 1), (X, 2), (Y, 0), (Y, 1), (Y, 2), (X, 3), (Y, 3), (X, 4), (Y, 4), (X, 5), (Y, 5), (X, 6), (Y, 6), (X, 7) },
        (false, 1) => new (byte, byte)[] { (X, 0), (X, 1), (X, 2), (X, 3), (Y, 0), (Y, 1), (Y, 2), (Y, 3), (Y, 4), (X, 4), (Y, 5), (X, 5) },
        (true, 1) => new (byte, byte)[] { (X, 0), (X, 1), (X, 2), (Y, 1), (Y, 0), (Y, 2), (X, 3), (Y, 3), (Y, 4), (X, 4), (Y, 5), (X, 5), (Y, 6), (X, 6), (Y, 7), (X, 7) },
        _ => null
    };

    public sealed record MipPlace(int W, int H, long Offset, (int x, int y)? Tail);

    private readonly (byte axis, byte bit)[] _eq;
    public int Bpe { get; }
    public int BlockW { get; }
    public int BlockH { get; }
    public long BlockBytes { get; }
    public IReadOnlyList<MipPlace> Mips { get; }
    /// <summary>Octets d'une tranche (toute la chaîne de mips).</summary>
    public long SliceBytes { get; }

    private Gfx10Layout((byte, byte)[] eq, int bpe, int bw, int bh, long bs, MipPlace[] mips, long sliceBytes)
    { _eq = eq; Bpe = bpe; BlockW = bw; BlockH = bh; BlockBytes = bs; Mips = mips; SliceBytes = sliceBytes; }

    /// <summary>Octets pour <paramref name="slices"/> tranches (tableaux : niveau par niveau, toutes tranches ensemble).</summary>
    public long TotalFor(int slices) => SliceBytes * Math.Max(1, slices);

    /// <param name="w">largeur en éléments (blocs BCn)</param><param name="h">hauteur en éléments</param>
    public static Gfx10Layout? Create(bool d64k, int w, int h, int mips, int bpe)
    {
        var eq = Equation(d64k, bpe);
        if (eq == null || w < 1 || h < 1) return null;
        int nx = eq.Count(e => e.axis == X);
        int bw = 1 << nx, bh = 1 << (eq.Length - nx);
        long bs = (long)bw * bh * bpe;
        int log2 = BitOperations.Log2((ulong)bs);
        int maxTail = log2 <= 11 ? 1 + (1 << (log2 - 9)) : log2 - 4;
        int tw = bw / 2, th = bh;
        int nm = Math.Max(1, mips);
        var dims = Enumerable.Range(0, nm).Select(i => (w: Math.Max(1, w >> i), h: Math.Max(1, h >> i))).ToArray();
        // le mip tail n'existe que pour plus d'une mip ; une mip unique commence à l'offset 0
        int first = nm;
        if (nm == 1) first = 1;
        else for (int i = 0; i < nm; i++) if (dims[i].w <= tw && dims[i].h <= th && nm - i <= maxTail) { first = i; break; }
        var place = dims.Select(d => new MipPlace(d.w, d.h, 0, null)).ToArray();
        long off = first < nm ? bs : 0;
        for (int i = first - 1; i >= 0; i--)
        {
            place[i] = place[i] with { Offset = off };
            off += (long)CeilDiv(dims[i].w, bw) * bw * CeilDiv(dims[i].h, bh) * bh * bpe;
        }
        var (b256w, b256h) = bpe switch { 16 => (4, 4), 8 => (8, 4), 4 => (8, 8), 2 => (16, 8), _ => (16, 16) };
        for (int k = first; k < nm; k++)
        {
            int m = maxTail - 1 - (k - first);
            int mo = m > 6 ? 16 << m : m << 8;
            int mx = 0, my = 0;
            for (int b = 0; b < 6; b++) { mx |= ((mo >> (9 + 2 * b)) & 1) << b; my |= ((mo >> (8 + 2 * b)) & 1) << b; }
            place[k] = place[k] with { Offset = 0, Tail = (mx * b256w, my * b256h) };
        }
        return new Gfx10Layout(eq, bpe, bw, bh, bs, place, off);
    }

    private static int CeilDiv(int a, int b) => (a + b - 1) / b;

    private long InBlock(int x, int y)
    {
        long a = 0;
        for (int k = 0; k < _eq.Length; k++)
        {
            int v = _eq[k].axis == X ? x : y;
            a |= (long)((v >> _eq[k].bit) & 1) << k;
        }
        return a;
    }

    /// <summary>Octets de la mip <paramref name="m"/> pour une tranche (le bloc entier pour les mips du tail).</summary>
    public long LevelBytes(int m)
    {
        var p = Mips[m];
        return p.Tail != null ? BlockBytes : (long)CeilDiv(p.W, BlockW) * BlockW * CeilDiv(p.H, BlockH) * BlockH * Bpe;
    }

    /// <summary>Plage [début, fin) que la mip occupe dans la texture (tranche 0 ; une seule tranche supposée).</summary>
    public (long start, long end) MipSpan(int m)
    {
        long a = Mips[m].Tail != null ? 0 : Mips[m].Offset;
        return (a, a + LevelBytes(m));
    }

    /// <summary>Adresse en octets de l'élément (x, y) de la mip m, tranche 0.</summary>
    public long Addr(int m, int x, int y)
    {
        var p = Mips[m];
        if (p.Tail is { } t) return InBlock(x + t.x, y + t.y) * Bpe;
        long pitch = CeilDiv(p.W, BlockW);
        long blk = (long)(y / BlockH) * pitch + x / BlockW;
        return p.Offset + blk * BlockBytes + InBlock(x % BlockW, y % BlockH) * Bpe;
    }

    /// <summary>Éléments en ordre ligne par ligne de la mip m. <paramref name="src"/> commence à <paramref name="srcStart"/>
    /// (adresse dans la texture). Les octets absents restent à zéro.</summary>
    public byte[] Untile(ReadOnlySpan<byte> src, long srcStart, int m)
    {
        var p = Mips[m];
        var o = new byte[(long)p.W * p.H * Bpe];
        for (int y = 0; y < p.H; y++)
            for (int x = 0; x < p.W; x++)
            {
                long s = Addr(m, x, y) - srcStart;
                if (s >= 0 && s + Bpe <= src.Length)
                    src.Slice((int)s, Bpe).CopyTo(o.AsSpan((int)(((long)y * p.W + x) * Bpe), Bpe));
            }
        return o;
    }
}
