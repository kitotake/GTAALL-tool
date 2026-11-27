using GtaAll.Core.Formats;

namespace GtaAll.Textures.Decoding;

/// <summary>Une ligne de la liste de textures d'un magasin TTPF (aucun pixel lu : seulement l'index et l'état de décodage).</summary>
public sealed record TextureRow(int Index, TtpfTexture Texture, string? Reason)
{
    public string Hash => Texture.Hash.ToString("X16");
    public string SizeText => $"{Texture.Width}×{Texture.Height}";
    public string FormatText => Texture.FormatName;
    public int Mips => Texture.Mips;
    public bool Decodable => Reason == null;
    public string State => Reason ?? "ok";

    /// <summary>Texte parcouru par le filtre : hash, taille (avec « x » et « × »), format, état.</summary>
    public string SearchText => $"{Hash} {Texture.Width}x{Texture.Height} {Texture.Width}×{Texture.Height} {Texture.FormatName} {State}";
}

/// <summary>Liste, filtre et description des textures d'un magasin TTPF, pour l'interface.</summary>
public static class TextureCatalog
{
    public static List<TextureRow> Build(TtpfStore st)
    {
        var rows = new List<TextureRow>(st.Textures.Count);
        for (int i = 0; i < st.Textures.Count; i++)
            rows.Add(new TextureRow(i, st.Textures[i], TtpfImages.Unavailable(st, st.Textures[i])));
        return rows;
    }

    /// <summary>
    /// Garde les lignes qui contiennent tous les mots de <paramref name="query"/> (sans tenir compte de la casse ; un préfixe « 0x »
    /// devant un mot est ignoré). <paramref name="onlyDecodable"/> écarte les textures qu'on ne sait pas décoder.
    /// </summary>
    public static List<TextureRow> Filter(IReadOnlyList<TextureRow> rows, string? query, bool onlyDecodable)
    {
        var terms = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? t[2..] : t)
            .Where(t => t.Length > 0).ToArray();
        var res = new List<TextureRow>();
        foreach (var r in rows)
        {
            if (onlyDecodable && !r.Decodable) continue;
            var hay = r.SearchText;
            if (terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase))) res.Add(r);
        }
        return res;
    }

    /// <summary>Description multi-lignes d'une texture, pour le panneau d'information.</summary>
    public static string Describe(TextureRow r)
    {
        var t = r.Texture;
        return string.Join("\n",
            $"Hash      : {r.Hash}",
            $"Taille    : {t.Width}×{t.Height}",
            $"Format    : {t.FormatName} (DXGI {t.DxgiFormat})",
            $"Mips      : {t.Mips}",
            $"Type      : {t.KindName}, {t.Slices} tranche(s)",
            $"Tuilage   : {t.TileMode}",
            $"État      : {(r.Decodable ? "décodable" : r.State)}");
    }
}
