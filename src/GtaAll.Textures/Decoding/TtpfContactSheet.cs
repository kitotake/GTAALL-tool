using System.Text;
using GtaAll.Core.Formats;
using GtaAll.Core.IO;

namespace GtaAll.Textures.Decoding;

public sealed record ContactSheet(string Html, int Shown, int Failed, IReadOnlyDictionary<string, int> Reasons);

/// <summary>Planche-contact HTML autonome (vignettes PNG intégrées) d'un magasin TTPF. Lecture seule sur la source.</summary>
public static class TtpfContactSheet
{
    public static ContactSheet Build(string ttpfPath, int max, CancellationToken ct = default, int thumb = 160)
    {
        var st = TtpfInspector.Read(ttpfPath);
        using var fs = SafeFileAccess.OpenRead(ttpfPath);
        var cards = new StringBuilder();
        int ok = 0, fail = 0;
        var reasons = new Dictionary<string, int>();
        foreach (var t in st.Textures)
        {
            if (ok >= max) break;
            ct.ThrowIfCancellationRequested();
            var img = TtpfImages.DecodeMip0(fs, st, t, out var problem);
            if (img == null) { fail++; reasons[problem ?? "?"] = reasons.GetValueOrDefault(problem ?? "?") + 1; continue; }
            ok++;
            var (rgba, tw, th) = ImageFiles.Thumbnail(img.Rgba, img.Width, img.Height, thumb);
            cards.Append("<figure><img width=\"").Append(tw).Append("\" height=\"").Append(th).Append("\" src=\"data:image/png;base64,")
                 .Append(Convert.ToBase64String(ImageFiles.PngBytes(rgba, tw, th))).Append("\"><figcaption>")
                 .Append(t.Hash.ToString("X16")).Append("<br>").Append(t.Width).Append('×').Append(t.Height).Append(' ')
                 .Append(System.Net.WebUtility.HtmlEncode(t.FormatName)).Append("</figcaption></figure>\n");
        }
        var html = "<!doctype html><meta charset=\"utf-8\"><title>Aperçu TTPF</title><style>body{font:13px sans-serif;background:#2b2b2b;color:#ddd}" +
                   "figure{display:inline-block;margin:6px;text-align:center;width:170px;vertical-align:top}" +
                   "img{background:repeating-conic-gradient(#555 0 25%,#777 0 50%) 0 0/16px 16px}</style>" +
                   $"<h3>{System.Net.WebUtility.HtmlEncode(Path.GetFileName(ttpfPath))} — {ok} textures affichées sur {st.Textures.Count} (mip 0, vignettes ≤ {thumb} px ; transparence en damier)" +
                   (fail > 0 ? $" — {fail} non décodée(s)" : "") + "</h3>\n" + cards;
        return new ContactSheet(html, ok, fail, reasons);
    }
}
