namespace GtaAll.Core.Formats;

/// <summary>
/// Décompresseur zlib/deflate écrit pour GTAALL-tool (algorithme classique "puff").
/// Contrairement à ZLibStream, il indique combien d'octets le flux occupe, ce qui est
/// nécessaire pour enchaîner les flux zlib d'une archive RPF9.
/// </summary>
public static class ZlibInflater
{
    /// <summary>Tente de décompresser un flux zlib commençant à <paramref name="start"/>.</summary>
    public static bool TryInflate(byte[] buf, int start, int end, int maxOut,
        out byte[] output, out int outLength, out int consumed)
    {
        output = Array.Empty<byte>(); outLength = 0; consumed = 0;
        if (end - start < 6) return false;
        int cmf = buf[start], flg = buf[start + 1];
        if ((cmf & 0x0F) != 8 || ((cmf << 8) | flg) % 31 != 0 || (flg & 0x20) != 0) return false;
        try
        {
            var st = new State(buf, start + 2, end, maxOut);
            st.Run();
            int p = st.Pos;
            if (p + 4 > end) return false;
            uint expected = (uint)(buf[p] << 24 | buf[p + 1] << 16 | buf[p + 2] << 8 | buf[p + 3]);
            if (Adler32(st.Out, st.OutLen) != expected) return false;
            output = st.Out; outLength = st.OutLen; consumed = p + 4 - start;
            return true;
        }
        catch (InflateException) { return false; }
    }

    private static uint Adler32(byte[] d, int n)
    {
        uint a = 1, b = 0;
        int i = 0;
        while (i < n)
        {
            int chunk = Math.Min(5552, n - i);
            for (int k = 0; k < chunk; k++) { a += d[i + k]; b += a; }
            a %= 65521; b %= 65521; i += chunk;
        }
        return (b << 16) | a;
    }

    private sealed class InflateException : Exception { }

    private sealed class Huff
    {
        public readonly short[] Count = new short[16];
        public readonly short[] Symbol;
        public Huff(int n) { Symbol = new short[n]; }

        public int Build(short[] lengths, int off, int n)
        {
            Array.Clear(Count);
            for (int i = 0; i < n; i++) Count[lengths[off + i]]++;
            if (Count[0] == n) return 0;
            int left = 1;
            for (int len = 1; len < 16; len++)
            {
                left <<= 1; left -= Count[len];
                if (left < 0) return left;
            }
            var offs = new short[16];
            for (int len = 1; len < 15; len++) offs[len + 1] = (short)(offs[len] + Count[len]);
            for (int s = 0; s < n; s++)
                if (lengths[off + s] != 0) Symbol[offs[lengths[off + s]]++] = (short)s;
            return left;
        }
    }

    private static readonly short[] LBase = { 3,4,5,6,7,8,9,10,11,13,15,17,19,23,27,31,35,43,51,59,67,83,99,115,131,163,195,227,258 };
    private static readonly short[] LExt = { 0,0,0,0,0,0,0,0,1,1,1,1,2,2,2,2,3,3,3,3,4,4,4,4,5,5,5,5,0 };
    private static readonly short[] DBase = { 1,2,3,4,5,7,9,13,17,25,33,49,65,97,129,193,257,385,513,769,1025,1537,2049,3073,4097,6145,8193,12289,16385,24577 };
    private static readonly short[] DExt = { 0,0,0,0,1,1,2,2,3,3,4,4,5,5,6,6,7,7,8,8,9,9,10,10,11,11,12,12,13,13 };
    private static readonly byte[] Order = { 16,17,18,0,8,7,9,6,10,5,11,4,12,3,13,2,14,1,15 };

    private static readonly Huff FixedLen = new(288), FixedDist = new(30);
    static ZlibInflater()
    {
        var l = new short[288];
        for (int i = 0; i < 144; i++) l[i] = 8;
        for (int i = 144; i < 256; i++) l[i] = 9;
        for (int i = 256; i < 280; i++) l[i] = 7;
        for (int i = 280; i < 288; i++) l[i] = 8;
        FixedLen.Build(l, 0, 288);
        var d = new short[30];
        Array.Fill(d, (short)5);
        FixedDist.Build(d, 0, 30);
    }

    private sealed class State
    {
        private readonly byte[] _s; private readonly int _end; private readonly int _max;
        public int Pos; private uint _bitBuf; private int _bitCnt;
        public byte[] Out = new byte[1 << 16]; public int OutLen;

        public State(byte[] s, int pos, int end, int max) { _s = s; Pos = pos; _end = end; _max = max; }

        private int Bits(int n)
        {
            while (_bitCnt < n)
            {
                if (Pos >= _end) throw new InflateException();
                _bitBuf |= (uint)_s[Pos++] << _bitCnt; _bitCnt += 8;
            }
            int v = (int)(_bitBuf & ((1u << n) - 1));
            _bitBuf >>= n; _bitCnt -= n;
            return v;
        }

        private int Decode(Huff h)
        {
            int code = 0, first = 0, index = 0;
            for (int len = 1; len < 16; len++)
            {
                code |= Bits(1);
                int count = h.Count[len];
                if (code - count < first) return h.Symbol[index + (code - first)];
                index += count; first += count; first <<= 1; code <<= 1;
            }
            throw new InflateException();
        }

        private void Put(byte b)
        {
            if (OutLen >= _max) throw new InflateException();
            if (OutLen == Out.Length) Array.Resize(ref Out, Math.Min(_max, Out.Length * 2));
            Out[OutLen++] = b;
        }

        public void Run()
        {
            bool last;
            do
            {
                last = Bits(1) == 1;
                switch (Bits(2))
                {
                    case 0: Stored(); break;
                    case 1: Codes(FixedLen, FixedDist); break;
                    case 2: Dynamic(); break;
                    default: throw new InflateException();
                }
            } while (!last);
        }

        private void Stored()
        {
            _bitBuf = 0; _bitCnt = 0;
            if (Pos + 4 > _end) throw new InflateException();
            int len = _s[Pos] | _s[Pos + 1] << 8, nlen = _s[Pos + 2] | _s[Pos + 3] << 8;
            Pos += 4;
            if (len != (~nlen & 0xFFFF) || Pos + len > _end) throw new InflateException();
            for (int i = 0; i < len; i++) Put(_s[Pos++]);
        }

        private void Dynamic()
        {
            int nlen = Bits(5) + 257, ndist = Bits(5) + 1, ncode = Bits(4) + 4;
            if (nlen > 286 || ndist > 30) throw new InflateException();
            var lengths = new short[320];
            for (int i = 0; i < ncode; i++) lengths[Order[i]] = (short)Bits(3);
            var lencode = new Huff(320);
            if (lencode.Build(lengths, 0, 19) != 0) throw new InflateException();
            int idx = 0;
            while (idx < nlen + ndist)
            {
                int sym = Decode(lencode);
                if (sym < 16) lengths[idx++] = (short)sym;
                else
                {
                    short len = 0;
                    if (sym == 16) { if (idx == 0) throw new InflateException(); len = lengths[idx - 1]; sym = 3 + Bits(2); }
                    else if (sym == 17) sym = 3 + Bits(3);
                    else sym = 11 + Bits(7);
                    if (idx + sym > nlen + ndist) throw new InflateException();
                    while (sym-- > 0) lengths[idx++] = len;
                }
            }
            if (lengths[256] == 0) throw new InflateException();
            var lc = new Huff(288);
            int err = lc.Build(lengths, 0, nlen);
            if (err < 0 || (err > 0 && nlen - lc.Count[0] != 1)) throw new InflateException();
            var dc = new Huff(30);
            err = dc.Build(lengths, nlen, ndist);
            if (err < 0 || (err > 0 && ndist - dc.Count[0] != 1)) throw new InflateException();
            Codes(lc, dc);
        }

        private void Codes(Huff lc, Huff dc)
        {
            while (true)
            {
                int sym = Decode(lc);
                if (sym < 256) Put((byte)sym);
                else if (sym == 256) return;
                else
                {
                    sym -= 257;
                    if (sym >= 29) throw new InflateException();
                    int len = LBase[sym] + Bits(LExt[sym]);
                    int ds = Decode(dc);
                    if (ds >= 30) throw new InflateException();
                    int dist = DBase[ds] + Bits(DExt[ds]);
                    if (dist > OutLen) throw new InflateException();
                    while (len-- > 0) Put(Out[OutLen - dist]);
                }
            }
        }
    }
}
