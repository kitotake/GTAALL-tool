using System.IO.Compression;
using GtaAll.Core.Formats;

namespace GtaAll.Textures.Decoding;

public sealed record DecodedTexture(int Width, int Height, byte[] Rgba, TexFmt Format, byte[] RawElements);

/// <summary>Lecture de l'espace virtuel d'un magasin TTPF et décodage de la mip 0 d'une texture (lecture seule).</summary>
public static class TtpfImages
{
    /// <summary>
    /// Lit [virt, virt+len) de l'espace virtuel. Blocs zlib décompressés, blocs « stockés » copiés. Les blocs BCPack ne sont pas décodés :
    /// leurs octets restent à zéro et sont comptés dans <paramref name="missing"/>.
    /// </summary>
    public static byte[] ReadVirtual(Stream fs, TtpfStore st, long virt, long len, out long missing, TtpfChunkCache? cache = null)
    {
        var res = new byte[len];
        missing = 0;
        long end = virt + len;
        var ch = st.Chunks;
        for (int i = 0; i + 1 < ch.Count; i++)
        {
            long v0 = ch[i].VirtualOffset, v1 = ch[i + 1].VirtualOffset;
            if (v1 <= virt || v0 >= end) continue;
            long s = Math.Max(virt, v0), e = Math.Min(end, v1);
            int flags = ch[i].Flags;
            if (flags is not (0x00 or 0x20)) { missing += e - s; continue; }
            long fileLen = ch[i + 1].FileOffset - ch[i].FileOffset - ch[i].Padding;
            if (fileLen <= 0 || fileLen > 256L << 20 || v1 - v0 > 1L << 30) { missing += e - s; continue; }
            byte[]? data;
            if (cache == null || !cache.TryGet(i, out data))
            {
                data = LoadChunk(fs, ch[i].FileOffset, fileLen, flags, v1 - v0);
                cache?.Put(i, data);
            }
            if (data == null) { missing += e - s; continue; }
            long from = s - v0, n = Math.Min(e - s, data.Length - from);
            if (n < 0) n = 0;
            Array.Copy(data, from, res, s - virt, n);
            missing += (e - s) - n;
        }
        return res;
    }

    /// <summary>Lit un bloc du fichier et le décompresse si besoin. Retourne null si le flux zlib est invalide.</summary>
    private static byte[]? LoadChunk(Stream fs, long fileOffset, long fileLen, int flags, long virtLen)
    {
        var raw = new byte[fileLen];
        fs.Position = fileOffset;
        fs.ReadExactly(raw);
        if (flags != 0x20) return raw;
        using var z = new ZLibStream(new MemoryStream(raw), CompressionMode.Decompress);
        var ms = new MemoryStream((int)virtLen);
        try { z.CopyTo(ms); } catch (InvalidDataException) { return null; }
        return ms.ToArray();
    }

    /// <summary>
    /// Dit, sans lire aucun pixel, pourquoi la mip <paramref name="mip"/> ne peut pas être décodée (null = décodable en principe :
    /// type, format et blocs sont pris en charge). Mêmes règles que <see cref="DecodeMip"/>.
    /// </summary>
    public static string? Unavailable(TtpfStore st, TtpfTexture t, int mip = 0)
    {
        if (mip < 0 || mip >= t.Mips) return "mip inexistante";
        if (t.Kind != 2 || t.Slices != 1) return $"type {t.KindName} / {t.Slices} tranche(s) : non géré";
        var fmt = TextureLayouts.FromDxgi(t.DxgiFormat);
        if (fmt is not (TexFmt.Bc1 or TexFmt.Bc3 or TexFmt.Bc4 or TexFmt.Bc5 or TexFmt.Bc7 or TexFmt.Rgba8 or TexFmt.Bgra8 or TexFmt.R8 or TexFmt.A8))
            return $"format {t.FormatName} non décodé";
        var layout = TextureLayouts.LayoutFor(t, t.Mips);
        if (layout == null) return $"tuilage {t.TileMode} ou format {t.FormatName} non géré";
        var (a, b) = layout.MipSpan(mip);
        long start = t.Virt + a, end = t.Virt + b;
        var ch = st.Chunks;
        for (int i = 0; i + 1 < ch.Count; i++)
        {
            long v0 = ch[i].VirtualOffset, v1 = ch[i + 1].VirtualOffset;
            if (v1 <= start || v0 >= end) continue;
            if (ch[i].Flags is not (0x00 or 0x20)) return "en blocs BCPack (non décodés)";
        }
        return null;
    }

    /// <summary>Décode la mip 0 (2D, une tranche). Retourne null et un message si la texture n'est pas prise en charge.</summary>
    public static DecodedTexture? DecodeMip0(Stream fs, TtpfStore st, TtpfTexture t, out string? problem, TtpfChunkCache? cache = null) => DecodeMip(fs, st, t, 0, out problem, cache);

    /// <summary>Décode la mip <paramref name="mip"/> (0 = pleine résolution).</summary>
    public static DecodedTexture? DecodeMip(Stream fs, TtpfStore st, TtpfTexture t, int mip, out string? problem, TtpfChunkCache? cache = null)
    {
        problem = null;
        if (mip < 0 || mip >= t.Mips) { problem = "mip inexistante"; return null; }
        if (t.Kind != 2 || t.Slices != 1) { problem = $"type {t.KindName} / {t.Slices} tranche(s) : non géré"; return null; }
        var fmt = TextureLayouts.FromDxgi(t.DxgiFormat);
        var layout = TextureLayouts.LayoutFor(t, t.Mips);
        if (layout == null) { problem = $"tuilage {t.TileMode} ou format {t.FormatName} non géré"; return null; }
        var (a, b) = layout.MipSpan(mip);
        if (t.Virt + b > st.VirtualSizeField + (1L << 32) * 8) { problem = "adresse hors de l'espace virtuel"; return null; }
        var data = ReadVirtual(fs, st, t.Virt + a, b - a, out long missing, cache);
        if (missing > 0) { problem = $"{missing:N0} octets en blocs BCPack (non décodés)"; return null; }
        int mw = Math.Max(1, t.Width >> mip), mh = Math.Max(1, t.Height >> mip);
        var el = layout.Untile(data, a, mip);
        byte[] rgba;
        switch (fmt)
        {
            case TexFmt.Bc1: rgba = BcDecoder.Decode(BcDecoder.Format.Bc1, el, mw, mh); break;
            case TexFmt.Bc3: rgba = BcDecoder.Decode(BcDecoder.Format.Bc3, el, mw, mh); break;
            case TexFmt.Bc4: rgba = BcDecoder.Decode(BcDecoder.Format.Bc4, el, mw, mh); break;
            case TexFmt.Bc5: rgba = BcDecoder.Decode(BcDecoder.Format.Bc5, el, mw, mh); break;
            case TexFmt.Bc7: rgba = BcDecoder.Decode(BcDecoder.Format.Bc7, el, mw, mh); break;
            case TexFmt.Rgba8: rgba = el; break;
            case TexFmt.Bgra8: rgba = (byte[])el.Clone(); for (int i = 0; i + 3 < rgba.Length; i += 4) (rgba[i], rgba[i + 2]) = (rgba[i + 2], rgba[i]); break;
            case TexFmt.R8:
            case TexFmt.A8:
                rgba = new byte[el.Length * 4];
                for (int i = 0; i < el.Length; i++) { rgba[i * 4] = rgba[i * 4 + 1] = rgba[i * 4 + 2] = el[i]; rgba[i * 4 + 3] = 255; }
                break;
            default: problem = $"format {t.FormatName} non décodé"; return null;
        }
        return new DecodedTexture(mw, mh, rgba, fmt, el);
    }
}
