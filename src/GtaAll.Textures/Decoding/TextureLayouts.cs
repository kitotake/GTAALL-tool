using GtaAll.Core.Formats;

namespace GtaAll.Textures.Decoding;

/// <summary>Famille de format de pixels d'une texture TTPF (valeurs DXGI).</summary>
public enum TexFmt { Unknown, Bc1, Bc2, Bc3, Bc4, Bc5, Bc6, Bc7, Rgba8, Bgra8, R8, A8, Rg8, R16f, R16, Rgba16, Rgba16f }

public static class TextureLayouts
{
    public static TexFmt FromDxgi(int f) => f switch
    {
        >= 70 and <= 72 => TexFmt.Bc1, >= 73 and <= 75 => TexFmt.Bc2, >= 76 and <= 78 => TexFmt.Bc3,
        >= 79 and <= 81 => TexFmt.Bc4, >= 82 and <= 84 => TexFmt.Bc5, 94 or 95 or 96 => TexFmt.Bc6, >= 97 and <= 99 => TexFmt.Bc7,
        >= 27 and <= 32 => TexFmt.Rgba8, 87 or 88 or 90 or 91 => TexFmt.Bgra8,
        >= 60 and <= 64 => TexFmt.R8, 65 => TexFmt.A8, >= 48 and <= 52 => TexFmt.Rg8,
        54 => TexFmt.R16f, 53 or 56 or 57 => TexFmt.R16, 11 => TexFmt.Rgba16, 10 => TexFmt.Rgba16f,
        _ => TexFmt.Unknown
    };

    public static int BlockDim(TexFmt f) => f is TexFmt.Bc1 or TexFmt.Bc2 or TexFmt.Bc3 or TexFmt.Bc4 or TexFmt.Bc5 or TexFmt.Bc6 or TexFmt.Bc7 ? 4 : 1;

    public static int BytesPerElement(TexFmt f) => f switch
    {
        TexFmt.Bc1 or TexFmt.Bc4 => 8, TexFmt.Bc2 or TexFmt.Bc3 or TexFmt.Bc5 or TexFmt.Bc6 or TexFmt.Bc7 => 16,
        TexFmt.Rgba8 or TexFmt.Bgra8 => 4, TexFmt.Rg8 or TexFmt.R16f or TexFmt.R16 => 2, TexFmt.Rgba16 or TexFmt.Rgba16f => 8,
        TexFmt.R8 or TexFmt.A8 => 1, _ => 0
    };

    /// <summary>Nombre de mips : champ + 1, sauf tile 1 avec champ 1 (une seule mip, mesuré). Cubes/volumes : non gérés ici.</summary>
    public static int MipCount(TtpfTexture t) => t.TileMode == 1 && t.MipField == 1 ? 1 : t.MipField + 1;

    /// <summary>Disposition d'une texture 2D (tile 0-3), ou null si le type, le mode ou le format n'est pas géré.</summary>
    public static Gfx10Layout? LayoutFor(TtpfTexture t, int mips)
    {
        if (t.Kind is not (2 or 10) || t.TileMode > 3) return null;
        var f = FromDxgi(t.DxgiFormat);
        int bpe = BytesPerElement(f);
        if (bpe == 0) return null;
        int bd = BlockDim(f);
        return Gfx10Layout.Create(t.TileMode == 0, (t.Width + bd - 1) / bd, (t.Height + bd - 1) / bd, mips, bpe);
    }

    /// <summary>
    /// Renseigne <see cref="TtpfTexture.Mips"/> et <see cref="TtpfTexture.DataSize"/>. Le champ de mips du descripteur sous-estime
    /// souvent : le magasin range chaque chaîne jusqu'à la texture suivante ; si la taille du champ n'atteint pas la suivante
    /// et qu'un seul nombre de mips l'atteint exactement, ce nombre est retenu (même méthode que SixNG).
    /// </summary>
    public static List<TtpfTexture> Resolve(List<TtpfTexture> textures, long virtSize)
    {
        var res = textures.Select(t =>
        {
            int m = MipCount(t);
            var l = LayoutFor(t, m);
            return t with { Mips = m, DataSize = l?.TotalFor(t.Slices) ?? 0 };
        }).ToList();
        var order = Enumerable.Range(0, res.Count).OrderBy(i => res[i].Virt).ToList();
        for (int k = 0; k + 1 < order.Count; k++)
        {
            int i = order[k]; var t = res[i];
            long gap = res[order[k + 1]].Virt - t.Virt;
            if (gap == t.DataSize || t.TileMode > 3 || t.Kind == 6) continue;
            var f = FromDxgi(t.DxgiFormat);
            if (f == TexFmt.Unknown) continue;
            int full = 32 - System.Numerics.BitOperations.LeadingZeroCount((uint)Math.Max(t.Width, t.Height));
            var fits = Enumerable.Range(1, full).Where(m => LayoutFor(t, m)?.TotalFor(t.Slices) == gap).ToList();
            if (fits.Count == 1) res[i] = t with { Mips = fits[0], DataSize = gap };
        }
        return res;
    }
}
